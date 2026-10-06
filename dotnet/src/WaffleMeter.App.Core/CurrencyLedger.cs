using WaffleMeter.Capture;

namespace WaffleMeter.App.Core;

/// <summary>One tracked balance as the session currently knows it: the sum of that item's stacks in one
/// container, stamped with when the server last stated it.</summary>
public readonly record struct CurrencyBalance(int ItemId, int Container, long Count, long ObservedAtMs);

/// <summary>
/// The session's view of the tracked currency stacks — what the panel's store gets filed from.
///
/// <para><b>Keyed by item key, summed per (item, container).</b> 0x561B states each stack's ABSOLUTE count under
/// its item key, and the key is not a stable name for a balance: depositing the 서버 창고 kinah removed one stack
/// and created another under a NEW key (2026-10-07, 0x1EA000017884D → 0x2400000128B82). A ledger keyed by item id
/// would have doubled or lost the warehouse on the first round trip; one keyed by item key and summed per container
/// gets 4,454,882 → 329,454,882 → 4,454,882 and 325,000,000 → 0 → 325,000,000 exactly.</para>
///
/// <para><b>Known vs. zero.</b> A balance is only reported once something has actually stated it. An EXACT 0x5611
/// snapshot states all of them — the game omits an empty stack, so absent means zero. A fallback snapshot states
/// only what its scan found, and a 0x561B change states the balances it touches; everything else stays unknown,
/// which is filed as nothing at all rather than as zero. A meter started mid-session (no snapshot until the next
/// world entry) therefore fills in balance by balance as they change, and never wipes the rest.</para>
///
/// <para><b>Whose it is.</b> The snapshot lands ~7 s before the own-load packet that names its character — the
/// 0x610B trap again — so filing waits on <see cref="WeeklyContentOwnership.CanFile"/>. Changes that arrive while
/// it waits update the ledger, not the store: they belong to the character the snapshot describes, and filing them
/// on arrival would write them under the one being left. Once nothing is pending, a change is filed at once.</para>
///
/// Pure (no clock, no settings) so the attribution rules are unit-testable; the app owns one per session and
/// drives it from the UI thread.
/// </summary>
public sealed class CurrencyLedger
{
    private readonly Dictionary<long, CurrencyItem> _stacks = new();

    /// <summary>(item, container) → when that balance was last stated. A key here is a balance that is KNOWN,
    /// possibly to be zero; a balance not here is unknown.</summary>
    private readonly Dictionary<(int ItemId, int Container), long> _known = new();

    private long _pendingSnapshotAtMs;
    private long _lastSnapshotAtMs;
    private bool _dirty;

    /// <summary>Whether a snapshot is waiting for the identity it belongs to.</summary>
    public bool HasPendingSnapshot => _pendingSnapshotAtMs > 0;

    /// <summary>A 0x5611 world-entry snapshot. Always starts the ledger over — a world entry can be a different
    /// character, and nothing from before it may leak into what gets filed next.</summary>
    public void ApplySnapshot(IReadOnlyList<CurrencyItem> items, bool exact, long atMs)
    {
        _stacks.Clear();
        _known.Clear();

        foreach (CurrencyItem item in items)
        {
            _stacks[item.ItemKey] = item;
            if (!exact)
            {
                _known[(item.ItemId, item.Container)] = atMs; // a fallback vouches only for what it found
            }
        }

        if (exact)
        {
            foreach (CurrencyInfo info in CurrencyCatalog.All)
            {
                _known[(info.ItemId, info.Container)] = atMs; // absent from a complete list = zero
            }
        }

        _pendingSnapshotAtMs = atMs;
        _lastSnapshotAtMs = atMs;
        _dirty = true;
    }

    /// <summary>0x561B changes: Add/Update set that stack's absolute count, Remove drops it. Every balance a
    /// change touches becomes known — including the one a stack left, when a key moves containers.</summary>
    public void ApplyChanges(IReadOnlyList<CurrencyItemChange> changes, long atMs)
    {
        foreach (CurrencyItemChange change in changes)
        {
            CurrencyItem item = change.Item;
            if (_stacks.Remove(item.ItemKey, out CurrencyItem previous))
            {
                Touch(previous.ItemId, previous.Container, atMs);
            }

            if (change.Type != ItemChangeType.Remove)
            {
                _stacks[item.ItemKey] = item;
            }

            // A Remove of a stack this session never saw still says something: that stack is gone. Currency stacks
            // are one per container in every capture, so what is left of the balance is what the ledger holds —
            // which is how withdrawing the whole warehouse reads as 0 even on a meter started mid-session.
            Touch(item.ItemId, item.Container, atMs);
            _dirty = true;
        }
    }

    /// <summary>
    /// Whether there is something to file now, and what. <paramref name="identityAtMs"/> is when the current
    /// character was last identified (0 = never); the caller must already know WHO that is.
    /// <para>A pending snapshot is filed only once <see cref="WeeklyContentOwnership.CanFile"/> says its naming
    /// packet has arrived (or waiting has stopped making sense). With none pending, any change since the last
    /// filing is filed at once. Filing hands back every KNOWN balance; the store skips the ones that did not
    /// move.</para>
    /// </summary>
    public bool TryTakeFiling(long identityAtMs, long nowMs, out IReadOnlyList<CurrencyBalance> balances)
    {
        balances = [];
        if (_pendingSnapshotAtMs > 0)
        {
            if (!WeeklyContentOwnership.CanFile(_pendingSnapshotAtMs, identityAtMs, nowMs))
            {
                return false; // still waiting for the character this snapshot describes
            }

            _pendingSnapshotAtMs = 0;
        }
        else if (!_dirty)
        {
            return false;
        }

        _dirty = false;
        balances = KnownBalances();
        return balances.Count > 0;
    }

    /// <summary>
    /// A DIFFERENT character just took over. If a snapshot landed shortly before — within the same window
    /// <see cref="WeeklyContentOwnership.SettleMs"/> allows — it is the incoming character's (the snapshot always
    /// beats its naming packet) and is kept for filing. Anything else describes the character being left and is
    /// dropped, so a change that arrives next cannot be summed onto the previous character's stacks.
    /// </summary>
    public void OnCharacterSwitch(long identityAtMs)
    {
        if (_lastSnapshotAtMs > 0
            && identityAtMs >= _lastSnapshotAtMs
            && identityAtMs - _lastSnapshotAtMs <= WeeklyContentOwnership.SettleMs)
        {
            return;
        }

        Reset();
    }

    /// <summary>Forget everything — no balance known, nothing pending.</summary>
    public void Reset()
    {
        _stacks.Clear();
        _known.Clear();
        _pendingSnapshotAtMs = 0;
        _lastSnapshotAtMs = 0;
        _dirty = false;
    }

    /// <summary>Every tracked balance the session can state, in catalog order. Unknown ones are left out, never
    /// reported as zero.</summary>
    public IReadOnlyList<CurrencyBalance> KnownBalances()
    {
        var balances = new List<CurrencyBalance>();
        foreach (CurrencyInfo info in CurrencyCatalog.All)
        {
            if (!_known.TryGetValue((info.ItemId, info.Container), out long observedAtMs))
            {
                continue;
            }

            long count = 0;
            foreach (CurrencyItem stack in _stacks.Values)
            {
                if (stack.ItemId == info.ItemId && stack.Container == info.Container)
                {
                    count += stack.Count;
                }
            }

            balances.Add(new CurrencyBalance(info.ItemId, info.Container, count, observedAtMs));
        }

        return balances;
    }

    private void Touch(int itemId, int container, long atMs) =>
        _known[(itemId, container)] = _known.TryGetValue((itemId, container), out long at) ? Math.Max(at, atMs) : atMs;
}

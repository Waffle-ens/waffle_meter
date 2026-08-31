using WaffleMeter.Capture;

namespace WaffleMeter.Data;

/// <summary>The local player's stat sheet, as last broadcast by the server.</summary>
/// <param name="Values">stat id -&gt; raw signed value. Percent-ish ids are basis points — see
/// <see cref="PlayerStatIds.IsPercent"/>; the raw values are kept so an id we cannot name yet still survives
/// to the clipboard, where it can be compared against the in-game stat window later.</param>
/// <param name="UpdatedAt">Capture-clock ms of the most recent frame folded into this sheet.</param>
/// <param name="FullSnapshotSeen">
/// Whether a FULL sheet (0x3649) has been folded in, as opposed to only incremental updates. This matters for
/// honesty, not correctness: the server sends the full sheet on character load, and after that only what
/// changed. A meter started mid-session therefore holds a partial sheet, and telling the user "복사할 수
/// 있습니다" from a partial sheet would hand the calculator a form with holes in it that look like zeroes.
/// </param>
public sealed record PlayerStatSheet(
    IReadOnlyDictionary<int, int> Values,
    long UpdatedAt,
    bool FullSnapshotSeen)
{
    public int? Raw(int statId) => Values.TryGetValue(statId, out int v) ? v : null;

    /// <summary>A basis-point stat as a percentage (6613 → 66.13), or null when absent.</summary>
    public double? Percent(int statId) => Raw(statId) is { } v ? v / 100.0 : null;

    /// <summary>쿨타임 감소(%). 서버는 두 항목(기본 + 추가)에 나눠 "감소량"을 양수로 싣는다 — 사람이 읽는 쪽에서
    /// 자연스러운 음수로 뒤집어 돌려준다. 둘 다 없으면 null.</summary>
    public double? CooldownPercent()
    {
        int? baseValue = Raw(PlayerStatIds.CooldownBasePercent);
        int? bonus = Raw(PlayerStatIds.CooldownBonusPercent);
        if (baseValue is null && bonus is null) return null;
        return -((baseValue ?? 0) + (bonus ?? 0)) / 100.0;
    }
}

/// <summary>
/// Accumulates the character stat dictionary the server broadcasts (0x364A deltas / 0x3649 full snapshots)
/// for the LOCAL player only.
///
/// <para><b>Why it buffers.</b> The stat frames arrive BEFORE the packet that says which entity is us — the
/// own-load broadcast is a single easily-lost packet and, measured on a live capture, the stat sheet leads it
/// by about six seconds. Dropping everything that arrives while the executor is unknown would mean the sheet
/// is only ever complete on a lucky login. So frames for not-yet-identified entities are held, and replayed
/// once the executor is confirmed. The hold is bounded by count and age so a busy zone cannot grow it without
/// limit — every nearby player's incremental updates come through the same opcode.</para>
///
/// <para><b>What it does not do.</b> It never guesses which entity is the player. Only an entity the identity
/// layer confirms is promoted, so a stranger's stat frame can never become "your stats" — the failure mode
/// that a garbage-identity path caused before (see the executor-hijack fix).</para>
/// </summary>
public sealed class PlayerStatStore
{
    /// <summary>How many unidentified entities to hold stats for. A party/raid plus a few strangers; past this
    /// the oldest is dropped. Generous enough that the local player is never the one evicted in practice.</summary>
    private const int MaxPendingEntities = 16;

    /// <summary>How long an unidentified entity's stats are worth keeping. The measured lead is ~6 s; a minute
    /// covers a slow zone load without holding a whole session's strangers.</summary>
    private const long PendingTtlMs = 60_000L;

    private readonly object _gate = new();
    private readonly Dictionary<int, int> _values = new();
    private readonly Dictionary<int, (Dictionary<int, int> Values, long At, bool Full)> _pending = new();

    private int _ownerId;
    private long _updatedAt;
    private bool _fullSeen;

    /// <summary>Raised when the local player's sheet changed, so a settings screen can refresh.</summary>
    public event Action? Changed;

    /// <summary>The local player's sheet, or null when nothing has been captured for them yet.</summary>
    public PlayerStatSheet? Current
    {
        get
        {
            lock (_gate)
            {
                return _values.Count == 0
                    ? null
                    : new PlayerStatSheet(new Dictionary<int, int>(_values), _updatedAt, _fullSeen);
            }
        }
    }

    /// <summary>Fold one captured frame in. <paramref name="entityId"/> 0 means the frame carried no entity —
    /// a full snapshot, which is the local player's by construction, but only usable once we know who that is.</summary>
    public void Accept(int entityId, IReadOnlyList<(int Stat, int Value)> stats, bool fullSnapshot, long arrivedAt)
    {
        if (stats.Count == 0) return;

        bool changed = false;
        lock (_gate)
        {
            // A full snapshot with no entity id belongs to whoever we currently believe we are. If that is not
            // known yet, park it under a reserved key so it is replayed on confirmation like any other.
            int key = entityId != 0 ? entityId : _ownerId;

            if (_ownerId != 0 && key == _ownerId)
            {
                Apply(stats, fullSnapshot, arrivedAt);
                changed = true;
            }
            else
            {
                Park(entityId, stats, fullSnapshot, arrivedAt);
            }
        }

        if (changed) Changed?.Invoke();
    }

    /// <summary>Tell the store who the local player is. Anything parked for that entity — plus any entity-less
    /// full snapshot — is replayed in arrival order. A different owner clears the sheet: the stats belong to a
    /// character, and carrying one character's numbers onto another is worse than showing none.</summary>
    public void SetOwner(int ownerId)
    {
        if (ownerId <= 0) return;

        bool changed = false;
        lock (_gate)
        {
            if (_ownerId != ownerId)
            {
                _values.Clear();
                _fullSeen = false;
                _updatedAt = 0;
                _ownerId = ownerId;
                changed = true;
            }

            // The entity-less full snapshot (key 0) is the local player's by construction; replay it first so a
            // later delta wins over it.
            foreach (int key in new[] { 0, ownerId })
            {
                if (!_pending.Remove(key, out (Dictionary<int, int> Values, long At, bool Full) held)) continue;

                Apply(held.Values.Select(kv => (kv.Key, kv.Value)).ToList(), held.Full, held.At);
                changed = true;
            }

            Prune(_updatedAt);
        }

        if (changed) Changed?.Invoke();
    }

    private void Apply(IReadOnlyList<(int Stat, int Value)> stats, bool fullSnapshot, long arrivedAt)
    {
        // A full snapshot REPLACES: it is the whole sheet, so a stat that disappeared (an unequipped item's
        // bonus) has to disappear here too. A delta merges.
        if (fullSnapshot)
        {
            _values.Clear();
            _fullSeen = true;
        }

        foreach ((int stat, int value) in stats)
        {
            _values[stat] = value;
        }

        if (arrivedAt > _updatedAt) _updatedAt = arrivedAt;
    }

    private void Park(int entityId, IReadOnlyList<(int Stat, int Value)> stats, bool fullSnapshot, long arrivedAt)
    {
        if (!_pending.TryGetValue(entityId, out (Dictionary<int, int> Values, long At, bool Full) held))
        {
            held = (new Dictionary<int, int>(), arrivedAt, false);
        }

        if (fullSnapshot) held.Values.Clear();
        foreach ((int stat, int value) in stats)
        {
            held.Values[stat] = value;
        }

        _pending[entityId] = (held.Values, arrivedAt, held.Full || fullSnapshot);
        Prune(arrivedAt);
    }

    private void Prune(long now)
    {
        foreach (int key in _pending
                     .Where(kv => now - kv.Value.At > PendingTtlMs)
                     .Select(kv => kv.Key)
                     .ToList())
        {
            _pending.Remove(key);
        }

        while (_pending.Count > MaxPendingEntities)
        {
            int oldest = _pending.OrderBy(kv => kv.Value.At).First().Key;
            _pending.Remove(oldest);
        }
    }
}

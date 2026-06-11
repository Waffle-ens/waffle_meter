using WaffleMeter.Capture;
using WaffleMeter.Data;

namespace WaffleMeter.App.Core;

/// <summary>
/// The single source of truth for pending party-join requests. Kotlin had no server-side list (the
/// React UI owned it); the .NET port has no web UI, so this lives here. Keyed by Requester (newest
/// wins). A request lives 20s — modeled here in <see cref="Snapshot"/> so a stale row never survives
/// even if the UI countdown tick stalls; the visible per-row bar is driven separately by the panel.
/// Mutated on the meter-consumer thread; <see cref="Changed"/> is marshalled to the UI by the caller.
/// </summary>
public sealed class JoinRequestStore
{
    public const long LifetimeMs = 20_000L;

    private readonly object _gate = new();
    private readonly Dictionary<int, JoinRequestUser> _byRequester = new();
    private readonly Func<long> _now;

    public JoinRequestStore(Func<long>? now = null)
        => _now = now ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

    /// <summary>Raised when the request set changes (add / remove / refuse) — re-render, stay open.</summary>
    public event Action? Changed;

    /// <summary>Raised on a full clear (instance start / party exit) — the panel closes (web isOpen=false).
    /// Kept distinct from <see cref="Changed"/> so a remove that empties the list leaves the panel open
    /// (web parity: only clearAll closes it).</summary>
    public event Action? Cleared;

    /// <summary>Add or replace by requester (newest wins / re-arm the timer).</summary>
    public void Add(JoinRequestUser u)
    {
        lock (_gate) _byRequester[u.Requester] = u;
        Changed?.Invoke();
    }

    /// <summary>Cancel + admit both remove by requester id.</summary>
    public void Remove(int requester)
    {
        bool changed;
        lock (_gate) changed = _byRequester.Remove(requester);
        if (changed) Changed?.Invoke();
    }

    /// <summary>Refuse carries no id — drop the oldest pending request.</summary>
    public void RefuseOldest()
    {
        bool changed = false;
        lock (_gate)
        {
            if (_byRequester.Count > 0)
            {
                int key = _byRequester.Values.OrderBy(r => r.ArrivedAt).First().Requester;
                changed = _byRequester.Remove(key);
            }
        }
        if (changed) Changed?.Invoke();
    }

    /// <summary>Instance start + party exit — clear everything and close the panel (fires
    /// <see cref="Cleared"/>, not <see cref="Changed"/>).</summary>
    public void ClearAll()
    {
        lock (_gate) _byRequester.Clear();
        Cleared?.Invoke();
    }

    /// <summary>Newest-first, with entries older than 20s dropped.</summary>
    public IReadOnlyList<JoinRequestUser> Snapshot()
    {
        long cutoff = _now() - LifetimeMs;
        lock (_gate)
        {
            return _byRequester.Values
                .Where(r => r.ArrivedAt >= cutoff)
                .OrderByDescending(r => r.ArrivedAt)
                .ToList();
        }
    }
}

/// <summary>
/// Bridges the primitives-only <see cref="IJoinRequestSink"/> (raised by the capture pipeline) to the
/// domain <see cref="JoinRequestStore"/>: resolves the job code to its Korean class name and builds the
/// <see cref="JoinRequestUser"/> record. Keeps <c>WaffleMeter.Capture</c> free of a data-layer dependency.
/// </summary>
public sealed class JoinRequestSinkAdapter(JoinRequestStore store, DataManager data) : IJoinRequestSink
{
    private const long EnrichWindowMs = 20_000L;

    public void OnJoinRequest(int requester, string nickname, int jobCode, int server, int power, long arrivedAt)
    {
        JobClass? job = JobClassInfo.ConvertFromCode(jobCode);
        var user = new JoinRequestUser
        {
            Requester = requester,
            Nickname = nickname,
            Job = job?.ClassName(),
            Server = server,
            Power = power,
            ArrivedAt = arrivedAt,
        };
        store.Add(user);

        // Enrich with the official-site skills (live only; no-op offline). The callback fires on a
        // background thread; store.Add is thread-safe and re-renders the card with the skill badges.
        data.RequestOfficialCharacterLookup(requester, nickname, server, job, info =>
        {
            if (info.Skills.Count == 0)
            {
                return;
            }

            if (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - arrivedAt > EnrichWindowMs)
            {
                return; // request already expired
            }

            store.Add(user with
            {
                Skill = new Dictionary<int, int>(info.Skills),
                Power = user.Power > 0 ? user.Power : info.Power,
                Job = user.Job ?? info.Job?.ClassName(),
            });
        });
    }

    public void OnJoinRequestRemove(int requester) => store.Remove(requester);
    public void OnRefuseJoinRequest() => store.RefuseOldest();
    public void OnExitPartyUi() => store.ClearAll();
}

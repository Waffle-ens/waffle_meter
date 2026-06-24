namespace WaffleMeter.Data;

/// <summary>
/// Verbatim port of Kotlin UserRepository. Identity (nickname/server/job) and combat power can
/// arrive in separate packets, so power that arrives before identity is held in "pending" maps and
/// merged in when the user is created. mergeInto fills only missing fields (never overwrites a known
/// nickname/server; an authoritative job overrides an inferred one; power always takes the latest &gt; 0).
/// </summary>
public sealed class UserRepository
{
    private readonly Dictionary<int, User> _storage = new();
    private readonly Dictionary<string, User> _pendingByNameServer = new();
    private readonly Dictionary<int, User> _pendingById = new();
    private readonly Dictionary<string, User> _pendingByNickname = new();
    private int _executor;

    // Per-identity (name+server) index of uids, ordered oldest-first / newest-last. The self re-registers under
    // a FRESH uid on every zone/instance load (0x3633), so without bounding, its prior User objects accumulate
    // without limit (5+ in one session) and a plain name lookup picks an arbitrary stale one. This index lets
    // FindByNicknameAndServer return the NEWEST (= live) instance and caps each identity to MaxUidsPerIdentity,
    // evicting the long-dead oldest while keeping a small buffer so a late in-flight packet for the just-replaced
    // uid still resolves. _uidIdentity tracks each uid's current group so a reused uid is re-grouped cleanly.
    private const int MaxUidsPerIdentity = 3;
    private readonly Dictionary<string, List<int>> _idIndex = new();
    private readonly Dictionary<int, string> _uidIdentity = new();

    public User? Save(int key, User value)
    {
        User? previous = _storage.GetValueOrDefault(key);
        User target = previous ?? value;

        if (previous != null && !ReferenceEquals(previous, value))
        {
            MergeInto(previous, value);
        }

        User? byId = RemovePendingById(key);
        if (byId != null)
        {
            MergeInto(target, byId);
        }

        User? byName = RemovePendingByName(target.Nickname, target.Server);
        if (byName != null)
        {
            MergeInto(target, byName);
        }

        _storage[key] = target;
        IndexIdentity(key, target);
        return previous;
    }

    public void SavePending(User user)
    {
        if (user.Id > 0)
        {
            if (_storage.TryGetValue(user.Id, out User? existing))
            {
                MergeInto(existing, user);
                return;
            }

            _pendingById[user.Id] = user;
        }

        string? nickname = NormalizedNickname(user.Nickname);
        if (nickname == null)
        {
            return;
        }

        if (user.Server > 0)
        {
            _pendingByNameServer[NameServerKey(nickname, user.Server)] = user;
        }

        _pendingByNickname[nickname] = user;
    }

    public void RemovePending(User user)
    {
        if (user.Id > 0)
        {
            _pendingById.Remove(user.Id);
        }

        string? nickname = NormalizedNickname(user.Nickname);
        if (nickname == null)
        {
            return;
        }

        if (user.Server > 0)
        {
            _pendingByNameServer.Remove(NameServerKey(nickname, user.Server));
        }

        _pendingByNickname.Remove(nickname);
    }

    public User? Get(int id) => _storage.GetValueOrDefault(id);
    public bool Exist(int id) => _storage.ContainsKey(id);

    public User? FindByNicknameAndServer(string nickname, int server)
    {
        // A blank name is not a real identity (IndexIdentity never indexes one); reject it up front so the
        // LastOrDefault fallback below can't match a stray empty-named/provisional user.
        if (string.IsNullOrWhiteSpace(nickname))
        {
            return null;
        }

        // Return the NEWEST matching User. Same name+server = the same character; its most recently registered
        // uid is the live instance (the current executor / current sighting), so this never returns a stale
        // duplicate the way the old FirstOrDefault did. The index is ordered oldest-first, so walk it newest-first.
        if (_idIndex.TryGetValue(NameServerKey(nickname, server), out List<int>? uids))
        {
            for (int i = uids.Count - 1; i >= 0; i--)
            {
                if (_storage.TryGetValue(uids[i], out User? indexed)
                    && indexed.Nickname == nickname && indexed.Server == server)
                {
                    return indexed;
                }
            }
        }

        // Fallback for any entry whose identity was set without going through Save (not expected, but safe).
        return _storage.Values.LastOrDefault(u => u.Nickname == nickname && u.Server == server);
    }

    public void RememberPower(int id, string? nickname, int server, JobClass? job, int power)
    {
        if (power <= 0)
        {
            return;
        }

        SavePending(new User(id, nickname, server, job, power: power));
    }

    public void Flush()
    {
        _storage.Clear();
        _pendingByNameServer.Clear();
        _pendingById.Clear();
        _pendingByNickname.Clear();
        _idIndex.Clear();
        _uidIdentity.Clear();
    }

    public int Executor() => _executor;

    public int Executor(int id)
    {
        int past = _executor;
        _executor = id;
        return past;
    }

    private static void MergeInto(User target, User source)
    {
        if (string.IsNullOrWhiteSpace(target.Nickname) && !string.IsNullOrWhiteSpace(source.Nickname))
        {
            target.Nickname = source.Nickname;
        }

        if (target.Server <= 0 && source.Server > 0)
        {
            target.Server = source.Server;
        }

        // Merge the job by confidence: a higher-provenance source (e.g. own-skill over a jobByte) overrides,
        // an equal/lower one doesn't (see User.TrySetJob / JobProvenance).
        target.TrySetJob(source.Job, source.JobSource);

        if (!target.IsExecutor && source.IsExecutor)
        {
            target.IsExecutor = true;
        }

        if (source.Power > 0)
        {
            target.Power = source.Power;
        }
    }

    private User? RemovePendingById(int id)
    {
        if (id <= 0)
        {
            return null;
        }

        if (_pendingById.Remove(id, out User? removed))
        {
            return removed;
        }

        return null;
    }

    private User? RemovePendingByName(string? nickname, int server)
    {
        string? normalized = NormalizedNickname(nickname);
        if (normalized == null)
        {
            return null;
        }

        if (server > 0 && _pendingByNameServer.Remove(NameServerKey(normalized, server), out User? exact))
        {
            RemoveNicknameReference(normalized, exact);
            return exact;
        }

        User? nicknameMatch = _pendingByNickname.GetValueOrDefault(normalized);
        var sameNameEntries = _pendingByNameServer
            .Where(e => NormalizedNickname(e.Value.Nickname) == normalized)
            .ToList();

        var candidates = new List<User>();
        if (nicknameMatch != null)
        {
            candidates.Add(nicknameMatch);
        }

        foreach (KeyValuePair<string, User> e in sameNameEntries)
        {
            candidates.Add(e.Value);
        }

        candidates = candidates
            .GroupBy(u => $"{u.Id}:{u.Server}:{u.Power}")
            .Select(g => g.First())
            .ToList();

        if (candidates.Count != 1)
        {
            return null;
        }

        User selected = candidates[0];
        RemoveIf(_pendingByNickname, normalized, selected);
        foreach (KeyValuePair<string, User> e in sameNameEntries
                     .Where(e => ReferenceEquals(e.Value, selected) || (e.Value.Power == selected.Power && e.Value.Id == selected.Id)))
        {
            RemoveIf(_pendingByNameServer, e.Key, e.Value);
        }

        return selected;
    }

    private void RemoveNicknameReference(string nickname, User user) => RemoveIf(_pendingByNickname, nickname, user);

    private static void RemoveIf(Dictionary<string, User> map, string key, User expected)
    {
        if (map.TryGetValue(key, out User? current) && ReferenceEquals(current, expected))
        {
            map.Remove(key);
        }
    }

    private static string? NormalizedNickname(string? nickname)
    {
        string? trimmed = nickname?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    // Record this uid as the newest under its (name+server) identity, re-grouping it if it changed identity
    // (a reused entity id taken over by a different player), then cap the group. Only indexes a known identity
    // (non-blank nickname + valid server); a bare/provisional uid stays unindexed until it is named.
    private void IndexIdentity(int uid, User target)
    {
        if (string.IsNullOrWhiteSpace(target.Nickname) || target.Server <= 0)
        {
            return;
        }

        string key = NameServerKey(target.Nickname!, target.Server);
        if (_uidIdentity.TryGetValue(uid, out string? oldKey) && oldKey != key
            && _idIndex.TryGetValue(oldKey, out List<int>? oldList))
        {
            oldList.Remove(uid);
            if (oldList.Count == 0)
            {
                _idIndex.Remove(oldKey);
            }
        }

        _uidIdentity[uid] = key;
        if (!_idIndex.TryGetValue(key, out List<int>? uids))
        {
            uids = new List<int>();
            _idIndex[key] = uids;
        }

        uids.Remove(uid); // re-touch: move to the newest end so recency stays accurate on re-registration
        uids.Add(uid);
        EvictOldestBeyondCap(uids);
    }

    // Drop the oldest uids past the cap, but never the current executor (which is the newest for the self anyway).
    // SAFETY INVARIANT: removing a uid from _storage is only safe because a superseded same-name+server uid never
    // emits damage again after the zone/instance load that retires it (DpsCalculator resolves actors purely by the
    // packet uid via _dm.User(actor), so a still-live evicted uid would silently lose its damage). This holds today
    // because a fresh same-character entity-uid is only issued on a zone load that ends the prior battle, and the
    // 3-deep cap keeps the recent ones; if a future packet flow ever lets a retired uid keep dealing, add an
    // is-this-uid-live guard here (e.g. skip eviction for a uid still in the live DPS cache) before removing it.
    private void EvictOldestBeyondCap(List<int> uids)
    {
        int i = 0;
        while (uids.Count > MaxUidsPerIdentity && i < uids.Count)
        {
            if (uids[i] == _executor)
            {
                i++; // keep the executor; evict the next-oldest instead
                continue;
            }

            int victim = uids[i];
            uids.RemoveAt(i);
            RemoveUidCompletely(victim);
        }
    }

    private void RemoveUidCompletely(int uid)
    {
        _storage.Remove(uid);
        _pendingById.Remove(uid);
        _uidIdentity.Remove(uid);
    }

    private static string NameServerKey(string nickname, int server) => $"{nickname}:{server}";
}

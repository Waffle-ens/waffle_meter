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

    public User? FindByNicknameAndServer(string nickname, int server) =>
        _storage.Values.FirstOrDefault(u => u.Nickname == nickname && u.Server == server);

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

    private static string NameServerKey(string nickname, int server) => $"{nickname}:{server}";
}

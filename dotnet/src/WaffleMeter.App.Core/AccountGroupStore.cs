using System.Globalization;

namespace WaffleMeter.App.Core;

/// <summary>How a character came to be in its account group.</summary>
public enum AccountGroupSource
{
    /// <summary>The game's character-select list put it there (<c>Login_RS</c> / <c>Lobby_RS</c>).</summary>
    Lobby,

    /// <summary>The user put it there. Wins over the list until the user hands it back.</summary>
    Manual,
}

/// <summary>A character's account group: a local, opaque id — never the game's account id, which the meter does not
/// read — and how the character got there.</summary>
public readonly record struct AccountMembership(string GroupId, AccountGroupSource Source, long AtMs);

/// <summary>
/// Which characters share an account. The 서버 창고 and the 슈고 열쇠 are per account per server, so two accounts on
/// one server must not be summed as one — and the character-select list is the account's whole roster on that
/// server, so it says exactly which characters go together.
///
/// <para><b>The newest list wins; lists never merge groups.</b> A list re-points every character in it at one group
/// and lets go of the group's characters it no longer names (deleted, renamed, transferred). Accumulating "seen
/// together" edges instead would be transitive and permanent: one name deleted on one account and re-created on the
/// other, or one misread frame, would join two accounts for good. Here it only moves that one character.</para>
///
/// <para><b>Manual wins.</b> A character the user placed stays put whatever a list says, until the user returns it
/// to automatic.</para>
///
/// <para><b>Its OWN settings key</b> (<c>content.accountGroups</c>), for the reason every content store has one: an
/// older build ignores a key it has never heard of, but would drop records it cannot parse from a key it knows.
/// Records: <c>m,identityHash,groupId,source,atMs</c> (membership) and <c>g,groupId,createdAtMs</c> (group, for a
/// stable "계정 N" order). ASCII only.</para>
///
/// Pure and cap-bounded; parsing never throws.
/// </summary>
public sealed class AccountGroupStore
{
    /// <summary>Characters remembered, matching <see cref="AetherPerCharacterStore.MaxCharacters"/> with headroom
    /// for listed characters the meter has not seen played yet.</summary>
    public const int MaxMembers = 96;

    private const string MemberKind = "m";
    private const string GroupKind = "g";
    private const string LobbyTag = "l";
    private const string ManualTag = "u";

    private readonly Dictionary<string, AccountMembership> _members;
    private readonly Dictionary<string, long> _groups;

    private AccountGroupStore(Dictionary<string, AccountMembership> members, Dictionary<string, long> groups)
    {
        _members = members;
        _groups = groups;
    }

    public static AccountGroupStore Empty() => Parse(null);

    /// <summary>Parse the serialized blob. Never throws — malformed records are skipped.</summary>
    public static AccountGroupStore Parse(string? serialized)
    {
        var members = new Dictionary<string, AccountMembership>(StringComparer.Ordinal);
        var groups = new Dictionary<string, long>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(serialized))
        {
            return new AccountGroupStore(members, groups);
        }

        foreach (string record in serialized.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] f = record.Split(',');
            if (f[0] == MemberKind && f.Length == 5
                && IsToken(f[1]) && IsToken(f[2])
                && (f[3] == LobbyTag || f[3] == ManualTag)
                && long.TryParse(f[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out long at))
            {
                members[f[1]] = new AccountMembership(
                    f[2], f[3] == ManualTag ? AccountGroupSource.Manual : AccountGroupSource.Lobby, at);
            }
            else if (f[0] == GroupKind && f.Length == 3 && IsToken(f[1])
                     && long.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out long created))
            {
                groups[f[1]] = created;
            }
        }

        // A membership whose group record went missing still names a real group: give it one, dated by the
        // membership, so ordering and counting see it.
        foreach (AccountMembership m in members.Values)
        {
            if (!groups.ContainsKey(m.GroupId))
            {
                groups[m.GroupId] = m.AtMs;
            }
        }

        return new AccountGroupStore(members, groups);
    }

    /// <summary>The character's account group, or null when nothing has placed it yet.</summary>
    public AccountMembership? GroupOf(string? identityHash) =>
        !string.IsNullOrEmpty(identityHash) && _members.TryGetValue(identityHash!, out AccountMembership m) ? m : null;

    /// <summary>When the group was first made — the order "계정 1, 계정 2 …" follows, so the numbers do not move
    /// as characters are played. 0 for a group this store does not know.</summary>
    public long CreatedAt(string groupId) => _groups.TryGetValue(groupId, out long at) ? at : 0;

    /// <summary>
    /// Apply a character-select list: <paramref name="identityHashes"/> are every character of one account on one
    /// server. They all go to the group already holding most of them (oldest group on a tie), or to a new group
    /// from <paramref name="newGroupId"/> when none holds any. The group's other list-placed characters, which the
    /// list no longer names, are let go. Characters the user placed are never moved. Returns whether anything
    /// changed.
    /// </summary>
    public bool ApplyLobby(IReadOnlyCollection<string> identityHashes, long atMs, Func<string> newGroupId)
    {
        var listed = new HashSet<string>(identityHashes.Where(IsToken), StringComparer.Ordinal);
        if (listed.Count == 0)
        {
            return false;
        }

        string? target = listed
            .Select(h => _members.TryGetValue(h, out AccountMembership m) ? m.GroupId : null)
            .Where(g => g != null)
            .GroupBy(g => g!, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => CreatedAt(g.Key))
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => g.Key)
            .FirstOrDefault();

        bool changed = false;
        if (target is null)
        {
            target = newGroupId();
            if (!IsToken(target) || _groups.ContainsKey(target))
            {
                return false;
            }

            _groups[target] = atMs;
            changed = true;
        }

        foreach (string hash in listed)
        {
            if (_members.TryGetValue(hash, out AccountMembership existing)
                && existing.Source == AccountGroupSource.Manual)
            {
                continue; // the user's placement stands
            }

            // Re-confirmed members are re-stamped too: AtMs is what the cap evicts by, and an account still logging
            // in must never be the one losing characters to it (adversarial review 2026-11-13).
            if (existing.GroupId != target || existing.AtMs < atMs)
            {
                _members[hash] = new AccountMembership(target, AccountGroupSource.Lobby, Math.Max(atMs, existing.AtMs));
                changed = true;
            }
        }

        // The list is the account's WHOLE roster on that server: a list-placed character of this group that it no
        // longer names has left the account (deleted, renamed, transferred).
        foreach (string hash in _members
                     .Where(kv => kv.Value.GroupId == target
                                  && kv.Value.Source == AccountGroupSource.Lobby
                                  && !listed.Contains(kv.Key))
                     .Select(kv => kv.Key)
                     .ToList())
        {
            _members.Remove(hash);
            changed = true;
        }

        if (changed)
        {
            Trim();
        }

        return changed;
    }

    /// <summary>The user puts a character in <paramref name="groupId"/>, or in a group of its own when null (a new id
    /// from <paramref name="newGroupId"/>). Returns whether anything changed.</summary>
    public bool AssignManual(string? identityHash, string? groupId, long atMs, Func<string> newGroupId)
    {
        if (!IsToken(identityHash))
        {
            return false;
        }

        string target;
        if (groupId is null)
        {
            target = newGroupId();
            if (!IsToken(target) || _groups.ContainsKey(target))
            {
                return false;
            }

            _groups[target] = atMs;
        }
        else
        {
            if (!_groups.ContainsKey(groupId))
            {
                return false;
            }

            target = groupId;
        }

        if (_members.TryGetValue(identityHash!, out AccountMembership existing)
            && existing.GroupId == target && existing.Source == AccountGroupSource.Manual)
        {
            return false;
        }

        _members[identityHash!] = new AccountMembership(target, AccountGroupSource.Manual, atMs);
        Trim();
        return true;
    }

    /// <summary>Hand a character the user placed back to the list: it leaves its manual group and waits for the next
    /// character-select list to place it. Returns whether anything changed.</summary>
    public bool ClearManual(string? identityHash)
    {
        if (string.IsNullOrEmpty(identityHash)
            || !_members.TryGetValue(identityHash!, out AccountMembership m)
            || m.Source != AccountGroupSource.Manual)
        {
            return false;
        }

        _members.Remove(identityHash!);
        DropEmptyGroups();
        return true;
    }

    /// <summary>Forget characters — the panel's ✕ and the startup purge, like every per-character store.</summary>
    public bool RemoveAll(IEnumerable<string> identityHashes)
    {
        bool removed = false;
        foreach (string hash in identityHashes)
        {
            removed |= !string.IsNullOrWhiteSpace(hash) && _members.Remove(hash);
        }

        if (removed)
        {
            DropEmptyGroups();
        }

        return removed;
    }

    /// <summary>Serialize for the settings key: groups oldest first, then members by group and hash — stable, so the
    /// blob does not churn.</summary>
    public string Serialize()
    {
        var parts = new List<string>();
        foreach ((string id, long created) in _groups.OrderBy(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal))
        {
            parts.Add(string.Join(',', GroupKind, id, created.ToString(CultureInfo.InvariantCulture)));
        }

        foreach ((string hash, AccountMembership m) in _members
                     .OrderBy(kv => CreatedAt(kv.Value.GroupId))
                     .ThenBy(kv => kv.Value.GroupId, StringComparer.Ordinal)
                     .ThenBy(kv => kv.Key, StringComparer.Ordinal))
        {
            parts.Add(string.Join(',',
                MemberKind,
                hash,
                m.GroupId,
                m.Source == AccountGroupSource.Manual ? ManualTag : LobbyTag,
                m.AtMs.ToString(CultureInfo.InvariantCulture)));
        }

        return string.Join(';', parts);
    }

    private void Trim()
    {
        while (_members.Count > MaxMembers)
        {
            _members.Remove(_members.OrderBy(kv => kv.Value.AtMs).ThenBy(kv => kv.Key, StringComparer.Ordinal).First().Key);
        }

        DropEmptyGroups();
    }

    private void DropEmptyGroups()
    {
        var used = new HashSet<string>(_members.Values.Select(m => m.GroupId), StringComparer.Ordinal);
        foreach (string id in _groups.Keys.Where(id => !used.Contains(id)).ToList())
        {
            _groups.Remove(id);
        }
    }

    private static bool IsToken(string? s) =>
        !string.IsNullOrWhiteSpace(s) && !s.Contains(',') && !s.Contains(';');
}

using WaffleMeter.Stats;

namespace WaffleMeter.App.Core;

/// <summary>What to do with a held character-select list right now.</summary>
public enum LobbyRosterVerdict
{
    /// <summary>The character it was sent for has not entered the world yet — keep holding it.</summary>
    Wait,

    /// <summary>It can never be applied (the character that entered is not in it, or it went stale) — let it go.</summary>
    Drop,

    /// <summary>Apply it: <see cref="LobbyRosterDecision.IdentityHashes"/> are one account's characters.</summary>
    Apply,
}

public readonly record struct LobbyRosterDecision(LobbyRosterVerdict Verdict, IReadOnlyList<string> IdentityHashes);

/// <summary>
/// Decides when a character-select list (<c>Login_RS</c> / <c>Lobby_RS</c>) may be applied to the account groups,
/// and turns its nicknames into identity hashes.
///
/// <para>The list arrives BEFORE a character is picked (~20 s before the world-entry naming packet on the owner's
/// 2026-11-13 login). So it waits for an identity established AFTER it arrived — that is the character picked from
/// it — and is applied only if that character is in it. That second check is what makes a misread frame, or a list
/// of someone else's characters on a reused opcode, harmless: it would have to contain the very character that
/// entered the world next.</para>
///
/// <para>The list carries no server; every character in it is on the server the picked character is on (a
/// character-select list is per server — <c>Login_RS._server_key</c> names it). That server is the picked
/// character's display server (0x3645 <c>_server_id</c>), the same one its identity hash was built from, never the
/// connection's instance process number.</para>
/// </summary>
public static class LobbyRosterGate
{
    /// <summary>A list nobody entered the world from within this long is dropped — the player went back to the
    /// title screen, or the meter never saw the entry. Far longer than any character select.</summary>
    public const long ExpireMs = 15 * 60_000;

    /// <param name="listAtMs">When the list arrived.</param>
    /// <param name="identityAtMs">When the current character's identity was established (0 = never).</param>
    /// <param name="currentHash">The current character's identity hash.</param>
    /// <param name="currentServer">The current character's display server.</param>
    public static LobbyRosterDecision Evaluate(
        IReadOnlyList<string> nicknames,
        long listAtMs,
        long identityAtMs,
        string? currentHash,
        int currentServer,
        long nowMs)
    {
        if (identityAtMs <= 0 || identityAtMs < listAtMs || string.IsNullOrEmpty(currentHash) || currentServer <= 0)
        {
            return nowMs - listAtMs >= ExpireMs
                ? new LobbyRosterDecision(LobbyRosterVerdict.Drop, [])
                : new LobbyRosterDecision(LobbyRosterVerdict.Wait, []);
        }

        var hashes = new List<string>(nicknames.Count);
        foreach (string nickname in nicknames)
        {
            if (StatsIdentity.CharacterIdentityHash(currentServer, nickname) is { } hash && !hashes.Contains(hash))
            {
                hashes.Add(hash);
            }
        }

        return hashes.Contains(currentHash!, StringComparer.Ordinal)
            ? new LobbyRosterDecision(LobbyRosterVerdict.Apply, hashes)
            : new LobbyRosterDecision(LobbyRosterVerdict.Drop, []);
    }
}

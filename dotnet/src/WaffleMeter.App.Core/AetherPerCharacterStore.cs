using System.Globalization;
using System.Text;

namespace WaffleMeter.App.Core;

/// <summary>One character's last-seen aether (오드) balance, tagged with when it was recorded.
/// <paramref name="Base"/> = 자연회복 오드, <paramref name="Bonus"/> = 추가 오드.
/// <para><paramref name="Nickname"/>/<paramref name="Server"/> are stored so the 오드 목록 can name a character
/// the consent list has never heard of — the store's key is a one-way hash, so without them a character that
/// never recorded a consent decision would be an anonymous row. Null/0 on records written before this was
/// added; the next broadcast from that character fills them in.</para></summary>
public readonly record struct AetherSnapshot(
    int Base, int Bonus, long SavedAtMs, string? Nickname = null, int Server = 0)
{
    /// <summary>What the character can spend.</summary>
    public int Total => Base + Bonus;
}

/// <summary>
/// Remembers each character's last-seen aether balance, keyed by its stats identity hash, so the character-
/// management list can show every character's 오드 — not just the one currently logged in. The aether packet
/// only ever carries the ACTIVE character's balance, so this is populated over time as the user plays each
/// character. Serialized to a single settings string (<c>aether.perCharacter</c>); pure and cap-bounded so it
/// is unit-testable and can't grow without limit.
/// </summary>
public sealed class AetherPerCharacterStore
{
    /// <summary>Most-recent characters kept; the oldest is evicted past this (a player has few characters).</summary>
    public const int MaxCharacters = 48;

    private readonly Dictionary<string, AetherSnapshot> _byHash;

    private AetherPerCharacterStore(Dictionary<string, AetherSnapshot> byHash) => _byHash = byHash;

    /// <summary>Parse the serialized blob. Never throws — malformed records are skipped.
    /// <para>Records are <c>hash,base,bonus,savedAtMs</c>, or <c>hash,base,bonus,savedAtMs,server,nicknameB64</c>
    /// once the character's name is known (the nickname is Base64'd because <c>,</c> and <c>;</c> are the
    /// separators). The pre-2026-07-30 format carried a fifth numeric field (a separately-stored total); those
    /// FIVE-field records are deliberately dropped rather than migrated, because they were written while the
    /// parser mis-read the single-pool packet and their 자연회복/추가 split is wrong. Each character's chip
    /// refills from the next live broadcast. Field count alone tells the three apart: 4 = current-without-name,
    /// 5 = the bad legacy format, 6 = current-with-name.</para></summary>
    public static AetherPerCharacterStore Parse(string? serialized)
    {
        var map = new Dictionary<string, AetherSnapshot>(StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(serialized))
        {
            foreach (string record in serialized.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] f = record.Split(',');
                if ((f.Length != 4 && f.Length != 6) || string.IsNullOrWhiteSpace(f[0])
                    || !int.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int b)
                    || !int.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int bonus)
                    || !long.TryParse(f[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out long ms))
                {
                    continue;
                }

                string? nickname = null;
                int server = 0;
                if (f.Length == 6)
                {
                    int.TryParse(f[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out server);
                    nickname = DecodeName(f[5]);
                }

                map[f[0]] = new AetherSnapshot(b, bonus, ms, nickname, server);
            }
        }

        return new AetherPerCharacterStore(map);
    }

    /// <summary>The remembered balance for a character, or null if none has been seen.</summary>
    public AetherSnapshot? Get(string? identityHash) =>
        !string.IsNullOrEmpty(identityHash) && _byHash.TryGetValue(identityHash!, out AetherSnapshot s) ? s : null;

    /// <summary>Record (or replace) a character's balance. Returns false when the arguments are unusable (so the
    /// caller can skip re-serializing). Evicts the oldest entry once the cap is exceeded.</summary>
    public bool Upsert(string? identityHash, AetherSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(identityHash))
        {
            return false;
        }

        _byHash[identityHash!] = snapshot;
        while (_byHash.Count > MaxCharacters)
        {
            string oldest = _byHash.OrderBy(kv => kv.Value.SavedAtMs).First().Key;
            _byHash.Remove(oldest);
        }

        return true;
    }

    /// <summary>Drop the given characters' remembered balances. Returns true when anything was removed (so the
    /// caller can skip re-serializing). Used by the startup hygiene pass that deletes records written under an
    /// impossible identity — a mis-parsed own-load packet once made the meter believe it was a character with
    /// server 47200, and its 오드 got recorded under that hash (2026-07-30).</summary>
    public bool RemoveAll(IEnumerable<string> identityHashes)
    {
        bool removed = false;
        foreach (string hash in identityHashes)
        {
            removed |= !string.IsNullOrWhiteSpace(hash) && _byHash.Remove(hash);
        }

        return removed;
    }

    /// <summary>Every remembered character, newest-recorded first.</summary>
    public IReadOnlyList<KeyValuePair<string, AetherSnapshot>> All() => _byHash
        .OrderByDescending(kv => kv.Value.SavedAtMs)
        .ToList();

    /// <summary>Serialize back to the settings blob (records ordered newest-first for stability). A record only
    /// grows the name fields once a nickname is known, so an install that has never seen one stays byte-identical
    /// to what earlier versions wrote.</summary>
    public string Serialize() => string.Join(';', _byHash
        .OrderByDescending(kv => kv.Value.SavedAtMs)
        .Select(kv =>
        {
            string head = string.Join(',',
                kv.Key,
                kv.Value.Base.ToString(CultureInfo.InvariantCulture),
                kv.Value.Bonus.ToString(CultureInfo.InvariantCulture),
                kv.Value.SavedAtMs.ToString(CultureInfo.InvariantCulture));

            return string.IsNullOrWhiteSpace(kv.Value.Nickname)
                ? head
                : string.Join(',',
                    head,
                    kv.Value.Server.ToString(CultureInfo.InvariantCulture),
                    EncodeName(kv.Value.Nickname!));
        }));

    private static string EncodeName(string nickname) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(nickname));

    private static string? DecodeName(string encoded)
    {
        try
        {
            string name = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch (FormatException)
        {
            return null; // a hand-edited settings file shouldn't cost the whole record
        }
    }
}

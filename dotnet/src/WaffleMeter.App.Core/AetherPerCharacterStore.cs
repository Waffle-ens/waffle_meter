using System.Globalization;

namespace WaffleMeter.App.Core;

/// <summary>One character's last-seen aether (오드) balance, tagged with when it was recorded.</summary>
public readonly record struct AetherSnapshot(int Base, int Bonus, int Total, long SavedAtMs);

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

    /// <summary>Parse the serialized blob. Never throws — malformed records are skipped.</summary>
    public static AetherPerCharacterStore Parse(string? serialized)
    {
        var map = new Dictionary<string, AetherSnapshot>(StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(serialized))
        {
            foreach (string record in serialized.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] f = record.Split(',');
                if (f.Length != 5 || string.IsNullOrWhiteSpace(f[0])
                    || !int.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int b)
                    || !int.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int bonus)
                    || !int.TryParse(f[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int total)
                    || !long.TryParse(f[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out long ms))
                {
                    continue;
                }

                map[f[0]] = new AetherSnapshot(b, bonus, total, ms);
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

    /// <summary>Serialize back to the settings blob (records ordered newest-first for stability).</summary>
    public string Serialize() => string.Join(';', _byHash
        .OrderByDescending(kv => kv.Value.SavedAtMs)
        .Select(kv => string.Join(',',
            kv.Key,
            kv.Value.Base.ToString(CultureInfo.InvariantCulture),
            kv.Value.Bonus.ToString(CultureInfo.InvariantCulture),
            kv.Value.Total.ToString(CultureInfo.InvariantCulture),
            kv.Value.SavedAtMs.ToString(CultureInfo.InvariantCulture))));
}

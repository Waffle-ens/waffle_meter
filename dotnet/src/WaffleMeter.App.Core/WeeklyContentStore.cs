using System.Globalization;

namespace WaffleMeter.App.Core;

/// <summary>One character's last-known remaining clears for one raid, and when it was recorded.</summary>
public readonly record struct WeeklyContentRecord(int Remaining, long SavedAtMs);

/// <summary>
/// Remembers each character's weekly 성역 clear counters, keyed by stats identity hash + dungeon slug, so the
/// 컨텐츠 관리 panel can show every character — not just the one logged in.
///
/// <para><b>The value is the server's, not a guess.</b> The game broadcasts each counter on the 0x610B/0x610C
/// resource family (see <c>WeeklyContentParser</c>): a full snapshot on login/zone-in and a delta the moment a
/// final boss dies. This store is only the memory of what the ACTIVE character last broadcast — the packet
/// never carries another character's counters, exactly as with 오드.</para>
///
/// <para><b>Staleness is the whole reason a timestamp is stored.</b> A remembered "0 left" is only true until
/// the next Wednesday 05:00 KST, after which the server has recharged it; the reader compares against
/// <see cref="WeeklyContentReset"/> instead of rewriting the blob on a timer, so the answer stays right even if
/// the app was closed across the reset.</para>
///
/// <para>Serialized to its OWN settings string (<c>content.weeklyClears</c>) rather than as extra fields on the
/// 오드 blob. That is not tidiness: an older build ignores a settings key it has never heard of, but a record it
/// cannot parse it DROPS — and since every aether broadcast rewrites that whole blob, one launch of an older
/// meter would have made the loss permanent (<see cref="AetherPerCharacterStore"/> documents the same trap, and
/// its five-field legacy records are the scar).</para>
///
/// Pure and cap-bounded so the ordering and the staleness rule are unit-testable.
/// </summary>
public sealed class WeeklyContentStore
{
    /// <summary>Most-recent characters kept, matching <see cref="AetherPerCharacterStore.MaxCharacters"/>.</summary>
    public const int MaxCharacters = 48;

    private readonly Dictionary<string, Dictionary<string, WeeklyContentRecord>> _byHash;

    private WeeklyContentStore(Dictionary<string, Dictionary<string, WeeklyContentRecord>> byHash) =>
        _byHash = byHash;

    /// <summary>Parse the serialized blob. Never throws — malformed records are skipped.
    /// <para>Records are <c>hash,slug,remaining,savedAtMs</c>. A slug this build does not know is kept and
    /// re-serialized untouched, so shipping a new dungeon later can't be undone by a user who rolls back once.
    /// The FIELD COUNT is the format discriminator (there is no schema version, same as the 오드 blob), so a
    /// future field must go in a new key rather than extend this record.</para></summary>
    public static WeeklyContentStore Parse(string? serialized)
    {
        var map = new Dictionary<string, Dictionary<string, WeeklyContentRecord>>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(serialized))
        {
            return new WeeklyContentStore(map);
        }

        foreach (string record in serialized.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] f = record.Split(',');
            if (f.Length != 4
                || string.IsNullOrWhiteSpace(f[0])
                || string.IsNullOrWhiteSpace(f[1])
                || !int.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int remaining)
                || !long.TryParse(f[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out long savedAtMs))
            {
                continue;
            }

            if (!map.TryGetValue(f[0], out Dictionary<string, WeeklyContentRecord>? forCharacter))
            {
                forCharacter = new Dictionary<string, WeeklyContentRecord>(StringComparer.Ordinal);
                map[f[0]] = forCharacter;
            }

            forCharacter[f[1]] = new WeeklyContentRecord(Math.Max(0, remaining), savedAtMs);
        }

        return new WeeklyContentStore(map);
    }

    /// <summary>What this character has left for this dungeon, or null when unknown — never seen, or last seen
    /// before the most recent weekly reset, in which case the server has recharged it and the remembered value
    /// is a lie. Callers show <see cref="WeeklyContentCatalog.WeeklyGrant"/> for null.</summary>
    public int? Remaining(string? identityHash, string slug, long nowMs)
    {
        if (string.IsNullOrEmpty(identityHash)
            || !_byHash.TryGetValue(identityHash!, out Dictionary<string, WeeklyContentRecord>? forCharacter)
            || !forCharacter.TryGetValue(slug, out WeeklyContentRecord record))
        {
            return null;
        }

        return WeeklyContentReset.IsCurrentWeek(record.SavedAtMs, nowMs) ? record.Remaining : null;
    }

    /// <summary>Record a counter for a character. Returns false when the arguments are unusable or the value is
    /// already stored for this week (so the caller can skip re-serializing — these broadcasts repeat).</summary>
    public bool Upsert(string? identityHash, string slug, int remaining, long atMs)
    {
        if (string.IsNullOrWhiteSpace(identityHash) || string.IsNullOrWhiteSpace(slug) || atMs <= 0)
        {
            return false;
        }

        if (!_byHash.TryGetValue(identityHash!, out Dictionary<string, WeeklyContentRecord>? forCharacter))
        {
            forCharacter = new Dictionary<string, WeeklyContentRecord>(StringComparer.Ordinal);
            _byHash[identityHash!] = forCharacter;
        }
        else if (forCharacter.TryGetValue(slug, out WeeklyContentRecord existing)
                 && existing.Remaining == Math.Max(0, remaining)
                 && WeeklyContentReset.IsCurrentWeek(existing.SavedAtMs, atMs))
        {
            return false; // same value, same week — nothing to write
        }

        forCharacter[slug] = new WeeklyContentRecord(Math.Max(0, remaining), atMs);
        Evict();
        return true;
    }

    /// <summary>Drop every record for the given characters. Returns true when anything was removed. Wired to the
    /// panel's per-row ✕ and to the startup purge of impossible identities, so a forgotten character leaves
    /// nothing behind in either store.</summary>
    public bool RemoveAll(IEnumerable<string> identityHashes)
    {
        bool removed = false;
        foreach (string hash in identityHashes)
        {
            removed |= !string.IsNullOrWhiteSpace(hash) && _byHash.Remove(hash);
        }

        return removed;
    }

    /// <summary>Serialize for the settings key. Newest-recorded character first, and each character's dungeons in
    /// catalog order followed by any slug this build doesn't know, so the blob is stable across launches.</summary>
    public string Serialize() => string.Join(';', _byHash
        .OrderByDescending(kv => Newest(kv.Value))
        .SelectMany(kv => Ordered(kv.Value).Select(r => string.Join(',',
            kv.Key,
            r.Key,
            r.Value.Remaining.ToString(CultureInfo.InvariantCulture),
            r.Value.SavedAtMs.ToString(CultureInfo.InvariantCulture)))));

    private static long Newest(Dictionary<string, WeeklyContentRecord> forCharacter) =>
        forCharacter.Count == 0 ? 0 : forCharacter.Max(r => r.Value.SavedAtMs);

    private static IEnumerable<KeyValuePair<string, WeeklyContentRecord>> Ordered(
        Dictionary<string, WeeklyContentRecord> forCharacter)
    {
        int Rank(string slug)
        {
            for (int i = 0; i < WeeklyContentCatalog.All.Count; i++)
            {
                if (string.Equals(WeeklyContentCatalog.All[i].Slug, slug, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return int.MaxValue; // unknown slug from a newer build — kept, sorted last
        }

        return forCharacter.OrderBy(r => Rank(r.Key)).ThenBy(r => r.Key, StringComparer.Ordinal);
    }

    private void Evict()
    {
        while (_byHash.Count > MaxCharacters)
        {
            string oldest = _byHash.OrderBy(kv => Newest(kv.Value)).First().Key;
            _byHash.Remove(oldest);
        }
    }
}

using System.Globalization;

namespace WaffleMeter.App.Core;

/// <summary>One remembered balance and when the server last stated it.</summary>
public readonly record struct CurrencyRecord(long Count, long ObservedAtMs);

/// <summary>
/// Remembers each character's currency balances, and each server's 서버 창고 kinah, so the 컨텐츠 관리 panel can
/// show every character — not only the one logged in. The item packets only ever describe the ACTIVE character
/// (exactly like 오드), so this fills in over time as each character is played.
///
/// <para><b>Two kinds of row.</b> <c>c</c> rows key on the stats identity hash, the same key every other
/// per-character store uses — 컨텐츠 관리 rows are built from those, so a different key (the character's dbid, say)
/// would never meet a row to show on. <c>s</c> rows key on the server id and hold the 서버 창고: one stack per
/// account per server, which every character of that server sees. Filing it per character would count it once per
/// character the moment a server's 총 키나 is summed.</para>
///
/// <para><b>Its OWN settings key</b> (<c>content.currencies</c>), for the reason the corridor and artifact stores
/// each have one: a blob's field count is its format discriminator, an older build DROPS a record it cannot parse,
/// and these blobs are rewritten on every broadcast — so widening an existing blob would make one rollback permanent
/// data loss. A key an old build has never heard of is simply ignored.</para>
///
/// <para><b>Never older over newer.</b> Every row carries the time the server stated it, and a reading older than
/// the one on file is refused. A snapshot is filed seconds after it arrived (it waits for its character's naming
/// packet) and a change that landed meanwhile must not be rewound by it.</para>
///
/// Pure and cap-bounded; parsing never throws.
/// </summary>
public sealed class CurrencyStore
{
    /// <summary>Most-recent characters kept, matching <see cref="AetherPerCharacterStore.MaxCharacters"/>.</summary>
    public const int MaxCharacters = 48;

    /// <summary>Servers whose 서버 창고 is kept, matching <see cref="AbyssArtifactStore.MaxServers"/>.</summary>
    public const int MaxServers = 16;

    private const string CharacterKind = "c";

    private const string ServerKind = "s";

    private readonly Dictionary<string, Dictionary<string, CurrencyRecord>> _characters;

    private readonly Dictionary<int, Dictionary<string, CurrencyRecord>> _servers;

    private CurrencyStore(
        Dictionary<string, Dictionary<string, CurrencyRecord>> characters,
        Dictionary<int, Dictionary<string, CurrencyRecord>> servers)
    {
        _characters = characters;
        _servers = servers;
    }

    /// <summary>Parse the serialized blob. Never throws — malformed records are skipped.
    /// <para>Every record is <c>kind,key,slug,count,observedAtMs</c>. A slug this build does not know is kept and
    /// written back untouched, so a currency added by a newer meter survives one launch of an older one.</para></summary>
    public static CurrencyStore Parse(string? serialized)
    {
        var characters = new Dictionary<string, Dictionary<string, CurrencyRecord>>(StringComparer.Ordinal);
        var servers = new Dictionary<int, Dictionary<string, CurrencyRecord>>();
        if (string.IsNullOrEmpty(serialized))
        {
            return new CurrencyStore(characters, servers);
        }

        foreach (string record in serialized.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] f = record.Split(',');
            if (f.Length != 5
                || string.IsNullOrWhiteSpace(f[1])
                || string.IsNullOrWhiteSpace(f[2])
                || !long.TryParse(f[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out long count)
                || count < 0
                || !long.TryParse(f[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out long observedAt))
            {
                continue;
            }

            if (f[0] == CharacterKind)
            {
                if (!characters.TryGetValue(f[1], out Dictionary<string, CurrencyRecord>? forCharacter))
                {
                    characters[f[1]] = forCharacter = new Dictionary<string, CurrencyRecord>(StringComparer.Ordinal);
                }

                forCharacter[f[2]] = new CurrencyRecord(count, observedAt);
            }
            else if (f[0] == ServerKind
                     && int.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int serverId)
                     && serverId > 0)
            {
                if (!servers.TryGetValue(serverId, out Dictionary<string, CurrencyRecord>? forServer))
                {
                    servers[serverId] = forServer = new Dictionary<string, CurrencyRecord>(StringComparer.Ordinal);
                }

                forServer[f[2]] = new CurrencyRecord(count, observedAt);
            }
        }

        return new CurrencyStore(characters, servers);
    }

    /// <summary>A character's remembered balance, or null when none has been stated.</summary>
    public CurrencyRecord? Character(string? identityHash, string slug) =>
        !string.IsNullOrEmpty(identityHash)
        && _characters.TryGetValue(identityHash!, out Dictionary<string, CurrencyRecord>? forCharacter)
        && forCharacter.TryGetValue(slug, out CurrencyRecord record)
            ? record
            : null;

    /// <summary>A server's remembered balance (the 서버 창고), or null when none has been stated.</summary>
    public CurrencyRecord? Server(int serverId, string slug) =>
        _servers.TryGetValue(serverId, out Dictionary<string, CurrencyRecord>? forServer)
        && forServer.TryGetValue(slug, out CurrencyRecord record)
            ? record
            : null;

    /// <summary>Record a character's balance. Returns false when the arguments are unusable, the reading is older
    /// than the one on file, or nothing would change — so the caller can skip re-serializing.</summary>
    public bool UpsertCharacter(string? identityHash, string slug, long count, long observedAtMs)
    {
        if (string.IsNullOrWhiteSpace(identityHash) || identityHash!.Contains(',') || identityHash.Contains(';'))
        {
            return false;
        }

        if (!_characters.TryGetValue(identityHash, out Dictionary<string, CurrencyRecord>? forCharacter))
        {
            forCharacter = new Dictionary<string, CurrencyRecord>(StringComparer.Ordinal);
        }

        if (!Upsert(forCharacter, slug, count, observedAtMs))
        {
            return false;
        }

        _characters[identityHash] = forCharacter;
        while (_characters.Count > MaxCharacters)
        {
            _characters.Remove(_characters.OrderBy(kv => Newest(kv.Value)).First().Key);
        }

        return true;
    }

    /// <summary>Record a server's balance — the 서버 창고. Same rules as <see cref="UpsertCharacter"/>.</summary>
    public bool UpsertServer(int serverId, string slug, long count, long observedAtMs)
    {
        if (serverId <= 0)
        {
            return false;
        }

        if (!_servers.TryGetValue(serverId, out Dictionary<string, CurrencyRecord>? forServer))
        {
            forServer = new Dictionary<string, CurrencyRecord>(StringComparer.Ordinal);
        }

        if (!Upsert(forServer, slug, count, observedAtMs))
        {
            return false;
        }

        _servers[serverId] = forServer;
        while (_servers.Count > MaxServers)
        {
            _servers.Remove(_servers.OrderBy(kv => Newest(kv.Value)).First().Key);
        }

        return true;
    }

    /// <summary>File the ledger's balances for the character <paramref name="identityHash"/> on
    /// <paramref name="serverId"/>: character-scope ones under the character, the 서버 창고 under the server. A
    /// balance the catalog does not map is ignored; a missing hash or server skips its half. Returns whether
    /// anything changed.</summary>
    public bool File(string? identityHash, int serverId, IEnumerable<CurrencyBalance> balances)
    {
        bool changed = false;
        foreach (CurrencyBalance balance in balances)
        {
            if (CurrencyCatalog.For(balance.ItemId, balance.Container) is not { } info)
            {
                continue;
            }

            changed |= info.Scope == CurrencyScope.Server
                ? UpsertServer(serverId, info.Slug, balance.Count, balance.ObservedAtMs)
                : UpsertCharacter(identityHash, info.Slug, balance.Count, balance.ObservedAtMs);
        }

        return changed;
    }

    /// <summary>Drop every character row for the given characters. Wired to the panel's per-row ✕ and the startup
    /// purge of impossible identities, the same as the other per-character stores. Server rows stay: the 서버 창고
    /// belongs to the account, not to the character being forgotten.</summary>
    public bool RemoveAll(IEnumerable<string> identityHashes)
    {
        bool removed = false;
        foreach (string hash in identityHashes)
        {
            removed |= !string.IsNullOrWhiteSpace(hash) && _characters.Remove(hash);
        }

        return removed;
    }

    /// <summary>Serialize for the settings key. Characters newest first, then servers by id; each one's slugs in
    /// catalog order followed by any this build does not know — stable, so the blob does not churn.</summary>
    public string Serialize()
    {
        var parts = new List<string>();
        foreach ((string hash, Dictionary<string, CurrencyRecord> forCharacter) in
                 _characters.OrderByDescending(kv => Newest(kv.Value)).ThenBy(kv => kv.Key, StringComparer.Ordinal))
        {
            parts.AddRange(Ordered(forCharacter).Select(r => Record(CharacterKind, hash, r.Key, r.Value)));
        }

        foreach ((int serverId, Dictionary<string, CurrencyRecord> forServer) in _servers.OrderBy(kv => kv.Key))
        {
            string key = serverId.ToString(CultureInfo.InvariantCulture);
            parts.AddRange(Ordered(forServer).Select(r => Record(ServerKind, key, r.Key, r.Value)));
        }

        return string.Join(';', parts);
    }

    private static bool Upsert(Dictionary<string, CurrencyRecord> rows, string slug, long count, long observedAtMs)
    {
        if (string.IsNullOrWhiteSpace(slug) || slug.Contains(',') || slug.Contains(';') || count < 0 || observedAtMs <= 0)
        {
            return false;
        }

        if (rows.TryGetValue(slug, out CurrencyRecord existing)
            && (existing.ObservedAtMs > observedAtMs
                || (existing.ObservedAtMs == observedAtMs && existing.Count == count)))
        {
            return false; // older than what is on file, or exactly what is on file
        }

        rows[slug] = new CurrencyRecord(count, observedAtMs);
        return true;
    }

    private static string Record(string kind, string key, string slug, CurrencyRecord record) =>
        string.Join(',',
            kind,
            key,
            slug,
            record.Count.ToString(CultureInfo.InvariantCulture),
            record.ObservedAtMs.ToString(CultureInfo.InvariantCulture));

    private static long Newest(Dictionary<string, CurrencyRecord> rows) =>
        rows.Count == 0 ? 0 : rows.Max(r => r.Value.ObservedAtMs);

    private static IEnumerable<KeyValuePair<string, CurrencyRecord>> Ordered(Dictionary<string, CurrencyRecord> rows)
    {
        int Rank(string slug)
        {
            for (int i = 0; i < CurrencyCatalog.All.Count; i++)
            {
                if (string.Equals(CurrencyCatalog.All[i].Slug, slug, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return int.MaxValue; // unknown slug from a newer build — kept, sorted last
        }

        return rows.OrderBy(r => Rank(r.Key)).ThenBy(r => r.Key, StringComparer.Ordinal);
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;

namespace WaffleMeter.App.Core;

/// <summary>One character's nickname-effect grant. Wire shape — keep the short names.</summary>
public sealed class NameFxEntry
{
    /// <summary>Character identity hash (<c>StatsIdentity.CharacterIdentityHash</c>). The ONLY identifier here —
    /// no nickname, no server. Reversing a hash is not possible; publishing the names would be.</summary>
    [JsonPropertyName("h")]
    public string Hash { get; set; } = string.Empty;

    /// <summary>Effect id from the shipped catalogue. Unknown ids are dropped, not rendered.</summary>
    [JsonPropertyName("e")]
    public string EffectId { get; set; } = string.Empty;

    /// <summary>supporter | ranker. Kept so a grant can be audited; the effect id already implies the family.</summary>
    [JsonPropertyName("k")]
    public string Kind { get; set; } = string.Empty;

    /// <summary>Expiry, epoch ms. 0 = no expiry.</summary>
    [JsonPropertyName("x")]
    public long ExpiresAtMs { get; set; }
}

/// <summary>
/// Who currently has a nickname effect. Read-only on the meter side — the meter never decides a grant, it only
/// renders one.
/// <para><b>Where it comes from.</b> A JSON document in the user data folder
/// (<c>%APPDATA%\waffle_meter.v1.4\namefx\supporters.json</c>). Absent file = nobody has an effect, which is the
/// normal state until the server channel lands. It is deliberately NOT an embedded asset: this repository is
/// public, and a grant list committed here would put supporter hashes in git history permanently, where
/// revoking a grant cannot reach them.</para>
/// <para><b>Additive only.</b> Unknown effect ids, unknown kinds and unknown fields are ignored rather than
/// rejected, so a newer document never breaks an older meter. A <c>schemaVersion</c> above what this build
/// knows is refused outright — that is the one change that must be published to clients first.</para>
/// </summary>
public sealed class NameFxRoster
{
    /// <summary>The highest document version this build understands.</summary>
    public const int MaxSchemaVersion = 1;

    public static readonly NameFxRoster Empty = new(new Dictionary<string, NameFxEntry>(StringComparer.OrdinalIgnoreCase));

    private readonly Dictionary<string, NameFxEntry> _byHash;

    private NameFxRoster(Dictionary<string, NameFxEntry> byHash) => _byHash = byHash;

    public int Count => _byHash.Count;

    /// <summary>The grant for a character hash, or null. Expired entries were dropped at load; callers that
    /// keep a roster for a long session should pass <paramref name="nowMs"/> so expiry still bites.</summary>
    public NameFxEntry? Find(string? identityHash, long nowMs = 0)
    {
        if (string.IsNullOrEmpty(identityHash) || !_byHash.TryGetValue(identityHash, out NameFxEntry? e))
        {
            return null;
        }

        return e.ExpiresAtMs > 0 && nowMs > 0 && e.ExpiresAtMs <= nowMs ? null : e;
    }

    /// <summary>Path of the roster document. Public so the settings window can open the folder.</summary>
    public static string FilePath(string appDirectory) =>
        Path.Combine(appDirectory, "namefx", "supporters.json");

    /// <summary>Load the roster, or <see cref="Empty"/> for any reason at all — missing file, bad JSON, a future
    /// schema. A decoration must never be able to stop the meter from starting.</summary>
    public static NameFxRoster Load(string appDirectory, long nowMs, Func<string, bool> isKnownEffect)
    {
        try
        {
            string path = FilePath(appDirectory);
            return File.Exists(path) ? Parse(File.ReadAllText(path), nowMs, isKnownEffect) : Empty;
        }
        catch
        {
            return Empty;
        }
    }

    public static NameFxRoster Parse(string json, long nowMs, Func<string, bool> isKnownEffect)
    {
        try
        {
            Document? doc = JsonSerializer.Deserialize<Document>(json);
            if (doc is null || doc.SchemaVersion > MaxSchemaVersion || doc.Entries is null)
            {
                return Empty;
            }

            var map = new Dictionary<string, NameFxEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (NameFxEntry e in doc.Entries)
            {
                if (string.IsNullOrWhiteSpace(e.Hash) || !isKnownEffect(e.EffectId))
                {
                    continue; // an id this build cannot draw is the same as no grant
                }

                if (e.ExpiresAtMs > 0 && nowMs > 0 && e.ExpiresAtMs <= nowMs)
                {
                    continue;
                }

                map[e.Hash] = e; // last one wins; the publisher guarantees uniqueness
            }

            return map.Count == 0 ? Empty : new NameFxRoster(map);
        }
        catch
        {
            return Empty;
        }
    }

    private sealed class Document
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; } = 1;

        [JsonPropertyName("entries")]
        public List<NameFxEntry>? Entries { get; set; }
    }
}

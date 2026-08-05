using System.Text.Json;

namespace WaffleMeter.Data;

/// <summary>Where a boss mobCode sits in the supported-dungeon catalog.
/// <para><see cref="BossIndex"/> is 1-based, matching the stats web's boss ordering.</para></summary>
public readonly record struct EncounterInfo(
    string DungeonKey,
    string DungeonName,
    string Category,
    int CategoryOrd,
    string VariantLabel,
    int DungeonId,
    int BossIndex,
    string BossName,
    bool HasVariants,
    // The web models a variant as EITHER a difficulty (원정: 탐험/보통/어려움/시련, 성역 무스펠: 보통/어려움)
    // OR a numbered stage (초월: "1".."4"); exactly one is set, both are null for a single-variant dungeon.
    // Kept apart from VariantLabel so the upload payload can state them the way the server models them.
    string? Difficulty = null,
    string? Stage = null)
{
    /// <summary>The boss name carrying its difficulty/stage — <c>"바크론 (시련)"</c>. The same boss NAME recurs
    /// across every difficulty of a dungeon (only the mobCode differs), so without this the meter shows the same
    /// title for a 탐험 clear and a 시련 clear.
    /// <para>A dungeon with a single variant (성역 루드라·침식의 정화소, label "전체") gets no suffix — there is
    /// nothing to disambiguate and the label would be noise.</para></summary>
    public string DisplayBossName =>
        HasVariants && VariantLabel.Length > 0 ? $"{BossName} ({VariantLabel})" : BossName;
}

/// <summary>
/// The encounters the stats web publishes statistics for, as a <c>mobCode → (dungeon, variant, boss)</c> map.
/// <para>This is a shipped mirror of the web's encounter seed (regenerate with
/// <c>dotnet/tools/export-encounters.ts</c>). It earns its place twice:</para>
/// <list type="number">
/// <item>The upload gate. <c>normalizeEncounter</c> only verifies a mobCode that appears in that seed and the
/// server answers <c>400 unsupported_encounter</c> otherwise — and the meter has no retry path, so every such
/// battle is lost outright. Gating locally means we never spend the request.</item>
/// <item>Difficulty labelling. A boss mobCode is unique per (dungeon, difficulty/stage), so this table is the
/// only thing the meter needs to tell 시련 바크론 from 어려움 바크론 — no map-id tracking required.</item>
/// </list>
/// <para>Loading never throws: a missing or malformed asset yields <see cref="Empty"/>, which reports every code
/// as supported. That is deliberate — an unreadable catalog must not silently stop all uploads.</para>
/// </summary>
public sealed class EncounterCatalog
{
    /// <summary>The no-catalog fallback: nothing is known, so nothing is filtered out.</summary>
    public static readonly EncounterCatalog Empty = new(new Dictionary<int, EncounterInfo>());

    private readonly Dictionary<int, EncounterInfo> _byMobCode;

    private EncounterCatalog(Dictionary<int, EncounterInfo> byMobCode) => _byMobCode = byMobCode;

    /// <summary>How many mobCodes the catalog maps (0 = the fallback that filters nothing).</summary>
    public int Count => _byMobCode.Count;

    /// <summary>True when the catalog was actually loaded and may be used as a gate.</summary>
    public bool IsLoaded => _byMobCode.Count > 0;

    /// <summary>encounters.json: <c>{ "dungeons": [ { key, category, categoryOrd, name, variantType,
    /// bosses: [{index, name}], variants: [{ label, dungeonId, mobs: [[mobCode, bossIndex], ...] }] } ] }</c>.
    /// Returns <see cref="Empty"/> rather than throwing when the file is absent or unreadable.</summary>
    public static EncounterCatalog Load(string path)
    {
        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (Exception)
        {
            return Empty; // fail-open: a broken catalog must not stop every upload
        }
    }

    /// <summary>Parse from JSON text. Malformed dungeons/variants are skipped individually so one bad entry
    /// cannot cost the whole catalog.</summary>
    public static EncounterCatalog Parse(string json)
    {
        var map = new Dictionary<int, EncounterInfo>();
        using JsonDocument doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("dungeons", out JsonElement dungeons)
            || dungeons.ValueKind != JsonValueKind.Array)
        {
            return Empty;
        }

        foreach (JsonElement dungeon in dungeons.EnumerateArray())
        {
            string key = Str(dungeon, "key");
            string name = Str(dungeon, "name");
            string category = Str(dungeon, "category");
            int categoryOrd = dungeon.TryGetProperty("categoryOrd", out JsonElement co)
                && co.ValueKind == JsonValueKind.Number ? co.GetInt32() : 0;

            // variantType "all" = the dungeon has exactly one (unnamed) variant, so its label carries no
            // information and must not be appended to a boss name.
            bool hasVariants = !string.Equals(Str(dungeon, "variantType"), "all", StringComparison.Ordinal);

            var bossNames = new Dictionary<int, string>();
            if (dungeon.TryGetProperty("bosses", out JsonElement bosses) && bosses.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement boss in bosses.EnumerateArray())
                {
                    if (boss.TryGetProperty("index", out JsonElement bi) && bi.ValueKind == JsonValueKind.Number)
                    {
                        bossNames[bi.GetInt32()] = Str(boss, "name");
                    }
                }
            }

            if (!dungeon.TryGetProperty("variants", out JsonElement variants)
                || variants.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (JsonElement variant in variants.EnumerateArray())
            {
                string label = Str(variant, "label");
                int dungeonId = variant.TryGetProperty("dungeonId", out JsonElement di)
                    && di.ValueKind == JsonValueKind.Number ? di.GetInt32() : 0;
                string? difficulty = NullableStr(variant, "difficulty");
                string? stage = NullableStr(variant, "stage");

                if (!variant.TryGetProperty("mobs", out JsonElement mobs) || mobs.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (JsonElement pair in mobs.EnumerateArray())
                {
                    if (pair.ValueKind != JsonValueKind.Array || pair.GetArrayLength() < 2)
                    {
                        continue;
                    }

                    JsonElement codeEl = pair[0];
                    JsonElement indexEl = pair[1];
                    if (codeEl.ValueKind != JsonValueKind.Number || indexEl.ValueKind != JsonValueKind.Number)
                    {
                        continue;
                    }

                    int mobCode = codeEl.GetInt32();
                    int bossIndex = indexEl.GetInt32();
                    map[mobCode] = new EncounterInfo(
                        DungeonKey: key,
                        DungeonName: name,
                        Category: category,
                        CategoryOrd: categoryOrd,
                        VariantLabel: label,
                        DungeonId: dungeonId,
                        BossIndex: bossIndex,
                        BossName: bossNames.GetValueOrDefault(bossIndex, string.Empty),
                        HasVariants: hasVariants,
                        Difficulty: difficulty,
                        Stage: stage);
                }
            }
        }

        return map.Count > 0 ? new EncounterCatalog(map) : Empty;
    }

    /// <summary>The catalog entry for a boss mobCode, or null when the web does not publish stats for it.</summary>
    public EncounterInfo? Lookup(int mobCode) =>
        _byMobCode.TryGetValue(mobCode, out EncounterInfo info) ? info : null;

    /// <summary>Whether a battle on this boss is worth uploading. An unloaded catalog answers true for
    /// everything, so a missing asset degrades to the pre-catalog behaviour instead of blocking uploads.</summary>
    public bool IsSupported(int mobCode) => !IsLoaded || _byMobCode.ContainsKey(mobCode);

    /// <summary>The boss name to SHOW for a mobCode — with its difficulty/stage appended when the catalog knows
    /// one. Falls back to <paramref name="mobName"/> untouched for anything uncatalogued (field bosses, trash,
    /// new content). Never used for the upload payload: the web matches on the raw name.</summary>
    public string DisplayName(int mobCode, string? mobName)
    {
        string fallback = mobName ?? string.Empty;
        if (Lookup(mobCode) is not EncounterInfo info || !info.HasVariants || info.VariantLabel.Length == 0)
        {
            return fallback;
        }

        // Prefer the live mob name over the catalog's: the catalog's boss names are the web's display names and
        // can drift from mobs.json (e.g. "바실루스" vs "위악의 바실루스"). Only the SUFFIX comes from here.
        return fallback.Length > 0 ? $"{fallback} ({info.VariantLabel})" : info.DisplayBossName;
    }

    private static string? NullableStr(JsonElement el, string name) =>
        el.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static string Str(JsonElement el, string name) =>
        el.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? string.Empty
            : string.Empty;
}

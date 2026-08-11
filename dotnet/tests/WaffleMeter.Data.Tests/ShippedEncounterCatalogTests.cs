using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using WaffleMeter.Capture;
using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>
/// Guards the SHIPPED encounter catalog (Assets/json/encounters.json), which the upload gate depends on: a
/// mobCode missing here means that battle is never uploaded, and a mobCode here that mobs.json doesn't mark as
/// a boss means a battle that can never start in the first place. Regenerate the asset with
/// <c>dotnet/tools/export-encounters.ts</c> when the stats web's catalog changes.
/// </summary>
public sealed class ShippedEncounterCatalogTests
{
    private static string AssetsJsonDir()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "Assets", "json");
            if (File.Exists(Path.Combine(candidate, "encounters.json")))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Assets/json/encounters.json not found above " + AppContext.BaseDirectory);
    }

    private static EncounterCatalog Shipped() =>
        EncounterCatalog.Load(Path.Combine(AssetsJsonDir(), "encounters.json"));

    private static Dictionary<int, Mob> ShippedMobs() =>
        ReferenceJson.LoadMobs(Path.Combine(AssetsJsonDir(), "mobs.json"));

    /// <summary>The raw asset. <see cref="EncounterCatalog"/> keys by mobCode, so defects that live at the
    /// VARIANT level — a duplicate dungeonId, a variant with no mobs — are invisible through it.</summary>
    private static JsonElement ShippedJson() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(AssetsJsonDir(), "encounters.json"))).RootElement;

    [Fact]
    public void The_shipped_catalog_loads()
    {
        EncounterCatalog catalog = Shipped();

        Assert.True(catalog.IsLoaded);
        Assert.True(catalog.Count >= 150, $"only {catalog.Count} mobCodes — did the export drop dungeons?");
    }

    /// <summary>Every catalogued boss must exist in mobs.json AND be flagged <c>boss</c>. A code that isn't a
    /// boss never opens a battle, so the meter would silently record nothing for that encounter.</summary>
    [Fact]
    public void Every_catalogued_code_is_a_known_boss_in_mobs_json()
    {
        EncounterCatalog catalog = Shipped();
        Dictionary<int, Mob> mobs = ShippedMobs();

        var missing = new List<int>();
        var notBoss = new List<int>();
        foreach (int code in AllCodes(catalog))
        {
            if (!mobs.TryGetValue(code, out Mob? mob))
            {
                missing.Add(code);
            }
            else if (!mob.Boss)
            {
                notBoss.Add(code);
            }
        }

        Assert.True(missing.Count == 0, "not in mobs.json: " + string.Join(", ", missing));
        Assert.True(notBoss.Count == 0, "in mobs.json but boss=false: " + string.Join(", ", notBoss));
    }

    /// <summary>The lookup is a plain mobCode -> encounter map, so a code shared by two variants would make the
    /// difficulty ambiguous and silently mis-label one of them. The exporter also rejects this; assert it here
    /// too so a hand-edited asset can't slip through.</summary>
    [Fact]
    public void No_mob_code_belongs_to_two_encounters()
    {
        List<int> codes = AllCodes(Shipped()).ToList();

        Assert.Equal(codes.Count, codes.Distinct().Count());
    }

    /// <summary>Every variant must carry mobs. A variant with an empty mob list maps no code, so it can never
    /// label a battle — it is dead weight that only shows the seed grew a row it shouldn't have.
    /// <para>Found 2026-08-11: 바크론의 공중섬 carried a second 시련 row labelled <c>"시련 13~16단계"</c> with no
    /// mobs at all. That string is the METER's runtime label for an unpinned trial level, so something on the
    /// round trip is minting variants out of what the client displayed rather than out of the game's data. The
    /// meter itself is not the culprit — its payload sends the canonical <c>difficulty</c> ("시련") and the level
    /// separately as numbers — but this asserts we notice next time.</para></summary>
    [Fact]
    public void Every_variant_carries_at_least_one_mob()
    {
        var empty = new List<string>();
        foreach (JsonElement dungeon in ShippedJson().GetProperty("dungeons").EnumerateArray())
        {
            foreach (JsonElement variant in dungeon.GetProperty("variants").EnumerateArray())
            {
                if (variant.GetProperty("mobs").GetArrayLength() == 0)
                {
                    empty.Add($"{dungeon.GetProperty("name").GetString()} / {variant.GetProperty("label").GetString()}");
                }
            }
        }

        Assert.True(empty.Count == 0, "variants with no mobs: " + string.Join(", ", empty));
    }

    /// <summary>A dungeonId identifies ONE instance, so two variants claiming the same one cannot both be right
    /// — and the pairing is what any map-based cross-check would have to trust.
    /// <para>Found 2026-08-11: 무스펠의 성배 보통 and 어려움 both claimed 620021, while the capture corpus shows
    /// the game running them as 620022 and 620021 respectively. Harmless for the meter's own labels (those key
    /// off mobCode, which is unique) but wrong in the seed the web aggregates by.</para></summary>
    [Fact]
    public void No_dungeon_id_belongs_to_two_variants()
    {
        var seen = new Dictionary<int, string>();
        var clashes = new List<string>();
        foreach (JsonElement dungeon in ShippedJson().GetProperty("dungeons").EnumerateArray())
        {
            string name = dungeon.GetProperty("name").GetString() ?? "?";
            foreach (JsonElement variant in dungeon.GetProperty("variants").EnumerateArray())
            {
                int id = variant.GetProperty("dungeonId").GetInt32();
                string here = $"{name} / {variant.GetProperty("label").GetString()}";
                if (seen.TryGetValue(id, out string? there))
                {
                    clashes.Add($"{id}: {there} vs {here}");
                }
                else
                {
                    seen[id] = here;
                }
            }
        }

        Assert.True(clashes.Count == 0, "dungeonId claimed twice: " + string.Join("; ", clashes));
    }

    /// <summary>The case that motivated the catalog: 시련 바크론 shares its boss NAMES with the other three
    /// difficulties but has its own codes, and the web registered it on 2026-08-03.</summary>
    [Fact]
    public void Bakron_trial_is_catalogued_and_distinct_from_the_other_difficulties()
    {
        EncounterCatalog catalog = Shipped();

        EncounterInfo? trial = catalog.Lookup(2300582);
        Assert.NotNull(trial);
        Assert.Equal("바크론의 공중섬", trial!.Value.DungeonName);
        Assert.Equal("시련", trial.Value.VariantLabel);
        Assert.Equal("바크론 (시련)", catalog.DisplayName(2300582, "바크론"));

        // Same boss, other difficulties — different codes, different labels.
        Assert.Equal("탐험", catalog.Lookup(2310812)!.Value.VariantLabel);
        Assert.Equal("보통", catalog.Lookup(2300812)!.Value.VariantLabel);
        Assert.Equal("어려움", catalog.Lookup(2320812)!.Value.VariantLabel);
    }

    /// <summary>The gate has to be tight where it matters: a field boss must NOT be uploadable, or we are back
    /// to spending a request the server answers 400 for.</summary>
    [Fact]
    public void A_field_boss_is_not_supported()
    {
        EncounterCatalog catalog = Shipped();

        Assert.False(catalog.IsSupported(2600068)); // 정령왕 아그로 (어비스 필드보스)
        Assert.False(catalog.IsSupported(2600520)); // 처형관 드라모스
    }

    /// <summary>Category labels are the web's three, and every dungeon carries one.</summary>
    [Fact]
    public void Categories_are_the_three_the_web_publishes()
    {
        EncounterCatalog catalog = Shipped();

        var categories = AllCodes(catalog)
            .Select(code => catalog.Lookup(code)!.Value.Category)
            .Distinct()
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(["성역", "원정", "초월"], categories);
    }

    // The catalog exposes lookups rather than an enumeration, so walk the shipped asset's codes directly.
    private static IEnumerable<int> AllCodes(EncounterCatalog catalog)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(
            File.ReadAllText(Path.Combine(AssetsJsonDir(), "encounters.json")));

        foreach (var dungeon in doc.RootElement.GetProperty("dungeons").EnumerateArray())
        {
            foreach (var variant in dungeon.GetProperty("variants").EnumerateArray())
            {
                foreach (var pair in variant.GetProperty("mobs").EnumerateArray())
                {
                    yield return pair[0].GetInt32();
                }
            }
        }
    }
}

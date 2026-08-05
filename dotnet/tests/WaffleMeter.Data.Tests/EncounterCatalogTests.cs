using System.IO;
using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>
/// Spec for the supported-encounter catalog: the upload gate ("would the stats web accept this boss?") and the
/// difficulty/stage suffix the meter puts on a boss name.
/// </summary>
public sealed class EncounterCatalogTests
{
    // 바크론의 공중섬 is the interesting shape: four difficulties, the same three boss NAMES in each, and a
    // different mobCode per difficulty. 심연의 재련 stands in for a single-variant (성역) dungeon.
    private const string Json = """
    {
      "dungeons": [
        {
          "key": "expedition-bakron-floating-island",
          "category": "원정",
          "categoryOrd": 1,
          "name": "바크론의 공중섬",
          "variantType": "difficulty",
          "bosses": [{"index": 1, "name": "티에"}, {"index": 2, "name": "타몬"}, {"index": 3, "name": "바크론"}],
          "variants": [
            {"label": "보통", "dungeonId": 600072, "mobs": [[2300810, 1], [2300811, 2], [2300812, 3]]},
            {"label": "시련", "dungeonId": 600074, "mobs": [[2300580, 1], [2300581, 2], [2300582, 3]]}
          ]
        },
        {
          "key": "sanctuary-rudra",
          "category": "성역",
          "categoryOrd": 3,
          "name": "심연의 재련 : 루드라",
          "variantType": "all",
          "bosses": [{"index": 1, "name": "영겁의 루드라"}],
          "variants": [{"label": "전체", "dungeonId": 600082, "mobs": [[2301014, 1]]}]
        }
      ]
    }
    """;

    [Fact]
    public void Maps_a_mob_code_to_its_dungeon_variant_and_boss()
    {
        EncounterCatalog catalog = EncounterCatalog.Parse(Json);

        EncounterInfo? trial = catalog.Lookup(2300582);
        Assert.NotNull(trial);
        Assert.Equal("바크론의 공중섬", trial!.Value.DungeonName);
        Assert.Equal("원정", trial.Value.Category);
        Assert.Equal("시련", trial.Value.VariantLabel);
        Assert.Equal(600074, trial.Value.DungeonId);
        Assert.Equal(3, trial.Value.BossIndex);
        Assert.Equal("바크론", trial.Value.BossName);
    }

    /// <summary>The whole point of the table: the same boss name at two difficulties is two mobCodes, so the
    /// difficulty is recoverable without knowing the map.</summary>
    [Fact]
    public void The_same_boss_at_another_difficulty_is_a_different_code()
    {
        EncounterCatalog catalog = EncounterCatalog.Parse(Json);

        Assert.Equal("시련", catalog.Lookup(2300582)!.Value.VariantLabel);
        Assert.Equal("보통", catalog.Lookup(2300812)!.Value.VariantLabel);
        Assert.Equal("바크론", catalog.Lookup(2300582)!.Value.BossName);
        Assert.Equal("바크론", catalog.Lookup(2300812)!.Value.BossName);
    }

    [Fact]
    public void Display_name_appends_the_difficulty()
    {
        EncounterCatalog catalog = EncounterCatalog.Parse(Json);

        Assert.Equal("바크론 (시련)", catalog.DisplayName(2300582, "바크론"));
        Assert.Equal("바크론 (보통)", catalog.DisplayName(2300812, "바크론"));
    }

    /// <summary>A dungeon with one variant has nothing to disambiguate — "(전체)" would be noise.</summary>
    [Fact]
    public void Display_name_leaves_a_single_variant_dungeon_alone()
    {
        EncounterCatalog catalog = EncounterCatalog.Parse(Json);

        Assert.Equal("영겁의 루드라", catalog.DisplayName(2301014, "영겁의 루드라"));
    }

    /// <summary>Field bosses, trash and brand-new content aren't in the table; their names must pass through
    /// untouched rather than being blanked or decorated.</summary>
    [Fact]
    public void Display_name_passes_an_uncatalogued_mob_through()
    {
        EncounterCatalog catalog = EncounterCatalog.Parse(Json);

        Assert.Equal("정령왕 아그로", catalog.DisplayName(2600068, "정령왕 아그로"));
    }

    /// <summary>The live mob name wins over the catalog's — the web's display names can drift from mobs.json
    /// (e.g. "바실루스" vs "위악의 바실루스"), and only the SUFFIX should come from the catalog.</summary>
    [Fact]
    public void Display_name_keeps_the_live_mob_name()
    {
        EncounterCatalog catalog = EncounterCatalog.Parse(Json);

        Assert.Equal("위대한 바크론 (시련)", catalog.DisplayName(2300582, "위대한 바크론"));
    }

    [Fact]
    public void Supported_covers_exactly_the_catalogued_codes()
    {
        EncounterCatalog catalog = EncounterCatalog.Parse(Json);

        Assert.True(catalog.IsSupported(2300582));
        Assert.True(catalog.IsSupported(2301014));
        Assert.False(catalog.IsSupported(2600068)); // a field boss the web has no statistics for
    }

    /// <summary>Fail-open. A missing or broken asset must degrade to the pre-catalog behaviour (upload
    /// everything) rather than silently stopping every upload.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("{\"dungeons\":[]}")]
    public void An_empty_catalog_gates_nothing(string json)
    {
        EncounterCatalog catalog = json.Length == 0 ? EncounterCatalog.Empty : EncounterCatalog.Parse(json);

        Assert.False(catalog.IsLoaded);
        Assert.True(catalog.IsSupported(2600068));
        Assert.True(catalog.IsSupported(2300582));
        Assert.Equal("아무개", catalog.DisplayName(2300582, "아무개"));
    }

    [Fact]
    public void A_missing_file_loads_as_empty_instead_of_throwing()
    {
        EncounterCatalog catalog = EncounterCatalog.Load(Path.Combine(Path.GetTempPath(), "no-such-encounters.json"));

        Assert.False(catalog.IsLoaded);
        Assert.Equal(0, catalog.Count);
    }

    [Fact]
    public void A_malformed_variant_costs_only_that_variant()
    {
        EncounterCatalog catalog = EncounterCatalog.Parse("""
        {
          "dungeons": [
            {
              "key": "d", "category": "원정", "categoryOrd": 1, "name": "던전", "variantType": "difficulty",
              "bosses": [{"index": 1, "name": "보스"}],
              "variants": [
                {"label": "보통", "dungeonId": 1, "mobs": [[111, 1], ["nope"], [222]]},
                {"label": "어려움", "dungeonId": 2, "mobs": [[333, 1]]}
              ]
            }
          ]
        }
        """);

        Assert.Equal(2, catalog.Count);
        Assert.Equal("보통", catalog.Lookup(111)!.Value.VariantLabel);
        Assert.Equal("어려움", catalog.Lookup(333)!.Value.VariantLabel);
    }

    [Fact]
    public void DataManager_exposes_the_loaded_catalog()
    {
        var dm = new DataManager();
        Assert.False(dm.Encounters.IsLoaded); // nothing loaded yet -> gate is inert

        dm.LoadEncounters(EncounterCatalog.Parse(Json));

        Assert.True(dm.Encounters.IsSupported(2300582));
        Assert.False(dm.Encounters.IsSupported(2600068));
    }
}

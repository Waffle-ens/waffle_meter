using WaffleMeter.Capture;
using WaffleMeter.Data;
using WaffleMeter.Stats;
using Xunit;

namespace WaffleMeter.Stats.Tests;

/// <summary>
/// Spec for the encounter block of the upload payload: what it says about the dungeon a battle happened in,
/// and — the part that would break the server — that <c>bossName</c> stays the RAW mob name rather than the
/// difficulty-decorated one the meter's UI shows.
/// </summary>
public sealed class StatsPayloadEncounterTests
{
    private const string Catalog = """
    {
      "dungeons": [
        {
          "key": "expedition-bakron-floating-island", "category": "원정", "categoryOrd": 1,
          "name": "바크론의 공중섬", "variantType": "difficulty",
          "bosses": [{"index": 3, "name": "바크론"}],
          "variants": [
            {"label": "시련", "dungeonId": 600074, "difficulty": "시련", "stage": null,
             "mobs": [[2300582, 3]]}
          ]
        },
        {
          "key": "transcend-abyss-horn-cavern", "category": "초월", "categoryOrd": 2,
          "name": "심연의 뿔 암굴", "variantType": "stage",
          "bosses": [{"index": 1, "name": "어미잃은 변견 카푸"}],
          "variants": [
            {"label": "4단계", "dungeonId": 610033, "difficulty": null, "stage": "4",
             "mobs": [[2300544, 1]]}
          ]
        }
      ]
    }
    """;

    private static DataManager Party()
    {
        var dm = new DataManager();
        dm.SaveNickname(1, "Me", isExecutor: true, server: 3, jobByte: 5);
        dm.SaveUserPower(1, 500_000);
        dm.SaveNickname(2, "Ally", isExecutor: false, server: 3, jobByte: 25);
        dm.SaveUserPower(2, 400_000);
        dm.LoadEncounters(EncounterCatalog.Parse(Catalog));
        return dm;
    }

    private static DpsLog Log(DataManager dm, int mobCode, string mobName)
    {
        User me = dm.User(1)!;
        User ally = dm.User(2)!;
        return new DpsLog
        {
            Report = new DpsReport
            {
                Contributors = [me, ally],
                BattleStart = 1_000_000,
                BattleEnd = 1_030_000,
                Target = new MobInfo(100, new Mob(mobCode, mobName, true), remainHp: 0, maxHp: 1_000_000),
                Information = new Dictionary<int, DpsInformation>
                {
                    [1] = new DpsInformation(1_000_000, 50_000, 60.0, 40.0),
                    [2] = new DpsInformation(600_000, 30_000, 40.0, 24.0),
                },
            },
            SkillDetails = new Dictionary<int, Dictionary<string, AnalyzedSkill>>
            {
                [1] = new() { ["11020001"] = new AnalyzedSkill { SkillCode = 11020001, Name = "강타", DamageAmount = 1_000_000, Times = 100 } },
            },
            BuffRates = new Dictionary<int, List<OperatingData>>(),
            BossBuffRates = [],
        };
    }

    private static StatsEncounterPayload Encounter(DataManager dm, int mobCode, string mobName)
    {
        var builder = new StatsPayloadBuilder(dm, () => false);
        BuildResult result = builder.Build(Log(dm, mobCode, mobName), "test", killConfirmed: true);
        return Assert.IsType<BuildResult.Payload>(result).Value.Encounter;
    }

    /// <summary>원정 variants ride as a difficulty; the stage field stays null.</summary>
    [Fact]
    public void Reports_the_dungeon_and_difficulty_for_an_expedition_boss()
    {
        StatsEncounterPayload encounter = Encounter(Party(), 2300582, "바크론");

        Assert.Equal(2300582, encounter.MobCode);
        Assert.Equal("바크론의 공중섬", encounter.DungeonName);
        Assert.Equal("원정", encounter.Category);
        Assert.Equal("시련", encounter.Difficulty);
        Assert.Null(encounter.Stage);
        Assert.Equal(3, encounter.BossIndex);
    }

    /// <summary>초월 variants ride as a numbered stage — as TEXT, because the server's schema types it that
    /// way next to difficulty and rejects a number.</summary>
    [Fact]
    public void Reports_a_numbered_stage_as_text_for_a_transcend_boss()
    {
        StatsEncounterPayload encounter = Encounter(Party(), 2300544, "어미잃은 변견 카푸");

        Assert.Equal("심연의 뿔 암굴", encounter.DungeonName);
        Assert.Equal("초월", encounter.Category);
        Assert.Null(encounter.Difficulty);
        Assert.Equal("4", encounter.Stage);
    }

    /// <summary>The decorated name is a UI concern only. The server falls back to matching on bossName, so
    /// sending "바크론 (시련)" would break that fallback for anything the mobCode doesn't resolve.</summary>
    [Fact]
    public void Boss_name_stays_raw_never_the_difficulty_decorated_one()
    {
        DataManager dm = Party();

        StatsEncounterPayload encounter = Encounter(dm, 2300582, "바크론");

        Assert.Equal("바크론", encounter.BossName);
        Assert.DoesNotContain("시련", encounter.BossName);
        // ...even though that is exactly what the meter puts on screen for this fight.
        Assert.Equal("바크론 (시련)", dm.Encounters.DisplayName(2300582, "바크론"));
    }

    /// <summary>An uncatalogued boss still uploads with just its code and name (the upload gate decides whether
    /// it goes at all — the builder must not invent dungeon fields for it).</summary>
    [Fact]
    public void Leaves_the_descriptive_fields_null_for_an_uncatalogued_boss()
    {
        StatsEncounterPayload encounter = Encounter(Party(), 2600068, "정령왕 아그로");

        Assert.Equal(2600068, encounter.MobCode);
        Assert.Equal("정령왕 아그로", encounter.BossName);
        Assert.Null(encounter.DungeonName);
        Assert.Null(encounter.Category);
        Assert.Null(encounter.Difficulty);
        Assert.Null(encounter.Stage);
        Assert.Null(encounter.BossIndex);
    }
}

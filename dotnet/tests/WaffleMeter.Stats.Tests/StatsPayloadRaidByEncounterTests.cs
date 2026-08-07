using WaffleMeter.Capture;
using WaffleMeter.Data;
using WaffleMeter.Stats;
using Xunit;

namespace WaffleMeter.Stats.Tests;

/// <summary>
/// Spec for "is this battle a 공대", which decides whether participants carry a sub-party slot.
/// <para>The answer comes from WHICH DUNGEON the boss belongs to, not from how many party members the meter
/// managed to count. In the client's own dungeon table every 성역 is a 10-인 <c>Raid</c> and every 원정 and
/// 초월 is a <c>Party</c> capped at five (바크론 시련 included), so the boss mobCode settles it outright —
/// while the roster count was wrong in both directions.</para>
/// </summary>
public sealed class StatsPayloadRaidByEncounterTests
{
    private const int SanctuaryBoss = 2301014;   // 영겁의 루드라 — 심연의 재련 : 루드라 (성역)
    private const int ExpeditionBoss = 2300171;  // 완성체 베르크 — 크라오 동굴 보통 (원정, 5-인)

    /// <summary>Two dungeons, one per category — enough to answer the only question this code asks.</summary>
    private const string CatalogJson = """
    {
      "dungeons": [
        {
          "key": "sanctuary-rudra", "category": "성역", "categoryOrd": 3,
          "name": "심연의 재련 : 루드라", "variantType": "all",
          "bosses": [{ "index": 1, "name": "영겁의 루드라" }],
          "variants": [{ "label": "전체", "dungeonId": 600082, "mobs": [[2301014, 1]] }]
        },
        {
          "key": "expedition-krao-cave", "category": "원정", "categoryOrd": 1,
          "name": "크라오 동굴", "variantType": "difficulty",
          "bosses": [{ "index": 1, "name": "완성체 베르크" }],
          "variants": [{ "label": "보통", "dungeonId": 600001, "difficulty": "보통", "mobs": [[2300171, 1]] }]
        }
      ]
    }
    """;

    /// <summary><paramref name="dealers"/> people who dealt damage, each holding roster slot 1..n, against
    /// <paramref name="mobCode"/>, with the roster reporting <paramref name="rosterSize"/> members.</summary>
    private static (DataManager Dm, DpsLog Log) Battle(
        int dealers, int mobCode, int rosterSize, bool loadCatalog = true)
    {
        var dm = new DataManager();
        if (loadCatalog)
        {
            dm.LoadEncounters(EncounterCatalog.Parse(CatalogJson));
        }

        var contributors = new List<User>();
        var information = new Dictionary<int, DpsInformation>();
        var skillDetails = new Dictionary<int, Dictionary<string, AnalyzedSkill>>();
        var slots = new Dictionary<int, int>();

        for (int i = 1; i <= dealers; i++)
        {
            dm.SaveNickname(i, "P" + i, isExecutor: i == 1, server: 3, jobByte: 5);
            dm.SaveUserPower(i, 3000 + i);
            contributors.Add(dm.User(i)!);
            information[i] = new DpsInformation(1_000_000 - (i * 1000), 40_000, 100.0 / dealers, 80.0 / dealers);
            skillDetails[i] = new Dictionary<string, AnalyzedSkill>
            {
                ["11020001"] = new AnalyzedSkill { SkillCode = 11020001, Name = "강타", DamageAmount = 1_000_000, Times = 100 },
            };
            slots[i] = i;
        }

        var report = new DpsReport
        {
            Contributors = contributors,
            BattleStart = 1_000_000,
            BattleEnd = 1_030_000,
            Target = new MobInfo(100, new Mob(mobCode, "보스", true), remainHp: 0, maxHp: 1_000_000),
            Information = information,
            PartySlots = slots,
            PartyRosterSize = rosterSize,
        };
        return (dm, new DpsLog { Report = report, SkillDetails = skillDetails });
    }

    private static StatsUploadPayload Build(DataManager dm, DpsLog log)
    {
        var builder = new StatsPayloadBuilder(dm, publicCharacterProvider: () => false, clock: () => 1_700_000_000_000);
        return Assert.IsType<BuildResult.Payload>(builder.Build(log, "2.0.0", killConfirmed: true)).Value;
    }

    /// <summary>The bug. A roster stranded from an earlier 10-인 raid — SavePartyRoster's anti-shrink guard
    /// ignored the new party's smaller snapshots — made a four-man 원정 run upload as a raid.</summary>
    [Fact]
    public void A_five_man_dungeon_is_not_a_raid_even_when_the_roster_says_ten()
    {
        (DataManager dm, DpsLog log) = Battle(dealers: 4, mobCode: ExpeditionBoss, rosterSize: 10);

        StatsUploadPayload payload = Build(dm, log);

        Assert.All(payload.Participants, p => Assert.Null(p.PartySlot));
    }

    [Fact]
    public void A_five_man_dungeon_is_not_a_raid_when_a_phantom_member_inflates_the_roster()
    {
        // The 전투력-u32-as-a-member parser bug produced exactly this: a 5-인 party reporting six.
        (DataManager dm, DpsLog log) = Battle(dealers: 5, mobCode: ExpeditionBoss, rosterSize: 6);

        StatsUploadPayload payload = Build(dm, log);

        Assert.All(payload.Participants, p => Assert.Null(p.PartySlot));
    }

    /// <summary>The other direction, and the one that was silently costing real data: a 성역 raid whose 0x9702
    /// snapshot under-parsed (9 of 10 members — 62 such snapshots in the capture corpus) used to fail the
    /// <c>is 8 or 10</c> test and drop every sub-party tag it did have.</summary>
    [Fact]
    public void A_sanctuary_raid_stays_a_raid_when_its_roster_under_parses()
    {
        (DataManager dm, DpsLog log) = Battle(dealers: 10, mobCode: SanctuaryBoss, rosterSize: 9);

        StatsUploadPayload payload = Build(dm, log);

        Assert.All(payload.Participants, p => Assert.NotNull(p.PartySlot));
        Assert.Equal(Enumerable.Range(1, 10), payload.Participants.Select(p => p.PartySlot!.Value).Order());
    }

    [Fact]
    public void A_sanctuary_raid_is_a_raid_with_a_complete_roster_too()
    {
        (DataManager dm, DpsLog log) = Battle(dealers: 10, mobCode: SanctuaryBoss, rosterSize: 10);

        StatsUploadPayload payload = Build(dm, log);

        Assert.All(payload.Participants, p => Assert.NotNull(p.PartySlot));
    }

    /// <summary>Without a catalog the meter has nothing better than the old count, and must keep behaving as it
    /// did — a missing asset must not quietly turn every raid into a party. (This is also why every existing
    /// payload test, which builds no catalog, still describes current behaviour.)</summary>
    [Fact]
    public void Falls_back_to_the_roster_count_when_the_catalog_is_not_loaded()
    {
        (DataManager dm, DpsLog log) = Battle(dealers: 10, mobCode: SanctuaryBoss, rosterSize: 10, loadCatalog: false);

        StatsUploadPayload payload = Build(dm, log);

        Assert.All(payload.Participants, p => Assert.NotNull(p.PartySlot));
    }

    /// <summary>A boss the catalog does not know also falls back. Such a battle is blocked upstream by the
    /// upload queue's unsupported_encounter gate, so this is the shape of the rule, not a live path.</summary>
    [Fact]
    public void Falls_back_to_the_roster_count_for_a_boss_outside_the_catalog()
    {
        (DataManager dm, DpsLog log) = Battle(dealers: 10, mobCode: 999999, rosterSize: 10);

        StatsUploadPayload payload = Build(dm, log);

        Assert.All(payload.Participants, p => Assert.NotNull(p.PartySlot));
    }

    [Fact]
    public void The_catalog_knows_which_category_is_a_raid()
    {
        EncounterCatalog catalog = EncounterCatalog.Parse(CatalogJson);

        Assert.True(catalog.Lookup(SanctuaryBoss)!.Value.IsRaid);
        Assert.False(catalog.Lookup(ExpeditionBoss)!.Value.IsRaid);
    }
}

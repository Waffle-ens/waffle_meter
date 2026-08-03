using System.Text.Json;
using WaffleMeter.App.Core;
using WaffleMeter.Capture;
using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

/// <summary>
/// Pins the invariant the overlay depends on: the tier map is a pure function of the report handed in.
/// <para>The overlay used to receive tiers as a value pushed in beside the report (SetTiers → Update). Replaying
/// a saved battle called only Update, so a run's three bosses all rendered the LAST live evaluation — identical
/// percentages on every boss — and an older run rendered nothing at all, because its participants carry
/// different entity uids and the leftover map had no entry for them. OverlayViewModel.TierResolver now derives
/// the map inside Update; these tests pin the evaluator half of that contract.</para>
/// </summary>
public sealed class TierEvaluatorTests
{
    private static readonly double[] Grid =
    [
        100, 99.5, 99, 98, 96, 93, 90, 85, 80, 75, 70, 65, 60, 55, 50, 45, 40, 35, 30, 25, 20, 15, 12.5, 10, 7.5,
        5, 3, 2, 1, 0.5, 0.1,
    ];

    private const int FirstBoss = 2301059;
    private const int SecondBoss = 2311101;

    [Fact]
    public void Each_boss_of_a_run_is_scored_against_its_own_distribution()
    {
        // The reported symptom: "보스 3개 전부다 같은 상위% 로 보이는데". Same player, same dps, two bosses whose
        // distributions differ by 10x — the percentiles must differ.
        TierArtifact artifact = BuildArtifact(
            Row(bossIndex: 1, cutFloor: 100_000),
            Row(bossIndex: 2, cutFloor: 1_000_000));

        Dictionary<int, RowTier> first = TierEvaluator.Evaluate(Report(FirstBoss, (7, "본인", 900_000)), artifact);
        Dictionary<int, RowTier> second = TierEvaluator.Evaluate(Report(SecondBoss, (7, "본인", 900_000)), artifact);

        Assert.NotNull(first[7].BattleTopPercent);
        Assert.NotNull(second[7].BattleTopPercent);
        Assert.NotEqual(first[7].BattleTopPercent, second[7].BattleTopPercent);
        Assert.Equal("타락한 데바의 성 · 어려움", first[7].DungeonLabel);
        Assert.Equal("데우스 연구기지 · 1단계", second[7].DungeonLabel);
    }

    [Fact]
    public void An_older_run_is_scored_from_its_own_participants_not_the_previous_one()
    {
        // The other half of the symptom: "더 이전던전 전투기록을 보니까 아예 상위% 표기가 안되는데. 중간에 다른캐릭터를
        // 한번 다녀와서". A zone/character switch renumbers the entity uids, so a map left over from the previous
        // report has no key for anyone on screen. Evaluating the displayed report keys it correctly every time.
        TierArtifact artifact = BuildArtifact(Row(bossIndex: 1, cutFloor: 100_000));

        Dictionary<int, RowTier> recent = TierEvaluator.Evaluate(Report(FirstBoss, (7, "본인", 900_000)), artifact);
        Dictionary<int, RowTier> older = TierEvaluator.Evaluate(Report(FirstBoss, (4212, "본인", 900_000)), artifact);

        Assert.Equal([7], recent.Keys);
        Assert.Equal([4212], older.Keys);                                   // not empty, and not the old uid
        Assert.Equal(recent[7].BattleTopPercent, older[4212].BattleTopPercent);
    }

    [Fact]
    public void Without_career_tiers_the_rank_comes_from_that_battle()
    {
        // How a saved battle is evaluated: careerTiers is null, so the badge states 이번 전투 등급 rather than
        // today's standing — a record screen must not mix two points in time on one row.
        TierArtifact artifact = BuildArtifact(Row(bossIndex: 1, cutFloor: 100_000));
        DpsReport report = Report(FirstBoss, (7, "본인", 900_000));

        RowTier battle = TierEvaluator.Evaluate(report, artifact, careerTiers: null)[7];
        Assert.False(battle.IsCareer);
        Assert.Equal(TierLadder.TierRankOf(battle.BattleTopPercent!.Value), battle.TierRank);

        // With a career tier the rank is the standing, while the percentile still describes THIS fight.
        RowTier career = TierEvaluator.Evaluate(
            report, artifact, new Dictionary<string, int> { ["hash"] = 1 }, _ => "hash")[7];
        Assert.True(career.IsCareer);
        Assert.Equal(1, career.TierRank);
        Assert.Equal(battle.BattleTopPercent, career.BattleTopPercent);
    }

    private static DpsReport Report(int mobCode, params (int Uid, string Nick, double Dps)[] rows)
    {
        var report = new DpsReport
        {
            Target = new MobInfo(1, new Mob(mobCode, "보스", Boss: true)),
            BattleStart = 1_000_000,
            BattleEnd = 1_060_000, // 60s — comfortably past the 20s floor
        };

        foreach ((int uid, string nick, double dps) in rows)
        {
            report.Contributors.Add(new User(uid, nick, 2003, JobClass.ASSASSIN, power: 900_000));
            report.Information[uid] = new DpsInformation(dps * 60, dps, dps, dps);
        }

        return report;
    }

    /// <summary>An R0 row (per-boss, per-job, dps) — the rung a normal 5-man fight lands on.</summary>
    private static object Row(int bossIndex, long cutFloor)
    {
        long step = Math.Max(100, cutFloor / 10);
        var encoded = new long[Grid.Length];
        long previous = 0;
        for (int i = 0; i < Grid.Length; i++)
        {
            long quantised = (cutFloor + step * (Grid.Length - 1 - i)) / 100;
            encoded[i] = quantised - previous;
            previous = quantised;
        }

        return new
        {
            r = 0,
            m = "dps",
            k = bossIndex == 1 ? "원정" : "초월",
            d = bossIndex == 1 ? 11 : 12,
            v = bossIndex == 1 ? 3 : 1,
            b = 1,
            j = "살성",
            s = 0,
            p = 5,
            n = 1842,
            c = encoded,
        };
    }

    private static TierArtifact BuildArtifact(params object[] rows)
    {
        var document = new
        {
            schemaVersion = 1,
            artifactId = "test-artifact",
            windowDays = 7,
            generatedAt = "2026-08-03T04:00:11.000Z",
            grid = Grid,
            tierCuts = new[] { 1, 5, 10, 30, 50, 70, 90 },
            jobs = new[] { "검성", "수호성", "살성", "궁성", "마도성", "정령성", "치유성", "호법성", "권성" },
            dungeons = new object[]
            {
                new { ord = 11, key = "expedition-fallen-deva-castle", name = "타락한 데바의 성", category = "원정" },
                new { ord = 12, key = "transcend-deus-research-base", name = "데우스 연구기지", category = "초월" },
            },
            variants = new object[]
            {
                new { dungeonOrd = 11, ord = 3, label = "어려움" },
                new { dungeonOrd = 12, ord = 1, label = "1단계" },
            },
            mobs = new Dictionary<string, int[]>
            {
                [FirstBoss.ToString()] = [11, 3, 1],
                [SecondBoss.ToString()] = [12, 1, 1],
            },
            rows,
        };

        TierArtifact? artifact = TierArtifact.Parse(JsonSerializer.Serialize(document));
        Assert.NotNull(artifact);
        return artifact!;
    }
}

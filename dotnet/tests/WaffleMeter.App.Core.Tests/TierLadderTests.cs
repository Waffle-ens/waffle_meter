using System.Text;
using System.Text.Json;
using WaffleMeter.App.Core;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

/// <summary>
/// Spec for the client half of the tier contract. Every number here is a WIRE agreement with the stats web —
/// if one drifts the meter prints a different "상위 X.X%" than the site does for the same fight.
/// </summary>
public sealed class TierLadderTests
{
    // The real grid the server ships (31 anchors, descending top-percent).
    private static readonly double[] Grid =
    [
        100, 99.5, 99, 98, 96, 93, 90, 85, 80, 75, 70, 65, 60, 55, 50, 45, 40, 35, 30, 25, 20, 15, 12.5, 10, 7.5,
        5, 3, 2, 1, 0.5, 0.1,
    ];

    [Theory]
    // The eight boundaries are INCLUSIVE upper bounds: (0,1] 챌린저 … (90,∞) 아이언.
    [InlineData(1.0, 1, "챌린저")]
    [InlineData(1.1, 2, "마스터")]
    [InlineData(5.0, 2, "마스터")]
    [InlineData(5.1, 3, "다이아")]
    [InlineData(10.0, 3, "다이아")]
    [InlineData(10.1, 4, "플래티넘")]
    [InlineData(30.0, 4, "플래티넘")]
    [InlineData(30.1, 5, "골드")]
    [InlineData(50.0, 5, "골드")]
    [InlineData(50.1, 6, "실버")]
    [InlineData(70.0, 6, "실버")]
    [InlineData(70.1, 7, "브론즈")]
    [InlineData(90.0, 7, "브론즈")]
    [InlineData(90.1, 8, "아이언")]
    public void Maps_each_tier_boundary_to_the_agreed_rank(double topPercent, int rank, string name)
    {
        Assert.Equal(rank, TierLadder.TierRankOf(topPercent));
        Assert.Equal(name, TierLadder.TierNameOf(rank));
    }

    [Fact]
    public void Rounds_before_mapping_so_the_printed_number_and_the_badge_never_disagree()
    {
        // 5.04 displays as "상위 5.0%". Testing the RAW value would put it past the 5.0 boundary and render
        // 다이아 next to a 5.0% label — the exact contradiction the contract forbids.
        Assert.Equal(5.0, TierLadder.RoundTopPercent(5.04));
        Assert.Equal(2, TierLadder.TierRankOf(5.04));
        Assert.Equal("상위 5.0%", TierLadder.FormatTopPercent(5.04));

        // Self-consistency over the whole range: the printed string always agrees with the badge.
        var random = new Random(20260803);
        for (int i = 0; i < 10_000; i++)
        {
            double raw = random.NextDouble() * 120 - 10; // deliberately includes out-of-range values
            double shown = TierLadder.RoundTopPercent(raw);
            Assert.Equal(TierLadder.TierRankOf(shown), TierLadder.TierRankOf(raw));
            Assert.Equal($"상위 {shown:0.0}%", TierLadder.FormatTopPercent(raw));
        }
    }

    [Fact]
    public void Never_prints_an_impossible_or_insulting_percentile()
    {
        Assert.Equal(0.1, TierLadder.RoundTopPercent(-10));
        Assert.Equal(0.1, TierLadder.RoundTopPercent(0));
        Assert.Equal(99.9, TierLadder.RoundTopPercent(100));
        Assert.Equal(99.9, TierLadder.RoundTopPercent(150));
        Assert.DoesNotContain("100.0", TierLadder.FormatTopPercent(100));
        Assert.DoesNotContain("0.0%", TierLadder.FormatTopPercent(0));
    }

    [Fact]
    public void Interpolates_between_cuts_and_saturates_outside_them()
    {
        long[] cuts = BuildLinearCuts(10_000, 1_000); // 10000, 11000, ... ascending with the descending grid

        Assert.Equal(100, TierLadder.TopPercentIn(Grid, cuts, 5_000));   // below the floor
        Assert.Equal(100, TierLadder.TopPercentIn(Grid, cuts, 10_000));  // exactly the floor
        Assert.Equal(0.1, TierLadder.TopPercentIn(Grid, cuts, 999_999)); // above the ceiling

        // Halfway between cuts[0] (=100%) and cuts[1] (=99.5%) is 99.75%.
        Assert.Equal(99.75, TierLadder.TopPercentIn(Grid, cuts, 10_500)!.Value, 6);
        // Landing exactly on an anchor returns that anchor.
        Assert.Equal(90, TierLadder.TopPercentIn(Grid, cuts, cuts[6])!.Value, 6);
        // A larger metric value must always mean a SMALLER top-percent.
        Assert.True(TierLadder.TopPercentIn(Grid, cuts, 20_000) < TierLadder.TopPercentIn(Grid, cuts, 15_000));
    }

    [Fact]
    public void Survives_a_flat_segment_instead_of_dividing_by_zero()
    {
        // Low-sample cohorts collapse several quantiles into one histogram bin, so equal neighbours are legal.
        long[] cuts = BuildLinearCuts(10_000, 1_000);
        cuts[5] = cuts[4];
        cuts[6] = cuts[4];

        double? percent = TierLadder.TopPercentIn(Grid, cuts, cuts[4]);
        Assert.NotNull(percent);
        Assert.False(double.IsNaN(percent!.Value));
        Assert.False(double.IsInfinity(percent.Value));
    }

    [Fact]
    public void Walks_the_ladder_and_stops_at_the_first_shipped_rung()
    {
        // Only R1 exists (party mode dropped), so an R0 lookup must fall through to it.
        TierArtifact artifact = BuildArtifact(
            Row(rung: 1, metric: "dps", category: "원정", d: 11, v: 3, b: 1, job: "검성", s: 3, p: 0, cutFloor: 10_000));

        TierCohort cohort = Cohort(artifact, dungeonOrd: 11, variantOrd: 3, bossIndex: 1, job: "검성", synergyCount: 3, partyMode: 5, trusted: true);
        TierEvaluation? result = TierLadder.Evaluate(artifact, cohort, dps: 10_500);

        Assert.NotNull(result);
        Assert.Equal(1, result!.Value.Rung);
        Assert.Equal(99.8, result.Value.TopPercent, 6); // 99.75 rounds away from zero to 99.8
        Assert.Equal(1, result.Value.TierRank is >= 1 and <= 8 ? 1 : 0);
    }

    [Fact]
    public void Skips_synergy_bucketed_rungs_when_the_raid_mask_is_untrusted()
    {
        // A raid whose sub-party slots were not resolved carries the WHOLE raid's synergy mask — it claims
        // synergies the player never received, so R0/R1 must be skipped even though the row exists.
        TierArtifact artifact = BuildArtifact(
            Row(rung: 0, metric: "dps", category: "원정", d: 11, v: 3, b: 1, job: "검성", s: 3, p: 10, cutFloor: 10_000));

        TierCohort trusted = Cohort(artifact, 11, 3, 1, "검성", 3, 10, trusted: true);
        TierCohort untrusted = trusted with { SynergyTrusted = false };

        Assert.NotNull(TierLadder.Evaluate(artifact, trusted, dps: 12_000));
        Assert.Null(TierLadder.Evaluate(artifact, untrusted, dps: 12_000));
    }

    [Fact]
    public void Reaches_the_pooled_rungs_because_the_whole_ladder_is_raw_dps()
    {
        // The lower rungs used to ship as ndps, which the meter cannot compute, so a cohort with no exact cell
        // resolved to null — the "only two of five party members show a percentile" report. Measured on the live
        // artifact: 43.5% of cells reachable, 56.5% present but unreadable, 0% genuinely sampleless. The web now
        // emits the whole ladder in raw dps, so a pooled rung answers.
        TierArtifact artifact = BuildArtifact(
            Row(rung: 3, metric: "dps", category: "원정", d: 11, v: 3, b: 1, job: "검성", s: -1, p: 0, cutFloor: 10_000));

        TierCohort cohort = Cohort(artifact, 11, 3, 1, "검성", 3, 5, trusted: true);
        TierEvaluation? result = TierLadder.Evaluate(artifact, cohort, dps: 12_000);

        Assert.NotNull(result);
        Assert.Equal(3, result!.Value.Rung); // fell through R0/R1/R2, answered at R3
    }

    [Fact]
    public void Untrusted_raid_still_skips_the_synergy_bucketed_rungs_but_keeps_the_pooled_ones()
    {
        // R0/R1 are aggregated only from synergy-trusted rows (the web's dps_trusted branch), so an incomplete
        // raid roster must not read them. R2+ pool synergy and carry every row, so they stay available — that is
        // what keeps a half-resolved raid from losing its percentile entirely.
        TierArtifact artifact = BuildArtifact(
            Row(rung: 0, metric: "dps", category: "원정", d: 11, v: 3, b: 1, job: "검성", s: 3, p: 10, cutFloor: 1_000_000),
            Row(rung: 2, metric: "dps", category: "원정", d: 11, v: 3, b: 1, job: "검성", s: -1, p: 10, cutFloor: 10_000));

        TierCohort untrusted = Cohort(artifact, 11, 3, 1, "검성", 3, 10, trusted: false);
        TierEvaluation? result = TierLadder.Evaluate(artifact, untrusted, dps: 12_000);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Value.Rung); // R0 skipped (untrusted mask), R2 answered
    }

    [Fact]
    public void Keeps_categories_apart_so_a_transcend_fight_never_reads_a_sanctuary_curve()
    {
        // R6 keeps one row per category; ignoring 'k' would match whichever sorted first.
        TierArtifact artifact = BuildArtifact(
            Row(6, "dps", "성역", -1, -1, -1, "검성", -1, 0, cutFloor: 1_000),
            Row(6, "dps", "초월", -1, -1, -1, "검성", -1, 0, cutFloor: 100_000));

        // Dungeon 12 is registered as 초월 by BuildArtifact, so the 초월 curve (high floor) must answer.
        TierCohort cohort = Cohort(artifact, 12, 1, 1, "검성", 0, 5, trusted: true);
        TierEvaluation? result = TierLadder.Evaluate(artifact, cohort, dps: 12_000);

        Assert.NotNull(result);
        Assert.Equal(6, result!.Value.Rung);
        // 12,000 sits at the very bottom of the 초월 curve (floor 100,000) → 상위 100%.
        // Against the 성역 curve (floor 1,000) it would have read far better, which is the bug this guards.
        Assert.Equal(99.9, result.Value.TopPercent);
    }

    [Fact]
    public void Fails_closed_on_an_unmapped_boss_or_an_ineligible_fight()
    {
        TierArtifact artifact = BuildArtifact(
            Row(0, "dps", "원정", 11, 3, 1, "검성", 3, 5, cutFloor: 10_000));

        // Unmapped mobCode — never fall back to a neighbouring dungeon.
        Assert.Null(TierLadder.CohortFor(artifact, mobCode: 9_999_999, job: "검성", power: 500_000, durationMs: 60_000, 3, 5, true));
        // Under the 20s floor and under the 400k power floor: outside the server's population.
        Assert.Null(TierLadder.CohortFor(artifact, 2301059, "검성", 500_000, 19_999, 3, 5, true));
        Assert.Null(TierLadder.CohortFor(artifact, 2301059, "검성", 399_999, 60_000, 3, 5, true));
        // A job outside the nine ranked classes.
        Assert.Null(TierLadder.CohortFor(artifact, 2301059, "음유성", 500_000, 60_000, 3, 5, true));
        // A party size the server does not bucket.
        Assert.Null(TierLadder.CohortFor(artifact, 2301059, "검성", 500_000, 60_000, 3, 7, true));
    }

    [Fact]
    public void Reads_boss_index_as_one_based_from_the_mob_map()
    {
        TierArtifact artifact = BuildArtifact(
            Row(0, "dps", "원정", 11, 3, 1, "검성", 3, 5, cutFloor: 10_000));

        TierCohort? cohort = TierLadder.CohortFor(artifact, 2301059, "검성", 500_000, 60_000, 3, 5, true);
        Assert.NotNull(cohort);
        Assert.Equal(1, cohort!.Value.BossIndex); // 0-based would never match any shipped row
    }

    [Fact]
    public void Decodes_quantised_delta_cuts_back_to_ascending_values()
    {
        TierArtifact artifact = BuildArtifact(
            Row(0, "dps", "원정", 11, 3, 1, "검성", 3, 5, cutFloor: 10_000));

        long[]? cuts = artifact.Cuts(new TierRowKey(
            0, TierLadder.MetricId("dps"), artifact.CategoryId("원정"), 11, 3, 1, artifact.JobId("검성"), 3, 5));

        Assert.NotNull(cuts);
        Assert.Equal(31, cuts!.Length);
        Assert.Equal(10_000, cuts[0]);
        for (int i = 1; i < cuts.Length; i++)
        {
            Assert.True(cuts[i] >= cuts[i - 1], "cuts must be non-decreasing");
            Assert.Equal(0, cuts[i] % 100); // /100 quantisation is part of the contract
        }
    }

    [Fact]
    public void Refuses_a_document_it_does_not_understand()
    {
        Assert.Null(TierArtifact.Parse(""));
        Assert.Null(TierArtifact.Parse("{ not json"));
        Assert.Null(TierArtifact.Parse("""{"schemaVersion":2,"artifactId":"a"}"""));   // future schema
        Assert.Null(TierArtifact.Parse("""{"schemaVersion":1}"""));                     // no id / grid / rows
    }

    // ---- helpers -------------------------------------------------------------------------------------

    private static long[] BuildLinearCutsRaw(long floor, long step)
    {
        var cuts = new long[Grid.Length];
        for (int i = 0; i < cuts.Length; i++)
        {
            cuts[i] = floor + (step * i);
        }

        return cuts;
    }

    private static long[] BuildLinearCuts(long floor, long step) => BuildLinearCutsRaw(floor, step);

    /// <summary>Encode a row the way the server does: /100 quantise, then delta.</summary>
    private static object Row(int rung, string metric, string category, int d, int v, int b, string job, int s, int p, long cutFloor)
    {
        long[] absolute = BuildLinearCutsRaw(cutFloor, Math.Max(100, cutFloor / 10));
        var encoded = new long[absolute.Length];
        long previous = 0;
        for (int i = 0; i < absolute.Length; i++)
        {
            long quantised = absolute[i] / 100;
            encoded[i] = quantised - previous;
            previous = quantised;
        }

        return new
        {
            r = rung,
            m = metric,
            k = category,
            d,
            v,
            b,
            j = job,
            s,
            p,
            n = 1842,
            c = encoded,
        };
    }

    private static TierArtifact BuildArtifact(params object[] rows)
    {
        var document = new
        {
            schemaVersion = 1,
            artifactId = "39c08db605c62b49",
            windowDays = 30,
            generatedAt = "2026-08-03T04:00:11.000Z",
            grid = Grid,
            tierCuts = new[] { 1, 5, 10, 30, 50, 70, 90 },
            jobs = new[] { "검성", "수호성", "살성", "궁성", "마도성", "정령성", "치유성", "호법성", "권성" },
            dungeons = new object[]
            {
                new { ord = 11, key = "expedition-fallen-deva-castle", name = "타락한 데바의 성", category = "원정" },
                new { ord = 12, key = "transcend-deus-research-base", name = "데우스 연구기지", category = "초월" },
                new { ord = 19, key = "sanctuary-muspels-holy-grail", name = "무스펠의 성배", category = "성역" },
            },
            variants = new object[]
            {
                new { dungeonOrd = 11, ord = 3, label = "어려움" },
                new { dungeonOrd = 12, ord = 1, label = "1단계" },
            },
            // 2301059 deliberately reuses the real 무스펠 code but is mapped onto dungeon 11 here so the
            // eligibility tests can exercise one placement without shipping the whole catalog.
            mobs = new Dictionary<string, int[]>
            {
                ["2301059"] = [11, 3, 1],
                ["2311101"] = [12, 1, 1],
            },
            rows,
        };

        string json = JsonSerializer.Serialize(document);
        TierArtifact? artifact = TierArtifact.Parse(json);
        Assert.NotNull(artifact);
        return artifact!;
    }

    private static TierCohort Cohort(TierArtifact a, int dungeonOrd, int variantOrd, int bossIndex, string job, int synergyCount, int partyMode, bool trusted) =>
        new(a.CategoryIdForDungeon(dungeonOrd), dungeonOrd, variantOrd, bossIndex, a.JobId(job), synergyCount, partyMode, trusted);
}

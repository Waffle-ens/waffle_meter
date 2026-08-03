namespace WaffleMeter.App.Core;

/// <summary>The cohort a single combat row belongs to, in artifact coordinates.
/// <para><paramref name="SynergyTrusted"/> is false for an 8/10-man raid whose sub-party slots were not fully
/// resolved: its synergy mask then describes the WHOLE raid, i.e. it claims synergies the player did not
/// actually receive. Those rows must skip the synergy-bucketed rungs (R0/R1) — the server excludes them from
/// the same rungs when it builds the distribution.</para></summary>
public readonly record struct TierCohort(
    int CategoryId,
    int DungeonOrd,
    int VariantOrd,
    int BossIndex,
    int JobId,
    int SynergyCount,
    int PartyMode,
    bool SynergyTrusted);

/// <summary>A resolved live percentile: which rung answered, and the rounded top-percent + tier it maps to.</summary>
public readonly record struct TierEvaluation(int Rung, int MetricId, double TopPercent, int TierRank)
{
    public string TierName => TierLadder.TierNameOf(TierRank);
}

/// <summary>
/// Pure client-side evaluation of the tier distribution artifact — no WPF, no I/O, fully unit-testable.
/// Every constant here is a wire contract with the stats web; changing one silently desynchronises the meter
/// from the server and produces a different "상위 X.X%" for the same fight.
/// </summary>
public static class TierLadder
{
    /// <summary>Tier names, index 0 = rank 1. <b>Rank 1 is the TOP (챌린저); rank 8 is 아이언.</b></summary>
    public static readonly string[] TierNames =
        ["챌린저", "마스터", "다이아", "플래티넘", "골드", "실버", "브론즈", "아이언"];

    /// <summary>Upper bounds of each tier in top-percent, inclusive: (0,1] 챌린저 … (90,∞) 아이언.</summary>
    private static readonly double[] Boundaries = [1, 5, 10, 30, 50, 70, 90];

    /// <summary>Displayed top-percent is clamped to this range so we never print the impossible "상위 0.0%"
    /// nor the insulting "상위 100.0%". Matches the server after commit 9f7cb67 (TS and SQL both).</summary>
    public const double MinTopPercent = 0.1;
    public const double MaxTopPercent = 99.9;

    /// <summary>Server-side sample gates. A row that fails these is not in the distribution's population, so
    /// claiming a percentile for it would be asserting membership of a cohort it was never part of.</summary>
    public const long MinBattleDurationMs = 20_000;
    public const int MinPower = 400_000;

    private const int MetricDps = 0;
    private const int MetricNdps = 1;

    /// <summary>Wire metric name → id. Unknown → -1.</summary>
    public static int MetricId(string? metric) => metric switch
    {
        "dps" => MetricDps,
        "ndps" => MetricNdps,
        _ => -1,
    };

    public static string TierNameOf(int rank) =>
        rank >= 1 && rank <= TierNames.Length ? TierNames[rank - 1] : string.Empty;

    /// <summary>Round to one decimal and clamp — the ONLY way a top-percent may reach a screen or a tier map.</summary>
    public static double RoundTopPercent(double value)
    {
        double rounded = Math.Round(value, 1, MidpointRounding.AwayFromZero);
        return Math.Min(MaxTopPercent, Math.Max(MinTopPercent, rounded));
    }

    /// <summary>Map a top-percent to a tier rank (1..8).
    /// <para>🔑 The value is rounded FIRST and the boundary test runs on the rounded number. Reversing this
    /// prints "상위 5.0%" next to a 다이아 badge, because 5.04 displays as 5.0 but tests as &gt; 5.</para></summary>
    public static int TierRankOf(double topPercent)
    {
        double p = RoundTopPercent(topPercent);
        for (int i = 0; i < Boundaries.Length; i++)
        {
            if (p <= Boundaries[i])
            {
                return i + 1;
            }
        }

        return TierNames.Length;
    }

    /// <summary>"상위 12.3%" — the single display format, so every surface agrees.</summary>
    public static string FormatTopPercent(double topPercent) =>
        $"상위 {RoundTopPercent(topPercent).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}%";

    /// <summary>
    /// Invert the quantile ladder: where does <paramref name="value"/> sit in this cohort?
    /// <para>The grid is descending top-percent and the cuts ascend in metric value, so a larger value maps to a
    /// SMALLER top-percent. Interpolation is linear in percent space between the two bracketing anchors, matching
    /// the contract's "이진탐색 + 선형 보간".</para>
    /// <para>Equal neighbouring cuts are legal (a low-sample cohort collapses quantiles into one histogram bin);
    /// the zero-width span is treated as t=0 rather than dividing by zero.</para>
    /// </summary>
    public static double? TopPercentIn(double[] grid, long[] cuts, double value)
    {
        if (grid.Length < 2 || cuts.Length != grid.Length || double.IsNaN(value))
        {
            return null;
        }

        if (value <= cuts[0])
        {
            return grid[0];
        }

        if (value >= cuts[^1])
        {
            return grid[^1];
        }

        // Invariant: cuts[lo] <= value < cuts[hi], hi = lo + 1 on exit.
        int lo = 0;
        int hi = cuts.Length - 1;
        while (hi - lo > 1)
        {
            int mid = lo + ((hi - lo) / 2);
            if (cuts[mid] <= value)
            {
                lo = mid;
            }
            else
            {
                hi = mid;
            }
        }

        long span = cuts[lo + 1] - cuts[lo];
        double t = span <= 0 ? 0.0 : (value - cuts[lo]) / span;
        return grid[lo] + ((grid[lo + 1] - grid[lo]) * t);
    }

    /// <summary>
    /// Walk R0 → R6 and answer with the FIRST rung that has a shipped row. Rows below the server's sample floor
    /// are simply absent, so "present == qualified" and there is nothing to re-check here.
    /// <para><b>ndps rungs (R2..R6) are only reachable when <paramref name="ndps"/> is supplied.</b> The meter does
    /// not compute ndps — normalising out received buffs needs the web's buff-gain model, which is a second source
    /// of truth we deliberately do not fork. Until that ships, a cohort that only has ndps rows resolves to null and
    /// the UI shows 표본 부족 rather than comparing a raw dps against a normalised distribution (which would
    /// systematically over-rate every player who received synergies).</para>
    /// </summary>
    /// <returns>null when no rung matched — the caller must render "표본 부족", never a guessed number.</returns>
    public static TierEvaluation? Evaluate(TierArtifact artifact, TierCohort cohort, double dps, double? ndps = null)
    {
        if (artifact == null || cohort.CategoryId < 0 || cohort.JobId < 0 || dps <= 0)
        {
            return null;
        }

        for (int rung = 0; rung <= 6; rung++)
        {
            // R0/R1 bucket by synergy; an untrusted raid mask would place the row in a cohort it never
            // belonged to, so those rungs are skipped exactly as the server skips them when aggregating.
            if (rung <= 1 && !cohort.SynergyTrusted)
            {
                continue;
            }

            int metricId = rung <= 1 ? MetricDps : MetricNdps;
            double? observed = metricId == MetricDps ? dps : ndps;
            if (observed is not double sample || sample <= 0)
            {
                continue;
            }

            TierRowKey key = KeyFor(rung, metricId, cohort);
            long[]? cuts = artifact.Cuts(key);
            if (cuts == null)
            {
                continue;
            }

            double? raw = TopPercentIn(artifact.Grid, cuts, sample);
            if (raw is not double percent)
            {
                continue;
            }

            double rounded = RoundTopPercent(percent);
            return new TierEvaluation(rung, metricId, rounded, TierRankOf(rounded));
        }

        return null;
    }

    /// <summary>The row coordinate a given rung looks up. Sentinels: -1 removes an axis, party mode uses 0.
    /// Category and job are carried through every rung untouched.</summary>
    internal static TierRowKey KeyFor(int rung, int metricId, TierCohort c) => rung switch
    {
        // R0: everything explicit.
        0 => new TierRowKey(0, metricId, c.CategoryId, c.DungeonOrd, c.VariantOrd, c.BossIndex, c.JobId, c.SynergyCount, c.PartyMode),
        // R1: drop party mode (synergy matters more than raid size).
        1 => new TierRowKey(1, metricId, c.CategoryId, c.DungeonOrd, c.VariantOrd, c.BossIndex, c.JobId, c.SynergyCount, 0),
        // R2: pool synergy (safe now that the metric is normalised), keep party mode.
        2 => new TierRowKey(2, metricId, c.CategoryId, c.DungeonOrd, c.VariantOrd, c.BossIndex, c.JobId, -1, c.PartyMode),
        3 => new TierRowKey(3, metricId, c.CategoryId, c.DungeonOrd, c.VariantOrd, c.BossIndex, c.JobId, -1, 0),
        4 => new TierRowKey(4, metricId, c.CategoryId, c.DungeonOrd, c.VariantOrd, -1, c.JobId, -1, 0),
        5 => new TierRowKey(5, metricId, c.CategoryId, c.DungeonOrd, -1, -1, c.JobId, -1, 0),
        _ => new TierRowKey(6, metricId, c.CategoryId, -1, -1, -1, c.JobId, -1, 0),
    };

    /// <summary>Build a cohort from a finished/live battle. Returns null when the fight is outside the
    /// distribution's population (unmapped boss, unranked job, sub-20s, sub-400k power) — the caller then shows
    /// nothing rather than a percentile of a cohort this fight was never part of.</summary>
    public static TierCohort? CohortFor(
        TierArtifact artifact,
        int mobCode,
        string? job,
        int power,
        long durationMs,
        int synergyCount,
        int partyMode,
        bool synergyTrusted)
    {
        if (artifact == null || durationMs < MinBattleDurationMs || power < MinPower)
        {
            return null;
        }

        if (artifact.Placement(mobCode) is not TierMobPlacement placement)
        {
            return null; // fail-closed: an unmapped mobCode gets no tier, ever.
        }

        int jobId = artifact.JobId(job);
        int categoryId = artifact.CategoryIdForDungeon(placement.DungeonOrd);
        if (jobId < 0 || categoryId < 0)
        {
            return null;
        }

        if (partyMode != 5 && partyMode != 10)
        {
            return null;
        }

        return new TierCohort(
            categoryId,
            placement.DungeonOrd,
            placement.VariantOrd,
            placement.BossIndex,
            jobId,
            Math.Clamp(synergyCount, 0, 3),
            partyMode,
            synergyTrusted);
    }
}

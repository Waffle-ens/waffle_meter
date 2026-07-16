using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>
/// Locks the buff/debuff 가동률 interval-merge (BuffUptime.CoveredMs): overlapping/refreshed applications
/// of the same buff must NOT be double-counted, intervals are clamped to the fight window, and a real gap
/// between applications stays separate. This underpins DpsCalculator.GetBuffOperatingRate.
/// </summary>
public sealed class BuffUptimeTests
{
    [Fact]
    public void SingleInterval_CountsItsLength()
    {
        Assert.Equal(500L, BuffUptime.CoveredMs([(1000L, 1500L)], 0L, 5000L));
    }

    [Fact]
    public void OverlappingApplications_AreNotDoubleCounted()
    {
        // two refreshes [0,300] and [200,500] cover [0,500] = 500, not 600.
        Assert.Equal(500L, BuffUptime.CoveredMs([(0L, 300L), (200L, 500L)], 0L, 5000L));
    }

    [Fact]
    public void NestedApplication_CountsOuterOnly()
    {
        Assert.Equal(1000L, BuffUptime.CoveredMs([(0L, 1000L), (200L, 600L)], 0L, 5000L));
    }

    [Fact]
    public void ExactlyTouchingIntervals_AreMerged()
    {
        // [0,300] then [300,600] is contiguous -> 600.
        Assert.Equal(600L, BuffUptime.CoveredMs([(0L, 300L), (300L, 600L)], 0L, 5000L));
    }

    [Fact]
    public void GappedIntervals_StayDistinct()
    {
        // [0,300] gap [400,600] -> 300 + 200 = 500.
        Assert.Equal(500L, BuffUptime.CoveredMs([(0L, 300L), (400L, 600L)], 0L, 5000L));
    }

    [Fact]
    public void IntervalsAreClampedToWindow()
    {
        // [-100,200]->[0,200]=200 ; [4900,5200]->[4900,5000]=100 ; total 300.
        Assert.Equal(300L, BuffUptime.CoveredMs([(-100L, 200L), (4900L, 5200L)], 0L, 5000L));
    }

    [Fact]
    public void UnsortedInput_IsHandled()
    {
        Assert.Equal(600L, BuffUptime.CoveredMs([(300L, 600L), (0L, 300L), (200L, 400L)], 0L, 5000L));
    }

    [Fact]
    public void EmptyOrZeroWindow_ReturnsZero()
    {
        Assert.Equal(0L, BuffUptime.CoveredMs([], 0L, 5000L));
        Assert.Equal(0L, BuffUptime.CoveredMs([(0L, 500L)], 1000L, 1000L));
    }

    [Fact]
    public void MergeIntervals_UnionsOverlappingAndTouching_KeepsGapsSeparate()
    {
        // [0,300]+[200,500] overlap -> one run [0,500]; [700,900] is a distinct run after a real gap.
        List<(long Start, long End)> merged =
            BuffUptime.MergeIntervals([(0L, 300L), (200L, 500L), (700L, 900L)], 0L, 5000L);

        Assert.Equal(new List<(long, long)> { (0L, 500L), (700L, 900L) }, merged);
    }

    [Fact]
    public void MergeIntervals_ClampsToWindow_AndSorts()
    {
        List<(long Start, long End)> merged =
            BuffUptime.MergeIntervals([(4900L, 5200L), (-100L, 200L)], 0L, 5000L);

        // clamped to [0,200] and [4900,5000], returned in start order.
        Assert.Equal(new List<(long, long)> { (0L, 200L), (4900L, 5000L) }, merged);
    }

    [Fact]
    public void MergeIntervals_ExactlyTouching_MergeIntoOneRun()
    {
        Assert.Equal(
            new List<(long, long)> { (0L, 600L) },
            BuffUptime.MergeIntervals([(0L, 300L), (300L, 600L)], 0L, 5000L));
    }

    [Fact]
    public void MergeIntervals_EmptyOrZeroWindow_ReturnsEmpty()
    {
        Assert.Empty(BuffUptime.MergeIntervals([], 0L, 5000L));
        Assert.Empty(BuffUptime.MergeIntervals([(0L, 500L)], 1000L, 1000L));
    }

    [Fact]
    public void CoveredMs_EqualsSumOfMergedRuns_Invariant()
    {
        (long, long)[][] cases =
        [
            [(0L, 300L), (200L, 500L)],
            [(0L, 1000L), (200L, 600L)],
            [(0L, 300L), (300L, 600L)],
            [(0L, 300L), (400L, 600L)],
            [(-100L, 200L), (4900L, 5200L)],
            [(300L, 600L), (0L, 300L), (200L, 400L)],
        ];

        foreach ((long, long)[] intervals in cases)
        {
            long covered = BuffUptime.CoveredMs(intervals, 0L, 5000L);
            long summed = BuffUptime.MergeIntervals(intervals, 0L, 5000L).Sum(iv => iv.End - iv.Start);
            Assert.Equal(covered, summed);
        }
    }
}

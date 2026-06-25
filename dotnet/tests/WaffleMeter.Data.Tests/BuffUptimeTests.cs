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
}

using WaffleMeter.Replay;
using Xunit;

namespace WaffleMeter.Replay.Tests;

/// <summary>Spec for <see cref="ReplayGeometry.IsTeleport"/> — the time-normalized teleport predicate the
/// replay players use to snap across recalls/blinks instead of drawing a map-crossing glide line.</summary>
public class ReplayGeometryTests
{
    private static ReplayPoint P(int tMs, float x, float y) => new(tMs, x, y, 0f);

    [Fact]
    public void Normal_10hz_step_is_not_a_teleport()
    {
        // ~100 ms apart, a few hundred units — ordinary in-combat movement.
        Assert.False(ReplayGeometry.IsTeleport(P(0, 0, 0), P(100, 300, 0)));
    }

    [Fact]
    public void A_recall_across_the_map_is_a_teleport()
    {
        // Tens of thousands of units in one tick — the artifact the guard exists to suppress.
        Assert.True(ReplayGeometry.IsTeleport(P(0, 0, 0), P(120, 40_000, 0)));
    }

    [Fact]
    public void A_fast_stride_across_a_sampling_gap_is_not_a_teleport()
    {
        // The regression the review caught: a distance-only threshold flagged this. A mount at ~2.7 u/ms
        // over a 1.4 s gap covers ~3800 units — legit locomotion, must glide, not freeze.
        Assert.False(ReplayGeometry.IsTeleport(P(0, 0, 0), P(1400, 3800, 0)));
    }

    [Fact]
    public void A_teleport_is_caught_even_across_a_long_span()
    {
        // Distance still an order of magnitude past the per-ms ceiling over the same 1.4 s gap.
        Assert.True(ReplayGeometry.IsTeleport(P(0, 0, 0), P(1400, 40_000, 0)));
    }

    [Fact]
    public void The_floor_keeps_a_tiny_span_from_over_flagging_jitter()
    {
        // A sub-floor jump over a 1 ms span must not read as a teleport (else decode jitter would freeze dots).
        Assert.False(ReplayGeometry.IsTeleport(P(0, 0, 0), P(1, 2000, 0)));
    }

    [Fact]
    public void A_zero_or_negative_span_does_not_throw_and_uses_the_floor()
    {
        Assert.False(ReplayGeometry.IsTeleport(P(500, 0, 0), P(500, 100, 0)));   // same timestamp, small jump
        Assert.True(ReplayGeometry.IsTeleport(P(500, 0, 0), P(500, 50_000, 0))); // same timestamp, huge jump
    }
}

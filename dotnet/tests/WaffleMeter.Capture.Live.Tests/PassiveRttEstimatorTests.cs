using WaffleMeter.Capture.Live;
using Xunit;

namespace WaffleMeter.Capture.Live.Tests;

/// <summary>Spec for the passive RTT estimator: ack matching, EWMA smoothing, expiry, wrap-safety.</summary>
public class PassiveRttEstimatorTests
{
    private const long Freq = 1000; // 1 tick = 1 ms for readable tests

    [Fact]
    public void Matching_ack_yields_the_round_trip_time()
    {
        var est = new PassiveRttEstimator(Freq);
        est.TrackOutbound(seq: 1000, payloadLength: 20, ts: 0);        // expects ack 1020
        Assert.True(est.TryResolveInbound(1020, ts: 42, out double ms)); // 42 ticks = 42 ms
        Assert.Equal(42, ms, 3);
    }

    [Fact]
    public void Non_matching_ack_does_not_resolve()
    {
        var est = new PassiveRttEstimator(Freq);
        est.TrackOutbound(1000, 20, 0);
        Assert.False(est.TryResolveInbound(999, 10, out _)); // ack for an earlier byte we didn't track
    }

    [Fact]
    public void Zero_payload_segments_are_ignored()
    {
        var est = new PassiveRttEstimator(Freq);
        est.TrackOutbound(1000, 0, 0);                       // pure ack, no data → nothing to match
        Assert.False(est.TryResolveInbound(1000, 10, out _));
    }

    [Fact]
    public void Expired_pending_samples_are_dropped()
    {
        var est = new PassiveRttEstimator(Freq);
        est.TrackOutbound(1000, 20, ts: 0);
        // ack arrives after the 500 ms expiry window → not a clean sample
        Assert.False(est.TryResolveInbound(1020, ts: 800, out _));
    }

    [Fact]
    public void Ewma_smooths_toward_new_samples()
    {
        var est = new PassiveRttEstimator(Freq);
        est.TrackOutbound(1000, 10, 0);
        est.TryResolveInbound(1010, 100, out double first);  // 100 ms → seeds
        est.TrackOutbound(2000, 10, 200);
        est.TryResolveInbound(2010, 220, out double second); // raw 20 ms → 100*0.9 + 20*0.1 = 92
        Assert.Equal(100, first, 3);
        Assert.Equal(92, second, 3);
    }

    [Fact]
    public void A_coalesced_ack_past_a_pending_segment_still_resolves()
    {
        var est = new PassiveRttEstimator(Freq);
        est.TrackOutbound(1000, 10, 0);  // expects 1010
        est.TrackOutbound(1010, 10, 5);  // expects 1020
        // a single ack for 1020 acknowledges both; the estimator drops 1010 and matches 1020
        Assert.True(est.TryResolveInbound(1020, 30, out double ms));
        Assert.Equal(25, ms, 3); // measured against the 1020 segment sent at ts=5
    }
}

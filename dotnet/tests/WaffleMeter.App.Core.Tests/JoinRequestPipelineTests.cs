using WaffleMeter.App.Core;
using WaffleMeter.Capture;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

/// <summary>
/// End-to-end backend path the join panel consumes: a real JoinRequest frame through StreamProcessor →
/// JoinRequestSinkAdapter (job-code → Korean name) → JoinRequestStore. Uses the corpus golden frame.
/// </summary>
public class JoinRequestPipelineTests
{
    // Corpus-verified first JoinRequest frame from session 20260604-175315.
    private static readonly byte[] GoldenJoinRequest =
    [
        0x3e, 0x07, 0x97, 0x1e, 0x23, 0x02, 0x00, 0xaa, 0x72, 0x01, 0x00, 0x00, 0x00, 0xe9,
        0x03, 0x1a, 0x00, 0x00, 0x00, 0x2d, 0x00, 0x00, 0x00, 0xec, 0x0f, 0x00, 0x00, 0x09,
        0xec, 0xbf, 0xb5, 0xed, 0x95, 0xb4, 0xec, 0xab, 0x91, 0xe9, 0x03, 0x00, 0x00, 0x00,
        0x00, 0xbf, 0x75, 0x06, 0x00, 0x00, 0x00, 0x00, 0x00, 0x6d, 0x95, 0xd8, 0x91, 0x9e,
        0x01, 0x00, 0x00,
    ];

    [Fact]
    public void JoinRequest_flows_through_adapter_into_store_with_resolved_job()
    {
        var store = new JoinRequestStore(() => 1_000_000);
        var proc = new StreamProcessor(joinSink: new JoinRequestSinkAdapter(store));

        proc.OnPacketReceived(GoldenJoinRequest, 1_000_000); // arrivedAt within the 20s window

        var snap = store.Snapshot();
        Assert.Single(snap);
        var r = snap[0];
        Assert.Equal(94890, r.Requester);
        Assert.Equal("쿵해쫑", r.Nickname);
        Assert.Equal("마도성", r.Job); // job code 26 → SORCERER → 마도성
        Assert.Equal(1001, r.Server);
        Assert.Equal(423359, r.Power);
    }

    [Fact]
    public void ExitParty_clears_the_store()
    {
        var store = new JoinRequestStore(() => 1_000_000);
        var proc = new StreamProcessor(joinSink: new JoinRequestSinkAdapter(store));
        int cleared = 0;
        store.Cleared += () => cleared++;

        proc.OnPacketReceived(GoldenJoinRequest, 1_000_000);
        Assert.Single(store.Snapshot());

        proc.OnPacketReceived([0x08, 0x1D, 0x97, 0x00, 0x00], 1_000_000); // ExitParty
        Assert.Empty(store.Snapshot());
        Assert.Equal(1, cleared);
    }
}

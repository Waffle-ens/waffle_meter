using System.Text;
using WaffleMeter.Capture;
using WaffleMeter.Capture.Live;
using Xunit;

namespace WaffleMeter.Capture.Live.Tests;

public sealed class CaptureWireProtocolTests
{
    [Fact]
    public void Segment_round_trips_through_encode_decode()
    {
        var original = new CapturedSegment(
            Seq: 0xFEDCBA98,
            Payload: new byte[] { 0x38, 0x04, 0x00, 0xFF, 0x10 },
            ArrivedAtMs: 1_749_500_000_123,
            SrcIp: "206.127.156.42");

        CapturedSegment decoded = CaptureWireProtocol.DecodeSegment(CaptureWireProtocol.EncodeSegment(original));

        Assert.Equal(original.Seq, decoded.Seq);
        Assert.Equal(original.ArrivedAtMs, decoded.ArrivedAtMs);
        Assert.Equal(original.SrcIp, decoded.SrcIp);
        Assert.Equal(original.Payload, decoded.Payload);
    }

    [Fact]
    public void Segment_round_trips_full_tuple()
    {
        var original = new CapturedSegment(
            Seq: 0x1000,
            Payload: new byte[] { 0x38, 0x04 },
            ArrivedAtMs: 5,
            SrcIp: "127.0.0.1",
            SrcPort: 51234,
            DstIp: "206.127.156.10",
            DstPort: 13328);

        CapturedSegment decoded = CaptureWireProtocol.DecodeSegment(CaptureWireProtocol.EncodeSegment(original));

        Assert.Equal(original.Seq, decoded.Seq);
        Assert.Equal(original.SrcIp, decoded.SrcIp);
        Assert.Equal(original.SrcPort, decoded.SrcPort);
        Assert.Equal(original.DstIp, decoded.DstIp);
        Assert.Equal(original.DstPort, decoded.DstPort);
        Assert.Equal(original.Payload, decoded.Payload);
        Assert.Equal("127.0.0.1:51234-206.127.156.10:13328", decoded.StreamKey);
    }

    [Fact]
    public void Segment_round_trips_with_empty_payload()
    {
        var original = new CapturedSegment(1, Array.Empty<byte>(), 0, "10.0.0.1");
        CapturedSegment decoded = CaptureWireProtocol.DecodeSegment(CaptureWireProtocol.EncodeSegment(original));
        Assert.Equal(original.Seq, decoded.Seq);
        Assert.Empty(decoded.Payload);
        Assert.Equal(original.SrcIp, decoded.SrcIp);
    }

    [Fact]
    public void Start_round_trips_backend_and_config()
    {
        var config = new CaptureConfig("206.127.156.0/24", "13328", TimeoutMs: 10, SnapshotSize: 65536);

        (string backend, CaptureConfig decoded) = CaptureWireProtocol.DecodeStart(
            CaptureWireProtocol.EncodeStart("npcap", config));

        Assert.Equal("npcap", backend);
        Assert.Equal(config.ServerIp, decoded.ServerIp);
        Assert.Equal(config.ServerPort, decoded.ServerPort);
        Assert.Equal(config.TimeoutMs, decoded.TimeoutMs);
        Assert.Equal(config.SnapshotSize, decoded.SnapshotSize);
    }

    [Fact]
    public void Start_defaults_unknown_backend_to_windivert()
    {
        (string backend, _) = CaptureWireProtocol.DecodeStart(
            CaptureWireProtocol.EncodeStart("anything-else", new CaptureConfig()));
        Assert.Equal("windivert", backend);
    }

    [Fact]
    public void Frames_round_trip_in_order_over_a_stream()
    {
        using var stream = new MemoryStream();
        CaptureWireProtocol.WriteFrame(stream, CaptureWireProtocol.FrameStarted, ReadOnlySpan<byte>.Empty);
        CaptureWireProtocol.WriteFrame(stream, CaptureWireProtocol.FrameSegment, new byte[] { 1, 2, 3 });
        CaptureWireProtocol.WriteFrame(stream, CaptureWireProtocol.FrameError, Encoding.UTF8.GetBytes("boom"));

        stream.Position = 0;

        (byte Type, byte[] Body)? f1 = CaptureWireProtocol.ReadFrame(stream);
        (byte Type, byte[] Body)? f2 = CaptureWireProtocol.ReadFrame(stream);
        (byte Type, byte[] Body)? f3 = CaptureWireProtocol.ReadFrame(stream);
        (byte Type, byte[] Body)? eof = CaptureWireProtocol.ReadFrame(stream);

        Assert.Equal(CaptureWireProtocol.FrameStarted, f1!.Value.Type);
        Assert.Empty(f1.Value.Body);
        Assert.Equal(CaptureWireProtocol.FrameSegment, f2!.Value.Type);
        Assert.Equal(new byte[] { 1, 2, 3 }, f2.Value.Body);
        Assert.Equal(CaptureWireProtocol.FrameError, f3!.Value.Type);
        Assert.Equal("boom", Encoding.UTF8.GetString(f3.Value.Body));
        Assert.Null(eof); // clean EOF between frames
    }
}

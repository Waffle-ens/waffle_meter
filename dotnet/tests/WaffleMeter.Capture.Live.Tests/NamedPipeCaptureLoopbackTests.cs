using System.IO.Pipes;
using WaffleMeter.Capture;
using WaffleMeter.Capture.Live;
using Xunit;

namespace WaffleMeter.Capture.Live.Tests;

/// <summary>
/// End-to-end check of the elevated-helper transport without any driver or admin: a real user-mode
/// named pipe carries segments from <see cref="CaptureHostServer"/> (fed by a fake backend) to
/// <see cref="NamedPipeCaptureClient"/>. Proves the isolated-elevation wiring delivers byte-identical
/// segments and surfaces start failures.
/// </summary>
public sealed class NamedPipeCaptureLoopbackTests
{
    private static NamedPipeServerStream NewServer(string pipeName) => new(
        pipeName,
        PipeDirection.InOut,
        maxNumberOfServerInstances: 1,
        PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous);

    [Fact]
    public void Segments_stream_from_helper_to_client_byte_identical()
    {
        string pipeName = "wm_test_" + Guid.NewGuid().ToString("N");
        CapturedSegment[] canned =
        {
            new(0x00000001, new byte[] { 0x38, 0x04, 0xAA }, 1_749_500_000_001, "206.127.156.10"),
            new(0xFFFFFFF0, new byte[] { 0x36, 0x45, 0x00, 0x01, 0x02 }, 1_749_500_000_055, "206.127.156.11"),
            new(0x10203040, Array.Empty<byte>(), 1_749_500_000_099, "206.127.156.12"),
        };

        var server = NewServer(pipeName);
        var fake = new FakeBackend(canned);
        var serverThread = new Thread(() =>
        {
            server.WaitForConnection();
            CaptureHostServer.Serve(server, (_, _) => fake);
            server.Dispose();
        }) { IsBackground = true };
        serverThread.Start();

        var received = new List<CapturedSegment>();
        using var allReceived = new ManualResetEventSlim(false);
        using var client = new NamedPipeCaptureClient("windivert", pipeName);
        client.SegmentReceived += seg =>
        {
            lock (received)
            {
                received.Add(seg);
                if (received.Count == canned.Length)
                {
                    allReceived.Set();
                }
            }
        };

        client.Start(new CaptureConfig());
        Assert.True(allReceived.Wait(5000), "did not receive all segments in time");
        client.Stop();
        serverThread.Join(5000);

        Assert.Equal(canned.Length, received.Count);
        for (int i = 0; i < canned.Length; i++)
        {
            Assert.Equal(canned[i].Seq, received[i].Seq);
            Assert.Equal(canned[i].ArrivedAtMs, received[i].ArrivedAtMs);
            Assert.Equal(canned[i].SrcIp, received[i].SrcIp);
            Assert.Equal(canned[i].Payload, received[i].Payload);
        }
    }

    [Fact]
    public void Backend_start_failure_is_reported_as_capture_error()
    {
        string pipeName = "wm_test_" + Guid.NewGuid().ToString("N");
        var server = NewServer(pipeName);
        var serverThread = new Thread(() =>
        {
            server.WaitForConnection();
            CaptureHostServer.Serve(server, (_, _) => new ThrowingBackend("driver load denied"));
            server.Dispose();
        }) { IsBackground = true };
        serverThread.Start();

        string? error = null;
        using var gotError = new ManualResetEventSlim(false);
        using var client = new NamedPipeCaptureClient("windivert", pipeName);
        client.CaptureError += msg =>
        {
            error = msg;
            gotError.Set();
        };

        client.Start(new CaptureConfig());
        Assert.True(gotError.Wait(5000), "no error frame received");
        Assert.Contains("driver load denied", error);
        client.Stop();
        serverThread.Join(5000);
    }

    private sealed class FakeBackend : IPacketCaptureBackend
    {
        private readonly CapturedSegment[] _segments;
        private Thread? _thread;
        private volatile bool _running;

        public FakeBackend(CapturedSegment[] segments) => _segments = segments;

        public event Action<CapturedSegment>? SegmentReceived;

        public void Start(CaptureConfig config)
        {
            _running = true;
            _thread = new Thread(() =>
            {
                foreach (CapturedSegment seg in _segments)
                {
                    if (!_running)
                    {
                        break;
                    }

                    SegmentReceived?.Invoke(seg);
                }
            }) { IsBackground = true };
            _thread.Start();
        }

        public void Stop()
        {
            _running = false;
            _thread?.Join(1000);
        }

        public void Dispose() => Stop();
    }

    private sealed class ThrowingBackend : IPacketCaptureBackend
    {
        private readonly string _message;

        public ThrowingBackend(string message) => _message = message;

        public event Action<CapturedSegment>? SegmentReceived;

        public void Start(CaptureConfig config)
        {
            _ = SegmentReceived; // unused; satisfies the interface event
            throw new InvalidOperationException(_message);
        }

        public void Stop()
        {
        }

        public void Dispose()
        {
        }
    }
}

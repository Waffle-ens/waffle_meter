using System.Text;
using WaffleMeter.Capture;

namespace WaffleMeter.Capture.Live;

/// <summary>
/// Server side of the capture-helper pipe protocol, factored out of the elevated host exe so it is
/// reusable and testable over any duplex <see cref="Stream"/> (a real named pipe in production, a
/// loopback pipe in tests). It reads the client's Start frame, runs the requested capture backend,
/// and forwards every <see cref="CapturedSegment"/> as a Segment frame until the client disconnects
/// or sends Stop. A start failure is reported as an Error frame and the session ends.
/// </summary>
public static class CaptureHostServer
{
    /// <summary>
    /// Serve one client on <paramref name="pipe"/>. Blocks until the client disconnects/sends Stop.
    /// <paramref name="backendFactory"/> maps the requested backend name + config to a backend
    /// (production: WinDivert/Npcap; tests: a fake), keeping this method driver-agnostic.
    /// </summary>
    public static void Serve(
        Stream pipe,
        Func<string, CaptureConfig, IPacketCaptureBackend> backendFactory,
        Action<string>? log = null)
    {
        (byte Type, byte[] Body)? first = CaptureWireProtocol.ReadFrame(pipe);
        if (first is null || first.Value.Type != CaptureWireProtocol.FrameStart)
        {
            return;
        }

        (string backendName, CaptureConfig config) = CaptureWireProtocol.DecodeStart(first.Value.Body);
        log?.Invoke($"start: backend={backendName} ip={config.ServerIp} port={config.ServerPort}");

        IPacketCaptureBackend backend = backendFactory(backendName, config);
        object writeLock = new();
        bool clientGone = false;

        // Batched pipe delivery. Each captured segment is a tiny frame, and writing + flushing
        // (FlushFileBuffers, a blocking kernel round-trip) once PER packet taxes the elevated helper even at
        // idle traffic — a constant per-packet syscall drip that steals CPU from the game (the confirmed cause
        // of the in-game frame drops). Instead, append frames to an in-memory buffer and hand them to the pipe
        // as ONE write+flush on a short cadence (or when the buffer crosses a size threshold under a burst).
        // The wire bytes are byte-for-byte identical — only the flush granularity changes — so the reader,
        // which frames on the length prefix, is unaffected.
        const int FlushIntervalMs = 10;
        const int FlushThresholdBytes = 48 * 1024;
        var sendBuf = new MemoryStream(64 * 1024);

        // Deliver everything buffered so far as a single write+flush. Safe to call from any thread (the lock
        // is reentrant, so callers may already hold writeLock).
        void FlushBuffered()
        {
            lock (writeLock)
            {
                if (clientGone || sendBuf.Length == 0)
                {
                    return;
                }

                try
                {
                    pipe.Write(sendBuf.GetBuffer(), 0, (int)sendBuf.Length);
                    pipe.Flush();
                }
                catch (IOException)
                {
                    clientGone = true;
                }

                sendBuf.SetLength(0);
            }
        }

        void Send(byte type, ReadOnlySpan<byte> body)
        {
            if (clientGone)
            {
                return;
            }

            lock (writeLock)
            {
                try
                {
                    CaptureWireProtocol.WriteFrame(sendBuf, type, body);
                }
                catch (IOException)
                {
                    clientGone = true;
                    return;
                }

                // Force delivery under a burst so the buffer (and added latency) stay bounded.
                if (sendBuf.Length >= FlushThresholdBytes)
                {
                    FlushBuffered();
                }
            }
        }

        long received = 0;
        backend.SegmentReceived += seg =>
        {
            Interlocked.Increment(ref received);
            Send(CaptureWireProtocol.FrameSegment, CaptureWireProtocol.EncodeSegment(seg));
        };

        // Forward passive RTT samples (throttled at the source) so the app can show server latency. Optional:
        // only a backend that supports it (WinDivert) raises this; old helper + new app degrades to "--ms".
        if (backend is WinDivertBackend rttBackend)
        {
            rttBackend.RttResolved += (key, ms, isLoopback) =>
                Send(CaptureWireProtocol.FramePing, CaptureWireProtocol.EncodePing(key, ms, isLoopback));
        }

        try
        {
            backend.Start(config);
        }
        catch (Exception ex)
        {
            log?.Invoke($"capture start FAILED: {ex.GetType().Name}: {ex.Message}");
            Send(CaptureWireProtocol.FrameError, Encoding.UTF8.GetBytes(ex.Message));
            FlushBuffered(); // control frame must reach the client before we tear down
            backend.Dispose();
            return;
        }

        Send(CaptureWireProtocol.FrameStarted, ReadOnlySpan<byte>.Empty);
        FlushBuffered(); // deliver the start ack immediately (not on the batch cadence)

        // Deliver buffered segment frames on a fixed cadence so DPS stays near real-time even when traffic is
        // below the size threshold. The 500 ms report interval dwarfs this, so there is no visible added delay.
        bool flushRunning = true;
        var flushThread = new Thread(() =>
        {
            while (Volatile.Read(ref flushRunning) && !clientGone)
            {
                Thread.Sleep(FlushIntervalMs);
                FlushBuffered();
            }
        })
        { IsBackground = true, Name = "capture-pipe-flush" };
        flushThread.Start();

        // Heartbeat so the operator can see whether the filter is actually matching traffic.
        using var stats = new System.Timers.Timer(2000) { AutoReset = true };
        stats.Elapsed += (_, _) => log?.Invoke($"captured segments: {Interlocked.Read(ref received)}");
        stats.Start();

        try
        {
            while (!clientGone)
            {
                (byte Type, byte[] Body)? frame = CaptureWireProtocol.ReadFrame(pipe);
                if (frame is null || frame.Value.Type == CaptureWireProtocol.FrameStop)
                {
                    break;
                }

                // The app classified a connection as high-volume non-game noise (P2P/streaming) — drop
                // it at the source so a flood can't starve the game's capture.
                if (backend is ISupportsConnectionExclusion excludable)
                {
                    if (frame.Value.Type == CaptureWireProtocol.FrameExclude && frame.Value.Body.Length >= 12)
                    {
                        excludable.ExcludeConnection(CaptureWireProtocol.DecodeConnKey(frame.Value.Body));
                    }
                    else if (frame.Value.Type == CaptureWireProtocol.FrameClearExclude)
                    {
                        excludable.ClearExclusions();
                    }
                }
            }
        }
        catch (IOException)
        {
            // client vanished mid-read
        }
        finally
        {
            flushRunning = false;
            flushThread.Join(200);
            FlushBuffered(); // deliver anything buffered at teardown
            backend.Stop();
            backend.Dispose();
        }
    }
}

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
                    CaptureWireProtocol.WriteFrame(pipe, type, body);
                }
                catch (IOException)
                {
                    clientGone = true;
                }
            }
        }

        backend.SegmentReceived += seg => Send(CaptureWireProtocol.FrameSegment, CaptureWireProtocol.EncodeSegment(seg));

        try
        {
            backend.Start(config);
        }
        catch (Exception ex)
        {
            Send(CaptureWireProtocol.FrameError, Encoding.UTF8.GetBytes(ex.Message));
            backend.Dispose();
            return;
        }

        Send(CaptureWireProtocol.FrameStarted, ReadOnlySpan<byte>.Empty);

        try
        {
            while (!clientGone)
            {
                (byte Type, byte[] Body)? frame = CaptureWireProtocol.ReadFrame(pipe);
                if (frame is null || frame.Value.Type == CaptureWireProtocol.FrameStop)
                {
                    break;
                }
            }
        }
        catch (IOException)
        {
            // client vanished mid-read
        }
        finally
        {
            backend.Stop();
            backend.Dispose();
        }
    }
}

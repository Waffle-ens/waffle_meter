using System.IO.Pipes;
using System.Text;
using WaffleMeter.Capture;

namespace WaffleMeter.Capture.Live;

/// <summary>
/// Client-side capture backend used by the UNELEVATED main app. It connects to the elevated
/// <c>WaffleMeter.CaptureHost</c> over a named pipe and re-raises each streamed
/// <see cref="CapturedSegment"/> — so the rest of the app sees an ordinary
/// <see cref="IPacketCaptureBackend"/> and never holds admin rights or touches the driver.
///
/// This is the inverse of <see cref="WinDivertBackend"/>/<see cref="NpcapBackend"/>: instead of
/// capturing in-process, it relays the helper's capture. The helper's chosen driver (WinDivert
/// default or Npcap option) is selected via the backend name passed to <see cref="Start(string, CaptureConfig)"/>.
/// </summary>
public sealed class NamedPipeCaptureClient : IPacketCaptureBackend
{
    private readonly string _pipeName;
    private readonly string _backend;
    private readonly int _connectTimeoutMs;
    private NamedPipeClientStream? _pipe;
    private Thread? _thread;
    private volatile bool _running;

    /// <summary>Raised if the helper reports a capture failure (e.g. driver load denied).</summary>
    public event Action<string>? CaptureError;

    public event Action<CapturedSegment>? SegmentReceived;

    public NamedPipeCaptureClient(
        string backend = "windivert",
        string pipeName = CaptureWireProtocol.DefaultPipeName,
        int connectTimeoutMs = 5000)
    {
        _backend = backend;
        _pipeName = pipeName;
        _connectTimeoutMs = connectTimeoutMs;
    }

    public void Start(CaptureConfig config)
    {
        var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            pipe.Connect(_connectTimeoutMs);
        }
        catch (Exception ex)
        {
            pipe.Dispose();
            throw new InvalidOperationException(
                $"could not connect to the capture helper pipe '{_pipeName}' — is WaffleMeter.CaptureHost running elevated?", ex);
        }

        _pipe = pipe;
        CaptureWireProtocol.WriteFrame(pipe, CaptureWireProtocol.FrameStart, CaptureWireProtocol.EncodeStart(_backend, config));

        _running = true;
        _thread = new Thread(ReadLoop) { IsBackground = true, Name = "pipe-capture-client" };
        _thread.Start();
    }

    private void ReadLoop()
    {
        try
        {
            while (_running)
            {
                (byte Type, byte[] Body)? frame = CaptureWireProtocol.ReadFrame(_pipe!);
                if (frame is null)
                {
                    break; // helper closed the pipe
                }

                switch (frame.Value.Type)
                {
                    case CaptureWireProtocol.FrameSegment:
                        SegmentReceived?.Invoke(CaptureWireProtocol.DecodeSegment(frame.Value.Body));
                        break;
                    case CaptureWireProtocol.FrameError:
                        CaptureError?.Invoke(Encoding.UTF8.GetString(frame.Value.Body));
                        break;
                    case CaptureWireProtocol.FrameStarted:
                        break; // ack only
                }
            }
        }
        catch (Exception ex) when (_running)
        {
            CaptureError?.Invoke($"capture pipe read failed: {ex.Message}");
        }
    }

    public void Stop()
    {
        if (!_running)
        {
            return;
        }

        _running = false;
        try
        {
            if (_pipe is { IsConnected: true })
            {
                CaptureWireProtocol.WriteFrame(_pipe, CaptureWireProtocol.FrameStop, ReadOnlySpan<byte>.Empty);
            }
        }
        catch
        {
            // helper may already be gone
        }

        _thread?.Join(1000);
    }

    public void Dispose()
    {
        Stop();
        _pipe?.Dispose();
        _pipe = null;
    }
}

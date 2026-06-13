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
public sealed class NamedPipeCaptureClient : IPacketCaptureBackend, ISupportsConnectionExclusion
{
    private readonly string _pipeName;
    private readonly string _backend;
    private readonly int _connectTimeoutMs;
    private readonly object _writeLock = new(); // serialize app->helper frames (Start/Stop/Exclude)
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
        NamedPipeClientStream pipe = ConnectWithRetry();

        _pipe = pipe;
        lock (_writeLock)
        {
            CaptureWireProtocol.WriteFrame(pipe, CaptureWireProtocol.FrameStart, CaptureWireProtocol.EncodeStart(_backend, config));
        }

        _running = true;
        _thread = new Thread(ReadLoop) { IsBackground = true, Name = "pipe-capture-client" };
        _thread.Start();
    }

    /// <summary>Tell the elevated helper to drop a connection at the source (P2P/streaming noise guard).
    /// Best-effort: the helper may already be gone, and the app drops the connection locally regardless.</summary>
    public void ExcludeConnection(ConnKey key)
    {
        NamedPipeClientStream? pipe = _pipe;
        if (pipe is not { IsConnected: true })
        {
            return;
        }

        try
        {
            lock (_writeLock)
            {
                CaptureWireProtocol.WriteFrame(pipe, CaptureWireProtocol.FrameExclude, CaptureWireProtocol.EncodeConnKey(key));
            }
        }
        catch
        {
            // helper gone / pipe broken — non-fatal; the local skip still applies
        }
    }

    /// <summary>Ask the helper to re-admit all dropped connections (from a user reset).</summary>
    public void ClearExclusions()
    {
        NamedPipeClientStream? pipe = _pipe;
        if (pipe is not { IsConnected: true })
        {
            return;
        }

        try
        {
            lock (_writeLock)
            {
                CaptureWireProtocol.WriteFrame(pipe, CaptureWireProtocol.FrameClearExclude, ReadOnlySpan<byte>.Empty);
            }
        }
        catch
        {
            // helper gone / pipe broken — non-fatal
        }
    }

    /// <summary>
    /// Connect within the SAME total budget (<see cref="_connectTimeoutMs"/>), retrying on transient
    /// failures with fresh streams. A healthy helper connects on the first attempt in milliseconds, so
    /// this never slows the happy path; only a failing connect re-attempts and the total wall-clock is
    /// unchanged. No relaunch/kill — the unelevated UI can't displace the elevated helper anyway; this is
    /// purely connect-side resilience plus a self-classifying failure message.
    /// </summary>
    private NamedPipeClientStream ConnectWithRetry()
    {
        const int perAttemptCapMs = 5000;
        const int retryGapMs = 200;
        long deadline = Environment.TickCount64 + _connectTimeoutMs;
        Exception? last = null;

        while (true)
        {
            int remaining = (int)(deadline - Environment.TickCount64);
            if (remaining <= 0)
            {
                break;
            }

            var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                pipe.Connect(Math.Min(remaining, perAttemptCapMs));
                return pipe;
            }
            catch (Exception ex)
            {
                pipe.Dispose();
                last = ex;
            }

            if (deadline - Environment.TickCount64 <= retryGapMs)
            {
                break;
            }

            Thread.Sleep(retryGapMs);
        }

        throw new InvalidOperationException(
            $"could not connect to the capture helper pipe '{_pipeName}' — {DiagnoseConnectFailure(last)}", last);
    }

    /// <summary>
    /// Turn a connect failure into a self-classifying message: whether the pipe NAME is present but
    /// unconnectable (helper busy/stale — the lone server instance is occupied) versus absent (the helper
    /// never served or has exited), plus the inner error detail. Maps a single screenshot to the right
    /// root-cause family without a repro.
    /// </summary>
    private string DiagnoseConnectFailure(Exception? ex)
    {
        string inner = ex is null
            ? "unknown error"
            : ex.GetType().Name + (ex.HResult != 0 ? $" (0x{ex.HResult:X8})" : string.Empty);
        return $"{PipeNamePresence(_pipeName)}; {inner} — is WaffleMeter.CaptureHost running elevated?";
    }

    private static string PipeNamePresence(string pipeName)
    {
        try
        {
            bool present = Directory.GetFiles(@"\\.\pipe\")
                .Any(p => string.Equals(Path.GetFileName(p), pipeName, StringComparison.OrdinalIgnoreCase));
            return present
                ? "pipe present but unconnectable (helper busy/stale — single instance occupied)"
                : "no helper pipe (CaptureHost not serving — never launched or exited)";
        }
        catch
        {
            return "pipe presence unknown";
        }
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
            lock (_writeLock)
            {
                if (_pipe is { IsConnected: true })
                {
                    CaptureWireProtocol.WriteFrame(_pipe, CaptureWireProtocol.FrameStop, ReadOnlySpan<byte>.Empty);
                }
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

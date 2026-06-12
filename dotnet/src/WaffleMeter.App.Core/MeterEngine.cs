using System.Diagnostics;
using System.Threading.Channels;
using WaffleMeter.Capture;
using WaffleMeter.Capture.Live;
using WaffleMeter.Data;

namespace WaffleMeter.App.Core;

/// <summary>
/// Owns the live capture loop on top of <see cref="MeterServices"/>: a capture backend feeds
/// segments into a channel; a single consumer thread drains them through the pipeline and, every
/// <c>reportIntervalMs</c>, computes the DPS report and raises <see cref="ReportUpdated"/>. The
/// report is computed on the consumer thread (the meter is single-owner), so UI handlers must
/// marshal to their own dispatcher.
/// </summary>
public sealed class MeterEngine : IDisposable
{
    private readonly MeterServices _services;
    private readonly IPacketCaptureBackend _backend;
    private readonly Channel<CapturedSegment> _channel;
    private readonly int _reportIntervalMs;
    private Thread? _consumer;
    private volatile bool _running;
    private volatile bool _resetRequested;
    private volatile bool _disposed;

    public MeterServices Services => _services;

    /// <summary>Raised (on the consumer thread) every report interval with the latest DPS report.</summary>
    public event Action<DpsReport>? ReportUpdated;

    /// <summary>Raised if the capture helper reports a failure (driver load denied, etc.).</summary>
    public event Action<string>? CaptureError;

    public MeterEngine(MeterServices services, IPacketCaptureBackend backend, int reportIntervalMs = 500)
    {
        _services = services;
        _backend = backend;
        _reportIntervalMs = reportIntervalMs;
        _channel = Channel.CreateUnbounded<CapturedSegment>(new UnboundedChannelOptions { SingleReader = true });
        _backend.SegmentReceived += seg => _channel.Writer.TryWrite(seg);
        if (backend is NamedPipeCaptureClient pipe)
        {
            pipe.CaptureError += msg => CaptureError?.Invoke(msg);
        }
    }

    /// <summary>Requests a full meter reset (clears saved battles + live data). Thread-safe: the actual
    /// reset runs on the consumer thread, which solely owns the meter state.</summary>
    public void RequestReset() => _resetRequested = true;

    public void Start() => Start(_services.BuildCaptureConfig());

    public void Start(CaptureConfig config)
    {
        // Idempotent + shutdown-safe: the launch runs on a background task that can take many seconds
        // (helper launch + pipe wait), so the user may quit (Dispose) before this fires. Don't start a
        // capture/consumer on a disposed or already-running engine.
        if (_disposed || _running)
        {
            return;
        }

        _backend.Start(config);
        _running = true;
        _consumer = new Thread(ConsumeLoop) { IsBackground = true, Name = "meter-consumer" };
        _consumer.Start();
    }

    private void ConsumeLoop()
    {
        ChannelReader<CapturedSegment> reader = _channel.Reader;
        var stopwatch = Stopwatch.StartNew();
        long lastReport = 0;
        while (_running)
        {
            while (reader.TryRead(out CapturedSegment segment))
            {
                // Defense-in-depth: with content-based capture, non-game / truncated TCP flows through
                // here. The parser dispatch already guards itself, but a framing-level throw (aligner /
                // assembler on garbage) must never kill the single consumer thread — swallow per-segment.
                try
                {
                    _services.Feed(segment);
                }
                catch
                {
                    // ignore one bad segment; keep draining
                }
            }

            // Reset hotkey: run on this (owner) thread, then push an immediate empty report so the
            // overlay clears without waiting for the next interval.
            if (_resetRequested)
            {
                _resetRequested = false;
                try
                {
                    _services.Calculator.HardReset();
                    _services.NotifyBattleListChanged(); // history was flushed — clear the panel
                }
                catch
                {
                    // never let a reset failure kill the consumer
                }

                ReportUpdated?.Invoke(_services.GetReport());
            }

            if (stopwatch.ElapsedMilliseconds - lastReport >= _reportIntervalMs)
            {
                lastReport = stopwatch.ElapsedMilliseconds;
                ReportUpdated?.Invoke(_services.GetReport());
            }

            Thread.Sleep(5);
        }
    }

    public void Stop()
    {
        if (!_running)
        {
            return;
        }

        _running = false;
        _backend.Stop();
        _consumer?.Join(1000);
    }

    public void Dispose()
    {
        _disposed = true;
        Stop();
        _backend.Dispose();
    }
}

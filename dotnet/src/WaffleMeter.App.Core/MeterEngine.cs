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
    // Volatile: the consumer thread reads it every loop; the UI thread writes it live from settings. A stale
    // read at worst delays the interval change by one loop (~5 ms), which is harmless.
    private volatile int _reportIntervalMs;
    private Thread? _consumer;
    private volatile bool _running;
    private volatile bool _resetRequested;
    private volatile bool _disposed;

    public MeterServices Services => _services;

    /// <summary>How often (ms) the consumer recomputes + pushes the report. Settable live from settings;
    /// clamped to [100, 1000]. Larger = less CPU/UI churn during combat (frame-drop relief) at the cost of
    /// coarser live DPS. A reset still pushes immediately regardless of this.</summary>
    public int ReportIntervalMs
    {
        get => _reportIntervalMs;
        set => _reportIntervalMs = Math.Clamp(value, 100, 1000);
    }

    /// <summary>Raised (on the consumer thread) every report interval with the latest DPS report.</summary>
    public event Action<DpsReport>? ReportUpdated;

    /// <summary>Raised if the capture helper reports a failure (driver load denied, etc.).</summary>
    public event Action<string>? CaptureError;

    /// <summary>Raised (on the consumer thread) right after a reset clears the meter, BEFORE the cleared
    /// report is pushed — so the UI can drop its own derived state (e.g. the recent-combat party tracker)
    /// without a one-frame flash of the stale party.</summary>
    public event Action? ResetCompleted;

    /// <summary>Raised (on the consumer thread) when the connected character is switched to a DIFFERENT
    /// character (not a same-character zone re-instance) — so the UI can drop its own per-character derived
    /// state (the recent-combat party tracker) that would otherwise linger as a stale idle preview row under
    /// the new character. Forwards <see cref="DataManager.ExecutorIdentityChanged"/>.</summary>
    public event Action? ExecutorChanged;

    public MeterEngine(MeterServices services, IPacketCaptureBackend backend, int reportIntervalMs = 500)
    {
        _services = services;
        _backend = backend;
        ReportIntervalMs = reportIntervalMs; // clamp through the setter
        // Bounded (not unbounded) so a sustained capture flood the single consumer can't drain cannot grow
        // the queue without limit (UI-heap bloat -> paging -> system-wide slowdown over long uptime).
        // DropOldest keeps the writer non-blocking and discards the oldest pending segment when full; the
        // data path already tolerates loss (the aligner skips gaps, the assembler resyncs), and in normal
        // play the queue stays near-empty so this never drops anything.
        _channel = Channel.CreateBounded<CapturedSegment>(new BoundedChannelOptions(50_000)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.DropOldest,
        });
        _backend.SegmentReceived += seg => _channel.Writer.TryWrite(seg);
        if (backend is NamedPipeCaptureClient pipe)
        {
            pipe.CaptureError += msg => CaptureError?.Invoke(msg);
            // Passive server latency: the helper resolves it per connection; the services layer keeps only
            // the sample matching the primary game stream. Thread-safe (volatile primary key + display fields).
            pipe.PingResolved += (key, ms, loop) => _services.AcceptPing(key, ms, loop);
        }

        // P2P/streaming noise guard: when the services classifier marks a connection as non-game noise,
        // forward it to the backend so it's dropped at the source (the pipe client relays to the helper).
        if (backend is ISupportsConnectionExclusion exclusion)
        {
            _services.ConnectionExcludeRequested += key => exclusion.ExcludeConnection(key);
        }

        // Forward a character switch up to the UI so it can drop per-character derived preview state (the
        // data layer already drops its 0x9702 roster snapshot in lockstep).
        _services.Data.ExecutorIdentityChanged += () => ExecutorChanged?.Invoke();
    }

    /// <summary>Requests a meter reset: clears the saved battles + live data but PRESERVES recognized
    /// characters (the executor + party) and the spawned-mob map, so combat info still appears on the next
    /// pull inside a dungeon with no zone reload. Thread-safe: the actual reset runs on the consumer thread,
    /// which solely owns the meter state.</summary>
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
                    // Soft reset: clears saved battles + live data but keeps recognized characters (executor +
                    // party) and the spawned-mob map, so the next pull still shows combat info even in a dungeon
                    // with no zone reload (HardReset would wipe those and the meter would stay blank until a zone
                    // change re-broadcast them). Use HardReset only for a true full wipe.
                    _services.Calculator.ResetKeepingCharacters();
                    // Recover from any noise-guard misclassification: re-admit excluded connections both
                    // locally and at the helper's source-side drop set.
                    _services.ClearExclusions();
                    if (_backend is ISupportsConnectionExclusion exclusion)
                    {
                        exclusion.ClearExclusions();
                    }

                    _services.NotifyBattleListChanged(); // history was flushed — clear the panel
                }
                catch
                {
                    // never let a reset failure kill the consumer
                }

                ResetCompleted?.Invoke(); // let the UI drop derived party state before the cleared report
                ReportUpdated?.Invoke(_services.GetReport());
            }

            if (stopwatch.ElapsedMilliseconds - lastReport >= _reportIntervalMs)
            {
                lastReport = stopwatch.ElapsedMilliseconds;
                ReportUpdated?.Invoke(_services.GetReport());
            }

            Thread.Sleep(5);
        }

        // Shutdown drain (still the owner thread): a battle that ended without reaching its save — the
        // user quits right after a wipe, or the end toggle was lost — would otherwise vanish, taking its
        // position replay with it. ResetDataStorage saves the pending battle iff non-empty & unsaved
        // (kills ride the normal save path long before this). Its OnBattleLogged writes the replay file
        // first and only then posts the history refresh via Dispatcher.BeginInvoke (non-blocking), so this
        // thread never waits on the UI thread that is itself joining us. Stop()'s Join then reaps us.
        try
        {
            _services.Calculator.ResetDataStorage();
        }
        catch
        {
            // never let the shutdown save throw out of the consumer thread
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
        // Generous join: the consumer runs the shutdown drain (save + replay-file write) as its last act
        // before exiting, so wait long enough for that IO to finish rather than kill it mid-write. It
        // returns as soon as the thread ends, so a clean no-save exit is still instant.
        _consumer?.Join(ShutdownDrainTimeoutMs);
    }

    private const int ShutdownDrainTimeoutMs = 5000;

    public void Dispose()
    {
        _disposed = true;
        Stop();
        _backend.Dispose();
    }
}

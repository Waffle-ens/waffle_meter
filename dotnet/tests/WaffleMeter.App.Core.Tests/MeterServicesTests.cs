using System.Text;
using WaffleMeter.App.Core;
using WaffleMeter.Capture;
using WaffleMeter.Capture.Live;
using WaffleMeter.Data;
using WaffleMeter.Services;
using WaffleMeter.Stats;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

public sealed class MeterServicesTests : IDisposable
{
    private readonly string _temp;
    private readonly MeterServices _services;

    public MeterServicesTests()
    {
        _temp = Path.Combine(Path.GetTempPath(), "wm_appcore_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_temp);
        _services = new MeterServices(new PropertyHandler(_temp));
    }

    public void Dispose()
    {
        _services.UploadQueue.Dispose();
        try
        {
            Directory.Delete(_temp, recursive: true);
        }
        catch
        {
            // best effort
        }
    }

    [Fact]
    public void Wires_the_full_object_graph()
    {
        Assert.NotNull(_services.Data);
        Assert.NotNull(_services.Calculator);
        Assert.NotNull(_services.OfficialLookup);
        Assert.Same(_services.OfficialLookup, GetInjectedLookup(_services));
        Assert.NotNull(_services.Consent);
        Assert.NotNull(_services.StatsBuilder);
        Assert.NotNull(_services.UploadQueue);
        Assert.NotNull(_services.Calculator.OnBattleLogged); // Data -> Stats edge wired
        // Version comes from the build (assembly InformationalVersion), not a persisted property;
        // the injectable appVersion path is the deterministic one to assert.
        Assert.False(string.IsNullOrWhiteSpace(_services.Version));
        Assert.Equal("9.9.9-test", new MeterServices(new PropertyHandler(_temp), appVersion: "9.9.9-test").Version);

        static IOfficialCharacterLookup? GetInjectedLookup(MeterServices s) => s.Data.OfficialLookup;
    }

    [Fact]
    public void Battle_log_is_routed_to_the_upload_queue()
    {
        // Consent is unknown by default -> upload not allowed -> the queue skips with that reason.
        // This proves OnBattleLogged actually reaches the queue (no network needed).
        var log = new DpsLog { Report = new DpsReport() };

        _services.Calculator.OnBattleLogged!(log);

        StatsUploadStatus status = _services.UploadQueue.Status();
        Assert.Equal(1, status.Skipped);
        Assert.Equal("consent_not_allowed", status.LastReason);
        Assert.Equal(0, status.Uploaded);
    }

    [Fact]
    public void Build_capture_config_uses_settings_defaults()
    {
        CaptureConfig config = _services.BuildCaptureConfig();
        Assert.Equal("206.127.156.0/24", config.ServerIp);
        Assert.Equal("13328", config.ServerPort);
    }

    [Fact]
    public void Feed_logs_each_segment_as_a_capture_line_with_the_right_fields()
    {
        // Proves the MeterServices -> DebugLogger.Capture wiring passes the segment's ip/seq/data/at
        // (not just that the logger formats correctly). Inject a logger pointed at a temp dir so the
        // test never touches the real %APPDATA% packet-debug-logs.
        string logDir = Path.Combine(_temp, "pdl");
        var logger = new PacketDebugLogger(logDir);
        using var services = new ServicesScope(new MeterServices(new PropertyHandler(_temp), debugLogger: logger));

        logger.Start();
        services.Value.Feed(new CapturedSegment(42, new byte[] { 0x0A, 0x0B }, 12345, "9.9.9.9", 1, "8.8.8.8", 2));
        logger.Stop();

        string file = Directory.GetFiles(logDir, "*.jsonl").Single();
        string[] lines = File.ReadAllLines(file, Encoding.UTF8).Where(l => l.Length > 0).ToArray();
        Assert.Contains(
            "{\"type\":\"capture\",\"at\":12345,\"ip\":\"9.9.9.9\",\"seq\":42,\"len\":2,\"head\":\"0A 0B\",\"data\":\"Cgs=\"}",
            lines);
    }

    [Fact]
    public void Dual_capture_of_the_game_stream_is_counted_once()
    {
        // The VPN dual-capture shape: the SAME game packet (opcode 0x3804) arrives under two different
        // 4-tuples (distinct src ports). The second connection is a duplicate of the game stream and must
        // be suppressed — otherwise every damage event would be dispatched (and SaveDamage'd) twice → ~2x.
        string logDir = Path.Combine(_temp, "pdl_dual");
        var logger = new PacketDebugLogger(logDir);
        using var services = new ServicesScope(new MeterServices(new PropertyHandler(_temp), debugLogger: logger));

        logger.Start();
        services.Value.Feed(GameSegment(seq: 0, at: 1000, srcPort: 5000));
        services.Value.Feed(GameSegment(seq: 0, at: 1000, srcPort: 5001));
        logger.Stop();

        string[] lines = ReadLog(logDir);
        Assert.Equal(1, lines.Count(l => l.Contains("\"type\":\"dispatch\"")));               // counted once
        Assert.Equal(1, lines.Count(l => l.Contains("\"type\":\"dup_game_stream_dropped\""))); // breadcrumb fired
    }

    [Fact]
    public void Single_game_stream_is_never_suppressed()
    {
        // Non-VPN users have ONE game connection: both packets on it dispatch, nothing is dropped.
        string logDir = Path.Combine(_temp, "pdl_single");
        var logger = new PacketDebugLogger(logDir);
        using var services = new ServicesScope(new MeterServices(new PropertyHandler(_temp), debugLogger: logger));

        logger.Start();
        services.Value.Feed(GameSegment(seq: 0, at: 1000, srcPort: 5000));
        services.Value.Feed(GameSegment(seq: 5, at: 1001, srcPort: 5000)); // same 4-tuple, next contiguous seq
        logger.Stop();

        string[] lines = ReadLog(logDir);
        Assert.Equal(2, lines.Count(l => l.Contains("\"type\":\"dispatch\"")));
        Assert.DoesNotContain(lines, l => l.Contains("dup_game_stream_dropped"));
    }

    [Fact]
    public void Quiet_primary_fails_over_to_the_surviving_game_stream()
    {
        // Real reconnect / proxy port change: the primary goes quiet, so after the handover window the
        // other game stream takes over and IS dispatched (no permanent blackout).
        string logDir = Path.Combine(_temp, "pdl_failover");
        var logger = new PacketDebugLogger(logDir);
        using var services = new ServicesScope(new MeterServices(new PropertyHandler(_temp), debugLogger: logger));

        logger.Start();
        services.Value.Feed(GameSegment(seq: 0, at: 1000, srcPort: 5000));        // A claims primary → dispatched
        services.Value.Feed(GameSegment(seq: 0, at: 1000, srcPort: 5001));        // B concurrent → suppressed
        services.Value.Feed(GameSegment(seq: 5, at: 7001, srcPort: 5001));        // > handover later → B takes over
        logger.Stop();

        string[] lines = ReadLog(logDir);
        Assert.Equal(2, lines.Count(l => l.Contains("\"type\":\"dispatch\""))); // A's first + B's failover packet
    }

    [Fact]
    public void Dedup_is_disabled_when_the_property_is_off()
    {
        // Escape hatch: capture.dedupeGameStreams=false restores the old (double-counting) behavior.
        var props = new PropertyHandler(_temp);
        props.SetProperty("capture.dedupeGameStreams", "false");
        string logDir = Path.Combine(_temp, "pdl_off");
        var logger = new PacketDebugLogger(logDir);
        using var services = new ServicesScope(new MeterServices(props, debugLogger: logger));

        logger.Start();
        services.Value.Feed(GameSegment(seq: 0, at: 1000, srcPort: 5000));
        services.Value.Feed(GameSegment(seq: 0, at: 1000, srcPort: 5001));
        logger.Stop();

        string[] lines = ReadLog(logDir);
        Assert.Equal(2, lines.Count(l => l.Contains("\"type\":\"dispatch\""))); // both dispatched (lock off)
        Assert.DoesNotContain(lines, l => l.Contains("dup_game_stream_dropped"));
    }

    // A length-prefixed frame whose opcode is 0x3804 (Damage) — passes StreamProcessor.LooksLikeGamePacket
    // and dispatches as a game packet. realLength = varint.value(8) + varint.length(1) - 4 = 5 = frame size.
    private static CapturedSegment GameSegment(long seq, long at, int srcPort) =>
        new(seq, new byte[] { 0x08, 0x04, 0x38, 0x00, 0x00 }, at, "10.0.0.1", srcPort, "10.0.0.2", 13328);

    private static string[] ReadLog(string logDir) =>
        File.ReadAllLines(Directory.GetFiles(logDir, "*.jsonl").Single(), Encoding.UTF8)
            .Where(l => l.Length > 0)
            .ToArray();

    // Disposes the extra MeterServices' upload-queue thread without leaking it past the test.
    private sealed class ServicesScope(MeterServices value) : IDisposable
    {
        public MeterServices Value { get; } = value;

        public void Dispose() => Value.UploadQueue.Dispose();
    }
}

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

    // Disposes the extra MeterServices' upload-queue thread without leaking it past the test.
    private sealed class ServicesScope(MeterServices value) : IDisposable
    {
        public MeterServices Value { get; } = value;

        public void Dispose() => Value.UploadQueue.Dispose();
    }
}

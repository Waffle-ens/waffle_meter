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
        Assert.Equal("1.6.9-dev", _services.Version);        // default when no version property

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
}

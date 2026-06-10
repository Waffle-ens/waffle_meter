using WaffleMeter.Capture.Live;
using Xunit;

namespace WaffleMeter.Capture.Live.Tests;

public sealed class CaptureConfigTests
{
    [Fact]
    public void FromProperties_uses_defaults_when_unset()
    {
        CaptureConfig config = CaptureConfig.FromProperties(_ => null);

        Assert.Equal("206.127.156.0/24", config.ServerIp);
        Assert.Equal("13328", config.ServerPort);
        Assert.Equal(10, config.TimeoutMs);
        Assert.Equal(65536, config.SnapshotSize);
    }

    [Fact]
    public void FromProperties_reads_overrides()
    {
        var values = new Dictionary<string, string?>
        {
            ["server.ip"] = "10.0.0.0/8",
            ["server.port"] = "9999",
            ["server.timeout"] = "25",
            ["server.maxSnapshotSize"] = "32768",
        };

        CaptureConfig config = CaptureConfig.FromProperties(k => values.GetValueOrDefault(k));

        Assert.Equal("10.0.0.0/8", config.ServerIp);
        Assert.Equal("9999", config.ServerPort);
        Assert.Equal(25, config.TimeoutMs);
        Assert.Equal(32768, config.SnapshotSize);
    }
}

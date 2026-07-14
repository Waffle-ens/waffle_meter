using WaffleMeter.App.Core;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

/// <summary>The packet-log replay wipes live battle state and bypasses the stats upload — it must never reach
/// a shipped build. The tray entry is gated on this predicate alone.</summary>
public sealed class DevPacketLogReplayTests
{
    [Theory]
    [InlineData("2.6.4-dev")]
    [InlineData("2.6.4-DEV")]
    [InlineData("3.0.0-dev.1")]
    public void Dev_versions_expose_the_replay(string version) =>
        Assert.True(DevPacketLogReplay.IsAvailable(version));

    [Theory]
    [InlineData("2.6.4")]
    [InlineData("2.6.4-rc1")]
    [InlineData("2.6.4-beta")]
    [InlineData("")]
    public void Shipped_versions_do_not(string version) =>
        Assert.False(DevPacketLogReplay.IsAvailable(version));
}

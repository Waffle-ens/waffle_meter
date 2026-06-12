using WaffleMeter.App.Core;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

public sealed class CaptureHostLauncherTests
{
    [Fact]
    public void Resolve_host_path_is_next_to_the_app()
    {
        string path = CaptureHostLauncher.ResolveHostPath();
        Assert.EndsWith(CaptureHostLauncher.HostExeName, path);
        Assert.StartsWith(AppContext.BaseDirectory, path);
    }

    [Fact]
    public void Launch_returns_not_found_for_a_missing_exe()
    {
        // A random pipe name guarantees IsRunning() is false, so we exercise the missing-exe path
        // without ever triggering a UAC prompt or the pipe-wait (NotFound returns before any launch).
        string randomPipe = "wm_nohost_" + Guid.NewGuid().ToString("N");
        string missing = Path.Combine(Path.GetTempPath(), "wm_missing_" + Guid.NewGuid().ToString("N") + ".exe");

        Assert.Equal(CaptureHostLaunch.NotFound, CaptureHostLauncher.EnsureServing(missing, randomPipe));
        Assert.Equal(CaptureHostLaunch.NotFound, CaptureHostLauncher.EnsureRunning(missing, randomPipe));
    }

    [Fact]
    public void Is_running_is_false_for_an_unknown_pipe()
    {
        Assert.False(CaptureHostLauncher.IsRunning("wm_unknown_" + Guid.NewGuid().ToString("N")));
    }
}

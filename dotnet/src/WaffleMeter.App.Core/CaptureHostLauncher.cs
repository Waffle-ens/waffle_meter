using System.ComponentModel;
using System.Diagnostics;
using WaffleMeter.Capture.Live;

namespace WaffleMeter.App.Core;

public enum CaptureHostLaunch
{
    AlreadyRunning,
    Launched,
    Declined,
    NotFound,
    Failed,
}

/// <summary>
/// Spawns the elevated <c>WaffleMeter.CaptureHost</c> from the unelevated UI. The host's
/// app.manifest triggers the UAC prompt; once it is up, the pipe client connects. Isolating
/// elevation here is the chosen architecture (the UI process never needs admin).
/// </summary>
public static class CaptureHostLauncher
{
    public const string HostExeName = "WaffleMeter.CaptureHost.exe";

    public static string ResolveHostPath() => Path.Combine(AppContext.BaseDirectory, HostExeName);

    /// <summary>
    /// Non-intrusive check: is a host serving the pipe? Enumerates <c>\\.\pipe\</c> rather than
    /// connecting, so it doesn't occupy the host's single server instance.
    /// </summary>
    public static bool IsRunning(string pipeName = CaptureWireProtocol.DefaultPipeName)
    {
        try
        {
            return Directory.GetFiles(@"\\.\pipe\")
                .Any(p => string.Equals(Path.GetFileName(p), pipeName, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    public static CaptureHostLaunch EnsureRunning(string? hostPath = null, string pipeName = CaptureWireProtocol.DefaultPipeName)
    {
        if (IsRunning(pipeName))
        {
            return CaptureHostLaunch.AlreadyRunning;
        }

        string path = hostPath ?? ResolveHostPath();
        if (!File.Exists(path))
        {
            return CaptureHostLaunch.NotFound;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true, // required for the "runas" elevation verb
                Verb = "runas",
            });
            return CaptureHostLaunch.Launched;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return CaptureHostLaunch.Declined; // ERROR_CANCELLED: user dismissed the UAC prompt
        }
        catch
        {
            return CaptureHostLaunch.Failed;
        }
    }
}

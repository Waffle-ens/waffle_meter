using System.ComponentModel;
using System.Diagnostics;
using System.Text;
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
/// Spawns the elevated <c>WaffleMeter.CaptureHost</c> from the unelevated UI.
///
/// Launch model: for an INSTALLED build (under %LocalAppData%, i.e. Velopack), the helper is launched
/// via a per-user on-demand Scheduled Task registered to run with the highest available privileges. The
/// task is registered ONCE (a single UAC prompt the first time); every subsequent launch triggers it with
/// NO prompt. Velopack keeps the app in a stable <c>...\current\</c> folder, so the task's command path
/// stays valid across updates (re-registered only if the path actually changes). The task runs as the
/// SAME user (HighestAvailable, InteractiveToken) so the elevated->medium named-pipe handshake (same SID
/// on the default DACL) still works. For a dev build (run from the repo bin), or if the task path fails,
/// it falls back to the original ShellExecute "runas" (UAC prompt each launch).
/// </summary>
public static class CaptureHostLauncher
{
    public const string HostExeName = "WaffleMeter.CaptureHost.exe";
    private const string TaskName = "waffle_meter Capture Helper";

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

        // Installed build -> prefer the no-prompt scheduled task; fall back to runas on any failure.
        if (IsInstalledPath(path))
        {
            CaptureHostLaunch viaTask = LaunchViaScheduledTask(path);
            if (viaTask is CaptureHostLaunch.Launched or CaptureHostLaunch.Declined)
            {
                return viaTask;
            }
            // viaTask == Failed -> fall through to the runas fallback below.
        }

        return LaunchViaRunas(path);
    }

    /// <summary>An installed (Velopack) build lives under %LocalAppData%; dev builds run from the repo bin.
    /// Only installed builds register a scheduled task (dev keeps the simple runas path).</summary>
    private static bool IsInstalledPath(string path)
    {
        try
        {
            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return !string.IsNullOrEmpty(localApp)
                && path.StartsWith(localApp, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Best-effort removal of the capture-helper scheduled task — called from the Velopack
    /// uninstall hook so an uninstall leaves nothing behind. If deletion needs rights the (per-user,
    /// unelevated) uninstall lacks, the leftover task is inert anyway (on-demand only, command path gone),
    /// so the failure is swallowed.</summary>
    public static void RemoveScheduledTask()
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks.exe", $"/delete /tn \"{TaskName}\" /f")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using Process? p = Process.Start(psi);
            p?.WaitForExit(10000);
        }
        catch
        {
            // best effort
        }
    }

    // ---- runas (fallback / dev): UAC prompt each launch ----
    private static CaptureHostLaunch LaunchViaRunas(string path)
    {
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

    // ---- scheduled task (installed): register once (1 UAC), then prompt-free ----
    private static CaptureHostLaunch LaunchViaScheduledTask(string path)
    {
        try
        {
            if (!TaskRegisteredForPath(path))
            {
                CaptureHostLaunch reg = RegisterTask(path);
                if (reg != CaptureHostLaunch.Launched)
                {
                    return reg; // Declined (user cancelled UAC) bubbles up; Failed -> runas fallback
                }
            }

            return RunTask() ? CaptureHostLaunch.Launched : CaptureHostLaunch.Failed;
        }
        catch
        {
            return CaptureHostLaunch.Failed;
        }
    }

    /// <summary>Does the task exist AND point at the current host exe? (Query needs no elevation.)</summary>
    private static bool TaskRegisteredForPath(string path)
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks.exe", $"/query /tn \"{TaskName}\" /xml ONE")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using Process? p = Process.Start(psi);
            if (p == null)
            {
                return false;
            }

            string xml = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            return p.ExitCode == 0 && xml.Contains(path, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Register the on-demand HighestAvailable task from an XML definition. Creating a HIGHEST
    /// task requires admin, so schtasks is launched elevated (one UAC prompt) via runas.</summary>
    private static CaptureHostLaunch RegisterTask(string path)
    {
        string xmlPath = Path.Combine(Path.GetTempPath(), "waffle_meter_capturehost_task.xml");
        try
        {
            File.WriteAllText(xmlPath, BuildTaskXml(path), new UnicodeEncoding(false, true)); // UTF-16 LE + BOM
        }
        catch
        {
            return CaptureHostLaunch.Failed;
        }

        try
        {
            var psi = new ProcessStartInfo("schtasks.exe", $"/create /tn \"{TaskName}\" /xml \"{xmlPath}\" /f")
            {
                UseShellExecute = true, // runas needs ShellExecute
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            using Process? p = Process.Start(psi);
            if (p == null)
            {
                return CaptureHostLaunch.Failed;
            }

            p.WaitForExit(15000);
            return p.ExitCode == 0 ? CaptureHostLaunch.Launched : CaptureHostLaunch.Failed;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return CaptureHostLaunch.Declined; // user dismissed the (one-time) UAC prompt
        }
        catch
        {
            return CaptureHostLaunch.Failed;
        }
    }

    /// <summary>Trigger the registered task on demand — runs elevated with NO prompt (no elevation needed
    /// to start an existing task; the task definition carries the privilege level).</summary>
    private static bool RunTask()
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks.exe", $"/run /tn \"{TaskName}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using Process? p = Process.Start(psi);
            if (p == null)
            {
                return false;
            }

            p.WaitForExit(10000);
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>A trigger-less, on-demand task that runs the helper as the current user with the highest
    /// available privileges (elevated for an admin, no prompt once registered). No ExecutionTimeLimit so a
    /// long capture session is never killed by the scheduler.</summary>
    private static string BuildTaskXml(string hostPath)
    {
        string escaped = hostPath
            .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-16\"?>");
        sb.Append("<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">");
        sb.Append("<RegistrationInfo><Description>waffle_meter elevated packet-capture helper (on-demand).</Description></RegistrationInfo>");
        sb.Append("<Principals><Principal id=\"Author\"><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>");
        sb.Append("<Settings>");
        sb.Append("<MultipleInstancesPolicy>Parallel</MultipleInstancesPolicy>");
        sb.Append("<DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>");
        sb.Append("<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>");
        sb.Append("<AllowHardTerminate>true</AllowHardTerminate>");
        sb.Append("<StartWhenAvailable>false</StartWhenAvailable>");
        sb.Append("<RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>");
        sb.Append("<AllowStartOnDemand>true</AllowStartOnDemand>");
        sb.Append("<Enabled>true</Enabled>");
        sb.Append("<Hidden>false</Hidden>");
        sb.Append("<ExecutionTimeLimit>PT0S</ExecutionTimeLimit>");
        sb.Append("</Settings>");
        sb.Append($"<Actions Context=\"Author\"><Exec><Command>{escaped}</Command></Exec></Actions>");
        sb.Append("</Task>");
        return sb.ToString();
    }
}

using System;
using System.Threading;
using Velopack;
using WaffleMeter.App.Core;

namespace WaffleMeter.App.Wpf;

/// <summary>
/// Explicit entry point so Velopack's install/update/uninstall hooks run at the very start of the
/// process — before WPF's <see cref="System.Windows.Application"/> is even constructed. Velopack warns
/// when <c>VelopackApp.Run()</c> is not in a real Main(); for hook invocations (e.g. --veloapp-install)
/// it handles the hook and exits, so no WPF startup happens at all on those runs.
/// </summary>
public static class Program
{
    // Per-session single-instance guard, held for the whole process lifetime (static so the GC can't
    // finalize it out from under us). Names are per-logon-session (Local\) — one instance per interactive
    // user, matching the per-user (LocalAppData) install.
    private static Mutex? _singleInstance;
    private const string MutexName = @"Local\waffle_meter_singleinstance";
    internal const string ShowEventName = @"Local\waffle_meter_show";

    [STAThread]
    public static void Main(string[] args)
    {
        // MUST be first. Handles Velopack lifecycle hooks and exits for those runs. On the first launch
        // after install, supersede the legacy Kotlin jpackage MSI; on uninstall, remove the capture-helper
        // scheduled task so nothing is left behind.
        VelopackApp.Build()
            .OnFirstRun(_ => LegacyMsiCleanup.Run())
            .OnBeforeUninstallFastCallback(_ => CaptureHostLauncher.RemoveScheduledTask())
            .Run();

        // Single-instance guard. A SECOND launch (e.g. the user relaunches from the shortcut while the
        // first instance is hidden in the tray) must NOT spawn a competing UI: that second UI's capture
        // client would see the pipe NAME already present, skip launching a fresh helper, and then block on
        // the busy single-instance helper pipe for the full connect timeout before failing with "could not
        // connect to the capture helper pipe". Instead, signal the running instance to surface (un-hide from
        // tray) and exit. Velopack's ApplyUpdatesAndRestart waits for the old process to fully exit before
        // relaunching, so the mutex is free on an update-restart — this never interferes with updates.
        _singleInstance = new Mutex(initiallyOwned: true, MutexName, out bool isFirstInstance);
        if (!isFirstInstance)
        {
            SignalExistingInstance();
            return;
        }

        var app = new App
        {
            // Created by the FIRST instance; later launches open this by name and Set() it to ask us back.
            SingleInstanceShowSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName),
        };
        app.InitializeComponent();
        app.Run();

        GC.KeepAlive(_singleInstance);
    }

    /// <summary>Best-effort: tell the already-running instance to surface. Even if signaling fails, the
    /// important part is that this second instance exits instead of spawning a colliding UI.</summary>
    private static void SignalExistingInstance()
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(ShowEventName, out EventWaitHandle? show))
            {
                show.Set();
                show.Dispose();
            }
        }
        catch
        {
            // best effort
        }
    }
}

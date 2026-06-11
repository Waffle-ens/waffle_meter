using System;
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

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}

using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace WaffleMeter.App.Wpf;

/// <summary>
/// First-run supersede of the old Kotlin jpackage MSI. Finds any product registered under the legacy
/// per-user UpgradeCode and uninstalls it quietly, so the Velopack install replaces (not duplicates)
/// it — the v1.6→v1.7.2 double-install bug must not recur. Best-effort + idempotent: re-running until
/// the old product is gone is safe. Runs via VelopackApp.WithFirstRun.
/// </summary>
internal static class LegacyMsiCleanup
{
    private const string UpgradeCode = "{66D7E440-C8DB-47D8-A7AC-996796404049}";
    private const int ErrorSuccess = 0;

    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    private static extern int MsiEnumRelatedProducts(string upgradeCode, int reserved, int index, StringBuilder productCode);

    public static void Run()
    {
        try
        {
            var codes = new List<string>();
            var product = new StringBuilder(39);
            int index = 0;
            while (MsiEnumRelatedProducts(UpgradeCode, 0, index++, product) == ErrorSuccess)
            {
                codes.Add(product.ToString());
                product.Clear();
                product.EnsureCapacity(39);
            }

            foreach (string code in codes)
            {
                Log($"legacy MSI {code} found — uninstalling");
                // Per-user MSI -> /qn uninstalls without a UAC prompt.
                using Process? p = Process.Start(new ProcessStartInfo("msiexec.exe", $"/x {code} /qn /norestart")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                p?.WaitForExit(60_000);
                Log($"msiexec /x {code} -> exit {(p?.HasExited == true ? p.ExitCode : -2)}"); // -2 = timed out
            }
        }
        catch (Exception ex)
        {
            Log($"cleanup skipped: {ex.GetType().Name}: {ex.Message}");
            // best-effort; retried on the next launch if it didn't complete
        }
    }

    // Diagnostic trail for the one-time transition (helps verify the supersede ran on a live machine).
    private static void Log(string message)
    {
        try
        {
            File.AppendAllText(
                Path.Combine(AppContext.BaseDirectory, "msi-migration.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}\n");
        }
        catch
        {
            // best effort
        }
    }
}

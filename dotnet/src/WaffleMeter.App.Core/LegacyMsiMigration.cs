using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace WaffleMeter.App.Core;

/// <summary>
/// One-time supersede of the legacy jpackage MSI (the Kotlin v1.x build) when the new Velopack build
/// runs, so the two never coexist as duplicate "Apps &amp; features" entries. Finds the old product by its
/// stable Windows Installer <c>UpgradeCode</c> (constant across the Kotlin app's versions) and silently
/// uninstalls it.
///
/// Idempotent + self-gating: once the MSI is gone, <see cref="MsiEnumRelatedProductsW"/> returns nothing
/// and this is a cheap no-op, so it is safe to run on every launch. A per-user MSI (jpackage's default)
/// uninstalls without elevation; a per-machine install would need admin — that case is reported via the
/// log, not force-elevated (no surprise UAC on launch). The CALLER must only invoke this for an INSTALLED
/// (Velopack) build, never a dev run, or it would uninstall the developer's own legacy install.
/// </summary>
public static class LegacyMsiMigration
{
    // jpackage bundle UpgradeCode (stable across the Kotlin app's versions; from the migration plan).
    private const string UpgradeCode = "{66D7E440-C8DB-47D8-A7AC-996796404049}";
    private const uint ErrorSuccess = 0;

    /// <summary>Uninstall every product registered under the legacy UpgradeCode. No-op when none exist.</summary>
    public static void RemoveIfPresent(Action<string>? log = null)
    {
        try
        {
            List<string> codes = RelatedProductCodes();
            if (codes.Count == 0)
            {
                return;
            }

            foreach (string productCode in codes)
            {
                log?.Invoke($"legacy MSI {productCode} found — uninstalling");
                int exit = Uninstall(productCode);
                // 0 = removed, 1605 = already gone, 1602 = user cancelled, 1603/5/1925 = needs elevation.
                log?.Invoke($"msiexec /x {productCode} -> exit {exit}");
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"legacy MSI cleanup skipped: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static List<string> RelatedProductCodes()
    {
        var result = new List<string>();
        for (uint i = 0; ; i++)
        {
            var buf = new StringBuilder(40); // GUID "{...}" = 38 chars + null
            uint rc = MsiEnumRelatedProductsW(UpgradeCode, 0, i, buf);
            if (rc != ErrorSuccess)
            {
                break; // ERROR_NO_MORE_ITEMS (259) or any error -> stop enumerating
            }

            result.Add(buf.ToString());
        }

        return result;
    }

    private static int Uninstall(string productCode)
    {
        var psi = new ProcessStartInfo("msiexec.exe", $"/x {productCode} /qn /norestart")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using Process? p = Process.Start(psi);
        if (p == null)
        {
            return -1;
        }

        return p.WaitForExit(120_000) ? p.ExitCode : -2; // -2 = timed out
    }

    [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint MsiEnumRelatedProductsW(string lpUpgradeCode, uint dwReserved, uint iProductIndex, StringBuilder lpProductBuf);
}

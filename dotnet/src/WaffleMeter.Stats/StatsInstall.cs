using WaffleMeter.Services;

namespace WaffleMeter.Stats;

/// <summary>
/// Verbatim port of Kotlin <c>stats.StatsInstall</c>: a stable per-install UUID persisted in
/// settings (generated on first use). Sent as the <c>x-install-id</c> header.
/// </summary>
public static class StatsInstall
{
    private const string KeyInstallId = "statsInstallId";

    public static string InstallId(PropertyHandler properties)
    {
        string? saved = properties.GetProperty(KeyInstallId);
        if (!string.IsNullOrWhiteSpace(saved))
        {
            return saved!;
        }

        string generated = Guid.NewGuid().ToString();
        properties.SetProperty(KeyInstallId, generated);
        return generated;
    }
}

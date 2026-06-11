using System.IO;
using System.Windows.Media.Imaging;

namespace WaffleMeter.App.Wpf;

/// <summary>
/// Resolves bundled icons for the join panel. Job icons (filename = Korean class name) ship as WPF
/// resources under JoinIcons/; resolved by pack URI and cached. Returns null for an unknown/missing
/// job (the panel falls back to no icon), matching the web getJobIconSrc behavior.
/// (Skill icons — the 332-file set — arrive with the skill-enrichment pass.)
/// </summary>
public static class JoinIcons
{
    private static readonly Dictionary<string, BitmapImage?> JobCache = new();

    private static BitmapImage? _bossIcon;
    private static bool _bossLoaded;

    /// <summary>The boss icon used by the battle-history panel (bundled from src/assets/bossIcon.png).</summary>
    public static BitmapImage? BossIcon
    {
        get
        {
            if (!_bossLoaded)
            {
                _bossIcon = TryLoad("pack://application:,,,/JobIcons/bossIcon.png");
                _bossLoaded = true;
            }

            return _bossIcon;
        }
    }

    public static BitmapImage? Job(string? job)
    {
        if (string.IsNullOrEmpty(job))
        {
            return null;
        }

        if (JobCache.TryGetValue(job, out BitmapImage? cached))
        {
            return cached;
        }

        BitmapImage? image = TryLoad($"pack://application:,,,/JobIcons/{job}.png");
        JobCache[job] = image;
        return image;
    }

    private static BitmapImage? TryLoad(string packUri)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(packUri, UriKind.Absolute);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (IOException)
        {
            return null; // resource not bundled (e.g. unknown class name)
        }
        catch (Exception)
        {
            return null;
        }
    }
}

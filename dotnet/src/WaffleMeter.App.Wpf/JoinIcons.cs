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
    private static readonly Dictionary<int, BitmapImage?> SkillCache = new();

    private static BitmapImage? _bossIcon;
    private static bool _bossLoaded;

    /// <summary>The boss icon used by the battle-history panel (bundled from src/assets/bossIcon.png).</summary>
    public static BitmapImage? BossIcon
    {
        get
        {
            if (!_bossLoaded)
            {
                _bossIcon = TryLoad("pack://application:,,,/WaffleMeter.App.Wpf;component/JobIcons/bossIcon.png");
                _bossLoaded = true;
            }

            return _bossIcon;
        }
    }

    /// <summary>Skill icon for a code (port of getSkillIconSrc): exact manifest hit, else the floor base
    /// for a skill/buff code, else null. Cached.</summary>
    public static BitmapImage? Skill(int code)
    {
        if (code <= 0)
        {
            return null;
        }

        if (SkillCache.TryGetValue(code, out BitmapImage? cached))
        {
            return cached;
        }

        int? resolved = null;
        if (SkillIconManifest.Codes.Contains(code))
        {
            resolved = code;
        }
        else
        {
            int? baseCode = code switch
            {
                >= 11_000_000 and <= 19_999_999 => code / 10_000 * 10_000,        // skill code
                >= 110_000_000 and <= 199_999_999 => code / 100_000 * 10_000,     // buff code (incl. 권성 19x)
                _ => null,
            };
            if (baseCode is int b && SkillIconManifest.Codes.Contains(b))
            {
                resolved = b;
            }
        }

        BitmapImage? image = resolved is int r
            ? TryLoad($"pack://application:,,,/WaffleMeter.App.Wpf;component/SkillIcons/{r}.png")
            : null;
        SkillCache[code] = image;
        return image;
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

        BitmapImage? image = TryLoad($"pack://application:,,,/WaffleMeter.App.Wpf;component/JobIcons/{job}.png");
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

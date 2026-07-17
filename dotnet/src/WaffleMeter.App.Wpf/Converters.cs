using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;

namespace WaffleMeter.App.Wpf;

/// <summary>double ratio -&gt; star GridLength, so two columns split a row into fill/rest by ratio.</summary>
public sealed class RatioToStarConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        double ratio = value is double d ? d : 0.0;
        return new GridLength(Math.Max(ratio, 0.0), GridUnitType.Star);
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>value == parameter (string) -&gt; Visible, else Collapsed. Drives the settings nav rail:
/// each section panel is shown only when the selected nav key matches.</summary>
public sealed class StringEqualsToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value as string, parameter as string, StringComparison.Ordinal) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>true -&gt; Collapsed, false -&gt; Visible (for "shown when false" hints).</summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Font-family name -&gt; <see cref="FontFamily"/> that prefers a BUNDLED font (Fonts/*.ttf embedded as
/// Resource) by its internal family name, then the same name as an installed system font, then safe
/// Korean-capable fallbacks. So the chosen font renders once its file is dropped into Fonts/, and
/// degrades gracefully (Malgun Gothic) until then.
/// </summary>
public sealed class FontFamilyConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) =>
        FontResolver.Resolve(value as string ?? "Malgun Gothic");

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Resolves a font-family name to a <see cref="FontFamily"/>: a BUNDLED font (Fonts/*.ttf embedded as
/// Resource) by its internal family name first, else the same name as an installed system font with safe
/// Korean-capable fallbacks (Malgun Gothic, Segoe UI). Shared by <see cref="FontFamilyConverter"/> (the
/// app-wide meter font) and <see cref="GlyphFallback"/> (the per-nickname glyph check).
/// </summary>
public static class FontResolver
{
    public static FontFamily Resolve(string name)
    {
        try
        {
            // Embedded (Fonts/*.ttf as Resource) first — but only if it actually resolved to a
            // typeface, so an unbundled name cleanly falls through to a system font instead of a blank.
            // Assembly-qualified location so the bundled font resolves regardless of the entry assembly
            // (the bare pack URI resolves against the host exe, which breaks UiPreview + any other host).
            var bundled = new FontFamily(new Uri("pack://application:,,,/"), $"/WaffleMeter.App.Wpf;component/Fonts/#{name}");
            if (bundled.GetTypefaces().Count > 0)
            {
                return bundled;
            }
        }
        catch
        {
            // fall through
        }

        try
        {
            // User-added fonts (a .ttf/.otf dropped into the fonts folder via 설정 › 커스텀 폰트 추가): loaded
            // straight from disk by their internal family name — no system install, no restart.
            if (Directory.Exists(UserFontsDir()))
            {
                var user = new FontFamily(UserFontsBaseUri(), $"./#{name}");
                if (user.GetTypefaces().Count > 0)
                {
                    return user;
                }
            }
        }
        catch
        {
            // fall through to system lookup
        }

        return new FontFamily($"{name}, Malgun Gothic, Segoe UI");
    }

    /// <summary>The folder user-added fonts are copied into (next to settings.properties).</summary>
    public static string UserFontsDir()
    {
        string appData = Environment.GetEnvironmentVariable("APPDATA")
                         ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(appData, "waffle_meter.v1.4", "fonts");
    }

    private static Uri UserFontsBaseUri()
    {
        string dir = UserFontsDir();
        return new Uri(dir.EndsWith(Path.DirectorySeparatorChar) ? dir : dir + Path.DirectorySeparatorChar);
    }

    /// <summary>Internal family names of every user-added font, for the settings font dropdown. Empty if none.</summary>
    public static IReadOnlyList<string> EnumerateUserFontFamilies()
    {
        var names = new List<string>();
        try
        {
            if (Directory.Exists(UserFontsDir()))
            {
                foreach (FontFamily fam in Fonts.GetFontFamilies(UserFontsBaseUri()))
                {
                    string? n = BestFamilyName(fam);
                    if (!string.IsNullOrWhiteSpace(n) && !names.Contains(n))
                    {
                        names.Add(n);
                    }
                }
            }
        }
        catch
        {
            // a bad file in the folder must never break the settings list
        }

        return names;
    }

    /// <summary>Copy a picked .ttf/.otf into the user fonts folder and return its primary family name (the value
    /// the settings store + <see cref="Resolve"/> match on), or null if the file can't be read as a font.</summary>
    public static string? InstallUserFont(string sourcePath)
    {
        try
        {
            string dir = UserFontsDir();
            Directory.CreateDirectory(dir);
            string dest = Path.Combine(dir, Path.GetFileName(sourcePath));
            File.Copy(sourcePath, dest, overwrite: true); // re-adding the same file is idempotent
            foreach (FontFamily fam in Fonts.GetFontFamilies(new Uri(dest))) // families in the copied file
            {
                string? n = BestFamilyName(fam);
                if (!string.IsNullOrWhiteSpace(n))
                {
                    return n;
                }
            }
        }
        catch
        {
            // unreadable / not a font / copy failed
        }

        return null;
    }

    private static string? BestFamilyName(FontFamily fam)
    {
        LanguageSpecificStringDictionary names = fam.FamilyNames;
        if (names.TryGetValue(XmlLanguage.GetLanguage("en-us"), out string? en) && !string.IsNullOrWhiteSpace(en))
        {
            return en;
        }

        foreach (string v in names.Values)
        {
            return v; // any localized family name resolves via WPF's "#name" match
        }

        return null;
    }
}

/// <summary>
/// Row height -&gt; font size, scaling like React MeterRow (sizes derive from rowHeight). Parameter is
/// "<c>mult:min</c>" (e.g. "0.4:10" primary, "0.32:9" secondary); result = max(min, floor(height*mult)).
/// </summary>
/// <summary>Adds a constant (ConverterParameter) to a numeric value — e.g. the boss/target bar height =
/// row height + a few px so it reads slightly thicker than the player rows.</summary>
public sealed class OffsetConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        double v = value switch { int i => i, double d => d, _ => 0.0 };
        double offset = 0;
        if (parameter is string p)
        {
            double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out offset);
        }

        return v + offset;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class RowHeightToFontSizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        double h = value switch { int i => i, double d => d, _ => 36.0 };
        double mult = 0.4, min = 10;
        if (parameter is string p)
        {
            string[] parts = p.Split(':');
            if (parts.Length == 2)
            {
                double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out mult);
                double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out min);
            }
        }

        return Math.Max(min, Math.Floor(h * mult));
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

using System.Globalization;
using System.Windows;
using System.Windows.Data;
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
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        string name = value as string ?? "Malgun Gothic";
        try
        {
            // Embedded (Fonts/*.ttf as Resource) first — but only if it actually resolved to a
            // typeface, so an unbundled name cleanly falls through to a system font instead of a blank.
            var bundled = new FontFamily(new Uri("pack://application:,,,/"), $"./Fonts/#{name}");
            if (bundled.GetTypefaces().Count > 0)
            {
                return bundled;
            }
        }
        catch
        {
            // fall through to system lookup
        }

        return new FontFamily($"{name}, Malgun Gothic, Segoe UI");
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Row height -&gt; font size, scaling like React MeterRow (sizes derive from rowHeight). Parameter is
/// "<c>mult:min</c>" (e.g. "0.4:10" primary, "0.32:9" secondary); result = max(min, floor(height*mult)).
/// </summary>
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

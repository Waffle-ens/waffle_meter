using System.Globalization;
using System.Windows;
using System.Windows.Data;

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

/// <summary>true -&gt; Collapsed, false -&gt; Visible (for "shown when false" hints).</summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

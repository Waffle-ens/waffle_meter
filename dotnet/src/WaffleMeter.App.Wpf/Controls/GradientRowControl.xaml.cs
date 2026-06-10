using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WaffleMeter.App.Core;

namespace WaffleMeter.App.Wpf.Controls;

/// <summary>A left→right gradient preview plus From/To <see cref="ColorSwatchButton"/>s, for editing a
/// two-stop meter-bar gradient (e.g. userBar). Two-way bindable <see cref="FromColor"/>/<see cref="ToColor"/>.</summary>
public partial class GradientRowControl : UserControl
{
    public GradientRowControl()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdatePreview();
    }

    public static readonly DependencyProperty FromColorProperty = DependencyProperty.Register(
        nameof(FromColor), typeof(string), typeof(GradientRowControl),
        new FrameworkPropertyMetadata("#000000", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnChanged));

    public static readonly DependencyProperty ToColorProperty = DependencyProperty.Register(
        nameof(ToColor), typeof(string), typeof(GradientRowControl),
        new FrameworkPropertyMetadata("#000000", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnChanged));

    public string FromColor
    {
        get => (string)GetValue(FromColorProperty);
        set => SetValue(FromColorProperty, value);
    }

    public string ToColor
    {
        get => (string)GetValue(ToColorProperty);
        set => SetValue(ToColorProperty, value);
    }

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((GradientRowControl)d).UpdatePreview();

    private void UpdatePreview() =>
        Preview.Background = new LinearGradientBrush(ToColor2(FromColor), ToColor2(ToColor), new Point(0, 0.5), new Point(1, 0.5));

    private static Color ToColor2(string value) =>
        ColorString.TryParse(value, out ColorRgba c) ? Color.FromArgb(c.A, c.R, c.G, c.B) : Colors.Black;
}

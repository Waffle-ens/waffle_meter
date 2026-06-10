using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WaffleMeter.App.Core;

namespace WaffleMeter.App.Wpf.Controls;

/// <summary>A 28×28 color swatch that opens a <see cref="ColorPickerControl"/> popup; two-way bindable
/// <see cref="Color"/> string (hex or rgba()).</summary>
public partial class ColorSwatchButton : UserControl
{
    public ColorSwatchButton()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateChip();
    }

    public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(
        nameof(Color), typeof(string), typeof(ColorSwatchButton),
        new FrameworkPropertyMetadata("#ffffff", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnColorChanged));

    public string Color
    {
        get => (string)GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    private static void OnColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((ColorSwatchButton)d).UpdateChip();

    private void UpdateChip()
    {
        Color color = ColorString.TryParse(Color, out ColorRgba c)
            ? System.Windows.Media.Color.FromArgb(c.A, c.R, c.G, c.B)
            : Colors.White;
        Chip.Background = new SolidColorBrush(color);
    }
}

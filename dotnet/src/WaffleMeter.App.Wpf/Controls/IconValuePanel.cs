using System.Windows;
using System.Windows.Controls;

namespace WaffleMeter.App.Wpf.Controls;

/// <summary>Lays out exactly two children — an icon and a value — for a small currency card. Side by side when
/// they fit (icon on the left, value pushed to the right edge so figures line up down a list); otherwise the icon
/// stacks centred above the value.
/// <para>Two triggers for stacking. <see cref="StackBelowProperty"/> is a width set per row of cards (inherited
/// from the ItemsControl) so every card in a row flips together at the same panel width instead of one card
/// stacking beside its side-by-side neighbour. The value's own measured width is the backstop: a wider user font
/// or an unusually long figure stacks that card even above the threshold rather than trimming it.</para>
/// <para>Stacked, the value is measured against the card's width, so its TextTrimming still engages if even a
/// line of its own is too narrow — it never paints past the card.</para></summary>
public sealed class IconValuePanel : Panel
{
    /// <summary>Gap between the icon and the value, sideways or stacked.</summary>
    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing), typeof(double), typeof(IconValuePanel),
        new FrameworkPropertyMetadata(5.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Stack whenever the card is narrower than this (device-independent px of the panel's own width).
    /// 0 = only when the value does not fit. Inherited, so it is set once on the ItemsControl of a row.</summary>
    public static readonly DependencyProperty StackBelowProperty = DependencyProperty.RegisterAttached(
        "StackBelow", typeof(double), typeof(IconValuePanel),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static double GetStackBelow(DependencyObject d) => (double)d.GetValue(StackBelowProperty);

    public static void SetStackBelow(DependencyObject d, double value) => d.SetValue(StackBelowProperty, value);

    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    private bool _stacked;

    protected override Size MeasureOverride(Size availableSize)
    {
        if (InternalChildren.Count != 2)
        {
            Size total = default;
            foreach (UIElement child in InternalChildren)
            {
                child.Measure(availableSize);
                total.Width = Math.Max(total.Width, child.DesiredSize.Width);
                total.Height = Math.Max(total.Height, child.DesiredSize.Height);
            }

            return total;
        }

        UIElement icon = InternalChildren[0];
        UIElement value = InternalChildren[1];
        var unbounded = new Size(double.PositiveInfinity, double.PositiveInfinity);
        icon.Measure(unbounded);
        value.Measure(unbounded);
        Size i = icon.DesiredSize;
        Size v = value.DesiredSize;
        double sideBySide = i.Width + Spacing + v.Width;
        bool bounded = !double.IsInfinity(availableSize.Width);
        _stacked = bounded && (availableSize.Width < GetStackBelow(this) || sideBySide > availableSize.Width);

        if (!_stacked)
        {
            return new Size(sideBySide, Math.Max(i.Height, v.Height));
        }

        value.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        v = value.DesiredSize;
        return new Size(Math.Max(i.Width, v.Width), i.Height + Spacing / 2 + v.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (InternalChildren.Count != 2)
        {
            foreach (UIElement child in InternalChildren)
            {
                child.Arrange(new Rect(finalSize));
            }

            return finalSize;
        }

        UIElement icon = InternalChildren[0];
        UIElement value = InternalChildren[1];
        Size i = icon.DesiredSize;
        Size v = value.DesiredSize;

        if (_stacked)
        {
            double valueWidth = Math.Min(finalSize.Width, v.Width);
            double top = Math.Max(0, (finalSize.Height - (i.Height + Spacing / 2 + v.Height)) / 2);
            icon.Arrange(new Rect((finalSize.Width - i.Width) / 2, top, i.Width, i.Height));
            value.Arrange(new Rect((finalSize.Width - valueWidth) / 2, top + i.Height + Spacing / 2, valueWidth, v.Height));
        }
        else
        {
            icon.Arrange(new Rect(0, (finalSize.Height - i.Height) / 2, i.Width, i.Height));
            value.Arrange(new Rect(finalSize.Width - v.Width, (finalSize.Height - v.Height) / 2, v.Width, v.Height));
        }

        return finalSize;
    }
}

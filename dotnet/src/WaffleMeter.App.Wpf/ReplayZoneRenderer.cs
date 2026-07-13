using System.Windows.Media;

namespace WaffleMeter.App.Wpf;

/// <summary>
/// The WPF skin for a boss mechanic. The rules that matter (when a zone is on screen, where it sits, what
/// its outline is) are UI-free and live in <c>WaffleMeter.Replay.ReplayZones</c> so the stats-web player
/// follows the same ones; this only picks the paint.
/// </summary>
public static class ReplayZoneRenderer
{
    private static readonly Brush TelegraphFill = Frozen(Color.FromArgb(0x38, 0xFF, 0xB0, 0x40));
    private static readonly Brush TelegraphStroke = Frozen(Color.FromArgb(0xC0, 0xFF, 0xC0, 0x60));
    private static readonly Brush ImpactFill = Frozen(Color.FromArgb(0x60, 0xFF, 0x50, 0x40));
    private static readonly Brush ImpactStroke = Frozen(Color.FromArgb(0xE0, 0xFF, 0x80, 0x60));

    /// <summary>Amber while the floor warns the raid, hot red as the mechanic lands.</summary>
    public static (Brush Fill, Brush Stroke, double Thickness) Paint(bool telegraphing)
        => telegraphing
            ? (TelegraphFill, TelegraphStroke, 1.5)
            : (ImpactFill, ImpactStroke, 2.5);

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}

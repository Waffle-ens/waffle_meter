using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace WaffleMeter.App.Wpf;

/// <summary>
/// The metallic sheen on the top two tiers' inner ring — the only moving pixel the tier feature adds.
/// <para>Two constraints shape this and rule out every obvious approach:</para>
/// <list type="bullet">
/// <item><description><c>App.xaml.cs</c> sets <c>RenderOptions.ProcessRenderMode = SoftwareOnly</c> by default
/// (vrrCompatMode) and the overlay is an <c>AllowsTransparency</c> layered window, so there is no GPU compositing
/// path. Blur/shadow effects would be CPU convolution over a running game.</description></item>
/// <item><description><c>OverlayViewModel.Update</c> replaces every row each tick (100~1000 ms), destroying and
/// rebuilding the DataTemplate tree. A Storyboard living inside the template restarts every tick and reads as
/// stutter — which is why the codebase contains zero Storyboard/animation usages.</description></item>
/// </list>
/// <para>So the animated brushes live OUTSIDE the rows, as shared unfrozen singletons, and a low-frequency timer
/// nudges their gradient origin. One property write repaints every badge of that tier on screen. This is the same
/// shape as the buff overlay's duration ring (a VM-built Geometry on a 500 ms DispatcherTimer), not a Storyboard.</para>
/// <para>When no such tier is on screen the timer is stopped, so idle CPU is exactly zero.</para>
/// </summary>
public static class TierSheen
{
    /// <summary>12 steps × 500 ms = a 6 s sweep. Slow enough to read as metal, not as a spinner.</summary>
    private const int Steps = 12;

    private static readonly DispatcherTimer Timer = new(DispatcherPriority.Background)
    {
        Interval = TimeSpan.FromMilliseconds(500),
    };

    // ★ The only unfrozen brushes in the app. Deliberately shared and mutated in place.
    private static readonly LinearGradientBrush ChallengerInner = Build("#FF7DD3FC", "#FFF0ABFC");
    private static readonly LinearGradientBrush MasterInner = Build("#FFE879F9", "#FFA855F7");

    private static int _phase;
    private static int _demand;

    static TierSheen() => Timer.Tick += (_, _) =>
    {
        _phase = (_phase + 1) % Steps;
        double t = _phase / (double)Steps;
        Shift(ChallengerInner, t);
        Shift(MasterInner, t);
    };

    /// <summary>The shared animated brush for a tier rank (1 = 챌린저, 2 = 마스터).</summary>
    public static Brush BrushFor(int rank) => rank == 1 ? ChallengerInner : MasterInner;

    /// <summary>
    /// Report how many animated-tier rows this frame drew. Zero (or effects disabled) stops the timer and resets
    /// the gradients, so a meter sitting idle in town costs nothing. Call it from the end of every row rebuild and
    /// whenever the overlay is hidden/parked.
    /// </summary>
    public static void SetDemand(int count, bool enabled)
    {
        _demand = enabled ? Math.Max(0, count) : 0;
        if (_demand > 0)
        {
            if (!Timer.IsEnabled)
            {
                Timer.Start();
            }

            return;
        }

        if (Timer.IsEnabled)
        {
            Timer.Stop();
            _phase = 0;
            Shift(ChallengerInner, 0);
            Shift(MasterInner, 0);
        }
    }

    /// <summary>Slide the gradient origin diagonally. Stops and colours never change — only the end points move,
    /// which invalidates ~400 px per badge and nothing else.</summary>
    private static void Shift(LinearGradientBrush brush, double t)
    {
        brush.StartPoint = new Point(t - 0.4, 0);
        brush.EndPoint = new Point(t + 0.6, 1);
    }

    private static LinearGradientBrush Build(string from, string to)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(-0.4, 0),
            EndPoint = new Point(0.6, 1),
        };
        brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(from)!, 0.0));
        brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(to)!, 1.0));
        return brush; // NOT frozen — Shift() mutates it.
    }
}

using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace WaffleMeter.App.Wpf;

/// <summary>
/// The moving part of the nickname effects and the ranker gauge skins.
/// <para><b>Why this is an animation and not a DispatcherTimer.</b> The first version nudged the gradient
/// origin from a 250 ms timer, the same shape <see cref="TierSheen"/> uses. That is fine for a ring sweeping
/// once every few seconds, but a highlight travelling along a nickname at 4 updates per second reads as a row
/// of steps, and raising the speed setting only makes the steps bigger.</para>
/// <para><b>Why this does not contradict "no Storyboards in this codebase".</b> That rule (see
/// <see cref="TierSheen"/>) is about animations living INSIDE a row DataTemplate: <c>OverlayViewModel.Update</c>
/// replaces every row each tick, so a template-owned Storyboard restarts every tick and stutters. These brushes
/// are process-wide singletons that no row owns, so the animation is started once and is never touched by a row
/// rebuild.</para>
/// <para><b>Cost control.</b> <c>Timeline.SetDesiredFrameRate</c> pins the clock to 30 fps; the animation only
/// exists while something on screen uses it; the meter being hidden or parked stops it outright
/// (<see cref="SetParked"/>); and low-spec mode refuses it (<see cref="SetLowSpec"/>). Every demand source
/// passes through the SAME gate in <see cref="Sync"/> — an earlier version gated only the row path, so the
/// settings preview could drive the meter's clock in low-spec mode.</para>
/// <para>⚠ <b>Units.</b> <c>Brush.Transform</c> applies in the painted shape's ABSOLUTE (DIP) space regardless
/// of <c>MappingMode</c>; only <c>Brush.RelativeTransform</c> works in the 0~1 box space. Mixing them is silent:
/// a relative-mapped brush translated 1.0 through <c>Transform</c> moves ONE PIXEL, not one tile, which is
/// exactly how the gauge skins shipped frozen in review. The period constants are named for their unit and each
/// track records which slot it animates so the two can never drift apart again.</para>
/// </summary>
public static class NameFxSheen
{
    /// <summary>Nickname tile width in DIP — absolute, so the band is the same physical size on every nickname.</summary>
    private const double NamePeriodPx = 90;

    /// <summary>
    /// Gauge tile as a FRACTION of the bar: one tile = one bar. An absolute tile looked right on the top row and
    /// wrong everywhere else — a bar at 16% is only ~65 px long, so it never got past the dark head of the
    /// gradient. Relative mapping gives every row the whole effect.
    /// </summary>
    private const double GaugePeriodRelative = 1.0;

    /// <summary>Seconds for one full tile at 100% speed. Slow enough to sit behind numbers you are reading.</summary>
    private const double NameSeconds = 2.5;

    private const double GaugeSeconds = 3.5;

    /// <summary>30 fps. 60 doubles the compositing work on an <c>AllowsTransparency</c> layered window for a
    /// difference that does not survive a moving game behind it.</summary>
    private const int FrameRate = 30;

    /// <param name="Brush">A <see cref="GradientBrush"/> for the ramp skins, a <see cref="DrawingBrush"/> for
    /// the shape ones. Both are translated the same way; only how they are REBUILT on a brightness change
    /// differs, which <see cref="Rebuild"/> handles.</param>
    private sealed record Track(
        Brush Brush,
        TranslateTransform Transform,
        double Period,
        double Seconds,
        bool Reverse);

    // ★ The only unfrozen brushes this feature owns. Shared, and animated in place.
    private static readonly Dictionary<(string Id, bool Light), Track> Tracks = Build();

    private static int _rowDemand;
    private static bool _previewDemand;
    private static bool _parked;
    private static bool _lowSpec;
    private static int _speedPercent = 100;
    private static bool _running;

    /// <summary>The shared live brush for an animated effect or gauge skin. Same instance every call.
    /// UI thread only — these are unfrozen <see cref="Freezable"/>s owned by the dispatcher that first touched
    /// them.</summary>
    public static Brush BrushFor(string id, bool isLight) => Tracks[(id, isLight)].Brush;

    /// <summary>Is the sweep actually running? Observable because "nothing moves" has several very different
    /// causes — no demand, parked, low-spec, or a demand source nobody wired up — and only the last is a bug.</summary>
    public static bool IsRunning => _running;

    /// <summary>
    /// Report how many animated rows this frame drew. Zero (or the feature off) releases the animation, so a
    /// meter sitting in town costs nothing.
    /// </summary>
    public static void SetDemand(int count, bool enabled, int speedPercent)
    {
        _rowDemand = enabled ? Math.Max(0, count) : 0;
        Sync(speedPercent);
    }

    /// <summary>
    /// The settings preview strip is a SECOND, independent demand source. Without it the strip sits frozen:
    /// grants come from the server, so a user deciding whether to keep the animation on has no decorated row on
    /// screen, row demand is zero, and the clock everything depends on is stopped.
    /// </summary>
    public static void SetPreviewDemand(bool on, int speedPercent)
    {
        _previewDemand = on;
        Sync(speedPercent);
    }

    /// <summary>
    /// Latch for "the meter is not on screen". A one-shot <c>SetDemand(0)</c> from the hide path only lasted
    /// until the next report tick, because <c>OverlayViewModel.Update</c> keeps running while hidden and
    /// reports demand again ~500 ms later. The clock has to stay down until the window comes back.
    /// </summary>
    public static void SetParked(bool parked)
    {
        _parked = parked;
        Sync(_speedPercent);
    }

    /// <summary>Low-spec mode refuses the animation outright, for every demand source at once.</summary>
    public static void SetLowSpec(bool lowSpec)
    {
        _lowSpec = lowSpec;
        Sync(_speedPercent);
    }

    private static void Sync(int speedPercent)
    {
        bool want = !_parked && !_lowSpec && (_rowDemand > 0 || _previewDemand);

        // Speed is only meaningful while running. Updating it on the way DOWN would let the park path
        // (which has no user setting to hand) overwrite the user's choice with a default.
        if (want)
        {
            int speed = Math.Clamp(speedPercent, 50, 200);
            if (_running && speed == _speedPercent)
            {
                return; // already running at this speed; restarting would visibly jump the phase
            }

            _speedPercent = speed;
            foreach (Track t in Tracks.Values)
            {
                var anim = new DoubleAnimation
                {
                    From = 0,
                    // A tile is seamless, so travelling one tile BACKWARDS is just as continuous as forwards —
                    // and two skins moving opposite ways read as two effects even at a glance.
                    To = t.Reverse ? -t.Period : t.Period,
                    Duration = TimeSpan.FromSeconds(t.Seconds * 100.0 / speed),
                    RepeatBehavior = RepeatBehavior.Forever,
                };
                Timeline.SetDesiredFrameRate(anim, FrameRate);
                anim.Freeze();
                t.Transform.BeginAnimation(TranslateTransform.XProperty, anim);
            }

            _running = true;
            return;
        }

        if (!_running)
        {
            return;
        }

        foreach (Track t in Tracks.Values)
        {
            t.Transform.BeginAnimation(TranslateTransform.XProperty, null);
            t.Transform.X = 0;
        }

        _running = false;
    }

    /// <summary>
    /// Park the sweep at a fixed phase, for the offline preview harness only. Rendering to a bitmap has no
    /// animation clock, so without this a captured frame cannot show that the transform reaches the pixels —
    /// and a stopped animation looks exactly like a working one in a single frame.
    /// <para>Restores the demand-derived state on the way out, so <see cref="IsRunning"/> never reports a
    /// fourth "posed" state that no demand flag explains.</para>
    /// </summary>
    public static void SetPreviewPhase(double fraction)
    {
        foreach (Track t in Tracks.Values)
        {
            t.Transform.BeginAnimation(TranslateTransform.XProperty, null);
            t.Transform.X = t.Period * fraction;
        }

        _running = false;
    }

    /// <summary>Recolour the shared brushes for a new brightness. Called on a settings change, never per frame —
    /// rebuilding stops is the expensive half and the slider commits on drag-end.
    /// <para>Note this covers the gauge skins too: one brightness control, both surfaces.</para></summary>
    public static void Rebuild(int brightnessPercent)
    {
        double factor = Math.Clamp(brightnessPercent, 70, 130) / 100.0;
        foreach (((string id, bool light), Track t) in Tracks)
        {
            NameFxPalette.Effect? e = NameFxPalette.Find(id);
            if (e is null)
            {
                continue;
            }

            if (t.Brush is GradientBrush gradient)
            {
                gradient.GradientStops.Clear();
                NameFxPalette.AddStops(gradient, e, light);
                foreach (GradientStop s in gradient.GradientStops)
                {
                    s.Color = NameFxPalette.Scale(s.Color, factor);
                }

                continue;
            }

            // A shape skin's tile is geometry, so brightness cannot be applied stop by stop — the whole
            // drawing is rebuilt at the new brightness and swapped in behind the same transform.
            if (t.Brush is DrawingBrush art)
            {
                DrawingBrush rebuilt = NameFxGaugeArt.Build(e.Motion, light ? e.Light : e.Dark, factor);
                art.Drawing = rebuilt.Drawing;
            }
        }
    }

    private static Dictionary<(string, bool), Track> Build()
    {
        var map = new Dictionary<(string, bool), Track>();
        foreach (NameFxPalette.Effect e in NameFxPalette.All.Where(x => x.Animated))
        {
            bool art = e.IsGauge && NameFxGaugeArt.IsArt(e.Motion);

            // A shape skin's tile is a FRACTION of the bar, so one loop is that fraction — translating a whole
            // bar would run the tile past several times and the speed setting would mean something different
            // for it than for a nickname effect.
            double period = art ? NameFxGaugeArt.TileFraction : (e.IsGauge ? GaugePeriodRelative : NamePeriodPx);
            double seconds = (e.IsGauge ? GaugeSeconds : NameSeconds) * Math.Clamp(e.SpeedScale, 0.25, 4.0);
            foreach (bool light in new[] { false, true })
            {
                var transform = new TranslateTransform();
                Brush b;
                if (art)
                {
                    // A shape skin's colours are baked into its geometry, so there are no stops to add.
                    b = NameFxGaugeArt.Build(e.Motion, light ? e.Light : e.Dark, 1.0);
                }
                else
                {
                    GradientBrush ramp = BuildBrush(e, e.IsGauge ? GaugePeriodRelative : NamePeriodPx);
                    NameFxPalette.AddStops(ramp, e, light);
                    b = ramp;
                }

                // ⚠ The slot MUST match the mapping — see the type remarks. Relative mapping needs
                // RelativeTransform; absolute mapping needs Transform. Getting this wrong does not fail, it
                // just quietly moves the brush by a pixel and looks like a still gradient.
                if (e.IsGauge)
                {
                    b.RelativeTransform = transform;
                }
                else
                {
                    b.Transform = transform;
                }


                // NOT frozen — animated in place.
                map[(e.Id, light)] = new Track(b, transform, period, seconds, e.IsGauge && e.Reverse);
            }
        }

        return map;
    }

    /// <summary>The brush geometry for one effect. Repeat is what makes a translation equal a seamless flow:
    /// the gradient tiles every <c>period</c>, so sliding by exactly one period lands on an identical pattern —
    /// which is true of the radial tile as much as the linear ones.</summary>
    private static GradientBrush BuildBrush(NameFxPalette.Effect e, double period)
    {
        BrushMappingMode mapping = e.IsGauge ? BrushMappingMode.RelativeToBoundingBox : BrushMappingMode.Absolute;

        return new LinearGradientBrush
        {
            MappingMode = mapping,
            SpreadMethod = GradientSpreadMethod.Repeat,
            StartPoint = new Point(0, 0),
            EndPoint = new Point(period, 0),
        };
    }
}

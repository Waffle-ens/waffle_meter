using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace WaffleMeter.App.Wpf;

/// <summary>
/// The moving part of the nickname effects — <see cref="TierSheen"/>'s sibling, and bound by the same two
/// constraints:
/// <list type="bullet">
/// <item><description><c>App.xaml.cs</c> sets <c>RenderOptions.ProcessRenderMode = SoftwareOnly</c> by default
/// and the overlay is an <c>AllowsTransparency</c> layered window, so there is no GPU compositing path and every
/// repaint uploads the whole window surface.</description></item>
/// <item><description><c>OverlayViewModel.Update</c> replaces every row each tick, destroying the DataTemplate
/// tree, so a Storyboard living inside the template restarts every tick and reads as stutter.</description></item>
/// </list>
/// <para>So the brushes live OUTSIDE the rows as shared unfrozen singletons and one low-frequency timer slides
/// their gradient origin. One property write repaints every name using that effect. When no such row is on
/// screen the timer is stopped, so idle CPU is exactly zero.</para>
/// <para><b>Interval.</b> 250 ms — deliberately between <see cref="TierSheen"/>'s 500 ms (a ring reads fine at
/// 2 Hz; a name sweep does not) and the 8 Hz the competitor uses (4× the meter's own 500 ms default repaint, on
/// a layered window, with an in-game frame-drop regression already in this project's history). The sweep is
/// SpreadMethod.Repeat over half the name width, so a step is small and the loop has no seam.</para>
/// </summary>
public static class NameFxSheen
{
    /// <summary>
    /// 12 steps × 250 ms = a 3 s sweep at 100% speed. The gradient repeats every half box width, so one
    /// highlight band crosses a nickname roughly every 1.5 s — slow enough not to nag on a meter you stare at
    /// for whole fights, fast enough to actually read as motion. (6 s was the first try and it was so slow it
    /// looked like a static gradient.)
    /// </summary>
    private const int Steps = 12;

    private static readonly DispatcherTimer Timer = new(DispatcherPriority.Background)
    {
        Interval = TimeSpan.FromMilliseconds(250),
    };

    // ★ Unfrozen on purpose, shared, mutated in place. Keyed by (effect id, isLight).
    private static readonly Dictionary<(string Id, bool Light), LinearGradientBrush> Brushes = Build();

    private static int _phase;
    private static int _rowDemand;
    private static bool _previewDemand;
    private static int _stepPerTick = 1;

    static NameFxSheen() => Timer.Tick += (_, _) => AdvanceOneStep();

    /// <summary>
    /// Move the sweep on by one tick. The live app never calls this — the timer does. It exists for the
    /// offline preview harness, which renders to a bitmap with no dispatcher pump and still has to be able to
    /// prove the brushes actually move (a stopped animation and a working one look identical in one frame).
    /// </summary>
    public static void AdvanceOneStep()
    {
        _phase = (_phase + _stepPerTick) % Steps;
        double t = _phase / (double)Steps;
        foreach (LinearGradientBrush b in Brushes.Values)
        {
            Shift(b, t);
        }
    }

    /// <summary>The shared live brush for an animated effect. Same instance every call.</summary>
    public static Brush BrushFor(string id, bool isLight) => Brushes[(id, isLight)];

    /// <summary>Is the sweep clock actually running? Observable because "nothing moves" has two very different
    /// causes — no demand, or a demand source nobody wired up — and only one of them is a bug.</summary>
    public static bool IsRunning => Timer.IsEnabled;

    /// <summary>
    /// Report how many animated-effect names this frame drew. Zero (or the feature off) stops the timer and
    /// resets the gradients, so a meter sitting in town costs nothing. Call it from the end of every row
    /// rebuild AND from the hide/park path — <see cref="TierSheen"/> only ever got the former, which is why a
    /// hidden meter kept its timer alive.
    /// </summary>
    /// <param name="speedPercent">50~200. Changes how far the phase advances per tick, NOT the timer interval:
    /// doubling the wake-up rate would double the cost, whereas a bigger step is free.</param>
    public static void SetDemand(int count, bool enabled, int speedPercent)
    {
        _stepPerTick = Math.Clamp((int)Math.Round(speedPercent / 100.0), 1, 2);
        _rowDemand = enabled ? Math.Max(0, count) : 0;
        Sync();
    }

    /// <summary>
    /// The settings preview strip is a SECOND, independent demand source. Without it the strip sits frozen:
    /// grants come from the server, so a user deciding whether to keep the animation on has no decorated row on
    /// screen, row demand is zero, and the timer everything depends on is stopped. Being able to see the motion
    /// is the entire reason that strip exists.
    /// </summary>
    public static void SetPreviewDemand(bool on, int speedPercent)
    {
        _stepPerTick = Math.Clamp((int)Math.Round(speedPercent / 100.0), 1, 2);
        _previewDemand = on;
        Sync();
    }

    private static void Sync()
    {
        if (_rowDemand > 0 || _previewDemand)
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
            foreach (LinearGradientBrush b in Brushes.Values)
            {
                Shift(b, 0);
            }
        }
    }

    /// <summary>Recolour the shared brushes for a new brightness. Called on a settings change, never per tick —
    /// rebuilding stops is the expensive half and the slider commits on drag-end.</summary>
    public static void Rebuild(int brightnessPercent)
    {
        double factor = Math.Clamp(brightnessPercent, 70, 130) / 100.0;
        foreach (((string id, bool light), LinearGradientBrush brush) in Brushes)
        {
            NameFxPalette.Effect? e = NameFxPalette.Find(id);
            if (e is null)
            {
                continue;
            }

            brush.GradientStops.Clear();
            NameFxPalette.AddStops(brush, e, light);
            for (int i = 0; i < brush.GradientStops.Count; i++)
            {
                GradientStop s = brush.GradientStops[i];
                s.Color = NameFxPalette.Scale(s.Color, factor);
            }
        }
    }

    /// <summary>Slide the tile origin. Colours and stops never change — only the end points move, and
    /// SpreadMethod.Repeat over half the box means the pattern loops without a seam.</summary>
    private static void Shift(LinearGradientBrush brush, double t)
    {
        brush.StartPoint = new Point(t - 0.5, 0.5);
        brush.EndPoint = new Point(t, 0.5);
    }

    private static Dictionary<(string, bool), LinearGradientBrush> Build()
    {
        var map = new Dictionary<(string, bool), LinearGradientBrush>();
        foreach (NameFxPalette.Effect e in NameFxPalette.All.Where(x => x.Animated))
        {
            foreach (bool light in new[] { false, true })
            {
                var b = new LinearGradientBrush
                {
                    MappingMode = BrushMappingMode.RelativeToBoundingBox,
                    SpreadMethod = GradientSpreadMethod.Repeat,
                    StartPoint = new Point(-0.5, 0.5),
                    EndPoint = new Point(0, 0.5),
                };
                NameFxPalette.AddStops(b, e, light);
                map[(e.Id, light)] = b; // NOT frozen — Shift()/Rebuild() mutate it.
            }
        }

        return map;
    }
}

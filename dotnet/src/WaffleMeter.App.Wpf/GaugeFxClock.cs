using System.Diagnostics;
using System.Windows.Media;

namespace WaffleMeter.App.Wpf;

/// <summary>
/// The one clock every <see cref="GaugeFxLayer"/> reads.
///
/// <para><b>Absolute time, never a per-element animation.</b> Particle positions are a pure function of
/// (elapsed seconds, skin, row seed), so a row that gets rebuilt — which <c>OverlayViewModel.Update</c> does
/// several times a second — resumes exactly where it was instead of restarting its particles at zero. That is
/// also why there is no <c>Storyboard</c> anywhere in this feature.</para>
///
/// <para><b>Gated by the same switch as the nickname sheen, deliberately not by a second copy of it.</b>
/// <see cref="NameFxSheen"/> already resolves parked / low-spec / row demand / preview demand into one answer,
/// and that answer took several bugs to get right (a hide path whose effect lasted one report tick, a preview
/// strip that could drive the meter's clock in low-spec mode). Duplicating the rule here would mean two
/// switches to keep in step; reading it means there is one.</para>
///
/// <para><b>24 fps, not 60.</b> Sparse particles over a game do not survive the extra compositing on an
/// <c>AllowsTransparency</c> layered window, and the frame cost is measured in the preview harness.</para>
/// </summary>
internal static class GaugeFxClock
{
    /// <summary>Target frame rate. The handoff asked for 20~24 and for it to be measured rather than assumed;
    /// the preview harness prints the overlay frame cost with and without the effects.</summary>
    private const double TargetFps = 24.0;

    private static readonly List<GaugeFxLayer> Layers = new();
    private static readonly Stopwatch Watch = Stopwatch.StartNew();

    private static bool _subscribed;
    private static double _lastRealSeconds;
    private static double _lastDrawnAt;
    private static double _artSeconds;

    /// <summary>
    /// The time the art is drawn at. Monotonic and shared, so every layer and every row agree on the phase.
    /// <para>NOT the wall clock: the user's speed setting scales how fast this advances. Scaling the frame RATE
    /// instead would have been wrong in a way that is easy to miss — particle positions are a function of this
    /// value, so drawing less often does not slow anything down, it just drops frames out of the same motion.</para>
    /// </summary>
    internal static double Seconds => _artSeconds;

    /// <summary>Whether the shared render tick is currently subscribed — observable because "nothing moves" has
    /// several very different causes and only one of them is a bug.</summary>
    internal static bool IsRunning => _subscribed;

    internal static void Register(GaugeFxLayer layer)
    {
        if (!Layers.Contains(layer))
        {
            Layers.Add(layer);
        }

        Sync();
    }

    internal static void Unregister(GaugeFxLayer layer)
    {
        Layers.Remove(layer);
        Sync();
    }

    /// <summary>A layer's activation changed (skin granted/revoked, mode switched).</summary>
    internal static void Refresh(GaugeFxLayer layer)
    {
        if (layer.IsLoaded && !Layers.Contains(layer))
        {
            Layers.Add(layer);
        }

        Sync();
    }

    /// <summary>Called by <see cref="NameFxSheen"/> whenever its gate opens or closes, so parking the meter
    /// stops this clock in the same instant rather than at the next tick.</summary>
    internal static void OnGateChanged() => Sync();

    private static void Sync()
    {
        bool want = NameFxSheen.IsRunning && Layers.Exists(l => l.WantsClock);
        if (want == _subscribed)
        {
            return;
        }

        if (want)
        {
            // Re-anchor so the gap while the clock was stopped is not billed to the first frame back.
            _lastRealSeconds = Watch.Elapsed.TotalSeconds;
            CompositionTarget.Rendering += OnRendering;
            _subscribed = true;
            return;
        }

        CompositionTarget.Rendering -= OnRendering;
        _subscribed = false;
    }

    private static void OnRendering(object? sender, EventArgs e)
    {
        double real = Watch.Elapsed.TotalSeconds;
        double delta = real - _lastRealSeconds;
        _lastRealSeconds = real;

        // A long gap means the meter was parked or the machine stalled. Advancing the art by all of it would
        // teleport every particle; clamping keeps the motion continuous across a hide/show.
        _artSeconds += Math.Clamp(delta, 0, 0.25) * (Math.Clamp(NameFxSheen.SpeedPercent, 50, 200) / 100.0);

        if (real - _lastDrawnAt < 1.0 / TargetFps)
        {
            return;
        }

        _lastDrawnAt = real;

        // A layer that has left the tree without raising Unloaded (a parked window) would otherwise be
        // invalidated forever; drop anything no longer connected as we go.
        for (int i = Layers.Count - 1; i >= 0; i--)
        {
            GaugeFxLayer layer = Layers[i];
            if (!layer.IsLoaded)
            {
                Layers.RemoveAt(i);
                continue;
            }

            if (layer.WantsClock)
            {
                layer.InvalidateVisual();
            }
        }

        if (!Layers.Exists(l => l.WantsClock))
        {
            Sync();
        }
    }
}

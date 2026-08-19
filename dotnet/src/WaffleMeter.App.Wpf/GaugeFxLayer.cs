using System.Windows;
using System.Windows.Media;

namespace WaffleMeter.App.Wpf;

/// <summary>
/// The decorative layer that sits ON TOP of a DPS gauge's colour fill — sparks for 잔불, crystals for 서리, and
/// so on.
///
/// <para><b>On top of, never instead of.</b> An earlier attempt replaced the whole bar with a tiled shape brush
/// and was reverted: the gauge is the BACKGROUND the DPS number is read against, and turning it into a picture
/// costs legibility mid-fight, which is a functional loss and not a matter of taste. The fill brush and its
/// opacity are untouched here; this element only adds sparse, low-alpha marks over it.</para>
///
/// <para><b>One element, immediate mode.</b> Every particle is drawn in a single <see cref="OnRender"/> pass
/// with primitive calls and CACHED, frozen pens and brushes — no <c>UIElement</c>, no <c>Storyboard</c> and no
/// per-particle geometry. Per-row visual trees were never an option: <c>OverlayViewModel.Update</c> replaces
/// every row on each report tick, so anything a row owns is rebuilt (and any animation it owns restarts)
/// several times a second.</para>
///
/// <para><b>Shapes are lines and ellipses on purpose.</b> At 2~6 DIP a thick round-capped line is
/// indistinguishable from a rhombus, and both are free of allocation; building a rotated polygon per particle
/// per frame would not be. Glow is likewise faked with two or three concentric low-alpha ellipses rather than a
/// <c>BlurEffect</c>, which would be CPU convolution on a software-rendered layered window over a game.</para>
///
/// <para><b>Alpha comes from <c>PushOpacity</c>, not from new brushes.</b> Particle alpha changes every frame;
/// a <c>SolidColorBrush</c> per particle would allocate thousands per second.</para>
/// </summary>
public sealed class GaugeFxLayer : FrameworkElement
{
    /// <summary>Which skin to draw, or null for none. Set from the row's grant, never inferred from the brush
    /// or the display name.</summary>
    public static readonly DependencyProperty SkinIdProperty = DependencyProperty.Register(
        nameof(SkinId), typeof(string), typeof(GaugeFxLayer),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnActivationChanged));

    /// <summary>Whether the decoration may draw at all. False for 색상만(static), low-spec, 게이지 토글 off and
    /// <c>BarStyle != fill</c>; the colour fill behind it stays in every one of those cases.</summary>
    public static readonly DependencyProperty IsFxEnabledProperty = DependencyProperty.Register(
        nameof(IsFxEnabled), typeof(bool), typeof(GaugeFxLayer),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender, OnActivationChanged));

    /// <summary>
    /// DIP at the left of the bar where nothing is drawn — the rail, the rank chip and the job icon live there.
    /// <para>The icon must look identical under every skin, and a translucent theme would let a spark behind it
    /// tint the badge. Excluding the strip is cheaper and more certain than trying to keep particles away from
    /// it, and the colour fill still runs the full width underneath, so the row's colour never breaks.</para>
    /// </summary>
    public static readonly DependencyProperty ContentExclusionLeftProperty = DependencyProperty.Register(
        nameof(ContentExclusionLeft), typeof(double), typeof(GaugeFxLayer),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Corner radius of the fill panel this sits on, so the decoration is clipped to the same shape.</summary>
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius), typeof(double), typeof(GaugeFxLayer),
        new FrameworkPropertyMetadata(4.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public GaugeFxLayer()
    {
        IsHitTestVisible = false;
        Loaded += (_, _) => GaugeFxClock.Register(this);
        Unloaded += (_, _) => GaugeFxClock.Unregister(this);
    }

    public string? SkinId
    {
        get => (string?)GetValue(SkinIdProperty);
        set => SetValue(SkinIdProperty, value);
    }

    public bool IsFxEnabled
    {
        get => (bool)GetValue(IsFxEnabledProperty);
        set => SetValue(IsFxEnabledProperty, value);
    }

    public double ContentExclusionLeft
    {
        get => (double)GetValue(ContentExclusionLeftProperty);
        set => SetValue(ContentExclusionLeftProperty, value);
    }

    public double CornerRadius
    {
        get => (double)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    /// <summary>True when this layer wants the clock running.</summary>
    internal bool WantsClock => IsFxEnabled && GaugeFxArt.Knows(SkinId);

    /// <summary>
    /// A stable per-row phase offset so two rows carrying the same skin are not in lockstep.
    /// <para>Bound to the row's entity id, NOT to a counter or to the row index. <c>OverlayViewModel.Update</c>
    /// rebuilds every row several times a second and rows reorder as damage lands, so anything positional
    /// would re-seed the particles constantly — which looks like the effect stuttering.</para>
    /// </summary>
    public static readonly DependencyProperty SeedProperty = DependencyProperty.Register(
        nameof(Seed), typeof(int), typeof(GaugeFxLayer),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

    public int Seed
    {
        get => (int)GetValue(SeedProperty);
        set => SetValue(SeedProperty, value);
    }

    /// <summary>
    /// Draw at this time instead of the shared clock's. <c>NaN</c> (the default) uses the clock.
    /// <para>For the offline preview harness only. <c>RenderTargetBitmap</c> has no animation clock, so a
    /// captured frame cannot otherwise show a chosen phase — and a stopped effect looks exactly like a working
    /// one in a single still. Pinning it per layer also lets one image hold several phases side by side, which
    /// a global override could not.</para>
    /// </summary>
    public static readonly DependencyProperty PreviewSecondsProperty = DependencyProperty.Register(
        nameof(PreviewSeconds), typeof(double), typeof(GaugeFxLayer),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    public double PreviewSeconds
    {
        get => (double)GetValue(PreviewSecondsProperty);
        set => SetValue(PreviewSecondsProperty, value);
    }

    /// <summary>Contributes nothing to layout — the row's geometry has regressed twice and this must not be
    /// able to participate in it. The element fills whatever cell it is placed in and measures as zero.</summary>
    protected override Size MeasureOverride(Size availableSize) => new(0, 0);

    protected override void OnRender(DrawingContext dc)
    {
        if (!IsFxEnabled || GaugeFxArt.Find(SkinId) is not { } art)
        {
            return;
        }

        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 1 || h <= 1)
        {
            return;
        }

        double left = Math.Clamp(ContentExclusionLeft, 0, w);
        if (w - left <= 2)
        {
            return; // the bar is shorter than the protected strip: nothing to decorate
        }

        // Clipped to the fill panel's own rounded rectangle so nothing escapes the bar's right edge — the
        // decoration must end exactly where the colour does, at every bar length.
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, h), CornerRadius, CornerRadius));
        dc.PushClip(new RectangleGeometry(new Rect(left, 0, w - left, h)));

        double at = double.IsNaN(PreviewSeconds) ? GaugeFxClock.Seconds : PreviewSeconds;
        art.Draw(dc, new Rect(left, 0, w - left, h), at, Seed);

        dc.Pop();
        dc.Pop();
    }

    private static void OnActivationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is GaugeFxLayer layer)
        {
            GaugeFxClock.Refresh(layer);
        }
    }
}

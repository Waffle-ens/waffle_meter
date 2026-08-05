namespace WaffleMeter.App.Wpf;

/// <summary>The 오드 목록 overlay panel: every character this install has seen and the 오드 it last held.
/// Opened from the meter's 오드 badge. Reuses <see cref="OverlayPanelWindow"/> windowing (drag, park/present,
/// topmost re-assert) — read-only, so it has no row interactions of its own.</summary>
public partial class AetherPanel : OverlayPanelWindow
{
    public AetherPanel()
    {
        InitializeComponent();
    }
}

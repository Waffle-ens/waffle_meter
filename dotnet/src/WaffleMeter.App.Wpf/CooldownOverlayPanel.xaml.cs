namespace WaffleMeter.App.Wpf;

/// <summary>The skill-cooldown overlay window: a draggable grid of the local player's skills with a pie that
/// wipes away as each cooldown runs out. A parallel overlay to the buff panel — same window shell, same
/// geometry rules (App owns the width cap + off-screen guard; see <c>App.ReflowOverlay</c>) — but a separate
/// window with its own toggle, because "what is on me" and "what can I press" are read at different moments
/// and users park them in different places.</summary>
public partial class CooldownOverlayPanel : OverlayPanelWindow
{
    public CooldownOverlayPanel(CooldownOverlayViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}

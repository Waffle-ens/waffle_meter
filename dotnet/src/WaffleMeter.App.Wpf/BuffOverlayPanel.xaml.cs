namespace WaffleMeter.App.Wpf;

/// <summary>The combat-assist overlay window: a small draggable strip of the local player's active buff
/// slots. A parallel overlay (like the join/history panels); its content comes from
/// <see cref="BuffOverlayViewModel"/>, refreshed by App on a timer.</summary>
public partial class BuffOverlayPanel : OverlayPanelWindow
{
    public BuffOverlayPanel(BuffOverlayViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}

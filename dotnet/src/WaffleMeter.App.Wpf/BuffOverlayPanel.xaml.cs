namespace WaffleMeter.App.Wpf;

/// <summary>The combat-assist overlay window: a small draggable strip of the local player's active buff
/// slots. A parallel overlay (like the join/history panels); its content comes from
/// <see cref="BuffOverlayViewModel"/>, refreshed by App on a timer. Unlike the other panels this one is
/// <c>SizeToContent</c>, so its width grows with slot count × icon scale — App owns the resulting geometry
/// (width cap + off-screen guard); see <c>App.ReflowBuffOverlay</c>.</summary>
public partial class BuffOverlayPanel : OverlayPanelWindow
{
    public BuffOverlayPanel(BuffOverlayViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}

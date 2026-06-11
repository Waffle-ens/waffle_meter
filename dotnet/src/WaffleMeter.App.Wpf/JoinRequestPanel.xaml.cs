using System.Windows.Threading;

namespace WaffleMeter.App.Wpf;

/// <summary>
/// The party join-request overlay panel. Reuses the <see cref="OverlayPanelWindow"/> windowing and adds
/// a 250ms timer (running only while presented) that drives the per-row countdown via
/// <see cref="JoinRequestViewModel.Tick"/>.
/// </summary>
public partial class JoinRequestPanel : OverlayPanelWindow
{
    private readonly DispatcherTimer _ticker;

    public JoinRequestPanel()
    {
        InitializeComponent();
        _ticker = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        _ticker.Tick += (_, _) => (DataContext as JoinRequestViewModel)?.Tick();
    }

    protected override void OnPresented() => _ticker.Start();

    protected override void OnParked() => _ticker.Stop();
}

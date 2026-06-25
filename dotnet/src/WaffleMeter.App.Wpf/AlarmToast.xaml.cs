using System.Windows.Threading;

namespace WaffleMeter.App.Wpf;

/// <summary>
/// A transient alarm toast (슈고 페스타 / future custom alarms). Reuses <see cref="OverlayPanelWindow"/>
/// windowing + its ✕ close (CloseRequested); auto-dismisses (parks itself) a few seconds after it is shown.
/// </summary>
public partial class AlarmToast : OverlayPanelWindow
{
    private readonly DispatcherTimer _dismiss = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(9) };

    public AlarmToast()
    {
        InitializeComponent();
        _dismiss.Tick += (_, _) =>
        {
            _dismiss.Stop();
            Park();
        };
    }

    protected override void OnPresented()
    {
        // Restart the auto-dismiss countdown each time the toast is (re)shown.
        _dismiss.Stop();
        _dismiss.Start();
    }

    protected override void OnParked() => _dismiss.Stop();
}

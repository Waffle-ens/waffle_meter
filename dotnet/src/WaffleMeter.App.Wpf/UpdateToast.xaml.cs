using System.Windows;

namespace WaffleMeter.App.Wpf;

/// <summary>The auto-update toast (bottom-right). Reuses <see cref="OverlayPanelWindow"/> windowing +
/// its ✕ close (CloseRequested); adds a 지금 재시작 action.</summary>
public partial class UpdateToast : OverlayPanelWindow
{
    /// <summary>Raised when the user clicks 지금 재시작 (App calls UpdateService.ApplyAndRestart).</summary>
    public event Action? RestartRequested;

    public UpdateToast()
    {
        InitializeComponent();
    }

    private void OnRestart(object sender, RoutedEventArgs e) => RestartRequested?.Invoke();
}

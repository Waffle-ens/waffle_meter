using System.Windows;

namespace WaffleMeter.App.Wpf;

/// <summary>The 오드 목록 overlay panel: every character this install has seen and the 오드 it last held.
/// Opened from the meter's 오드 badge or the tray menu. Reuses <see cref="OverlayPanelWindow"/> windowing
/// (drag, park/present, topmost re-assert).</summary>
public partial class AetherPanel : OverlayPanelWindow
{
    public AetherPanel()
    {
        InitializeComponent();
    }

    private void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AetherRowViewModel row } && DataContext is AetherPanelViewModel vm)
        {
            vm.RequestRemove(row.IdentityHash);
            e.Handled = true;
        }
    }
}

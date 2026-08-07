using System.Windows;

namespace WaffleMeter.App.Wpf;

/// <summary>The 컨텐츠 관리 overlay panel: every character this install has seen, the 오드 it last held, and its
/// weekly 성역 clears. Opened from the meter's 오드 badge or the tray menu. Reuses
/// <see cref="OverlayPanelWindow"/> windowing (drag, park/present, topmost re-assert).</summary>
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

    /// <summary>A weekly-clear chip was clicked: flip it by hand. Handled here rather than as a Button so the
    /// chip keeps the row's own hover treatment instead of a button chrome.</summary>
    private void OnWeeklyChipClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: WeeklyContentCellViewModel cell }
            && DataContext is AetherPanelViewModel vm)
        {
            vm.RequestWeeklyToggle(cell.IdentityHash, cell.Slug);
            e.Handled = true;
        }
    }
}

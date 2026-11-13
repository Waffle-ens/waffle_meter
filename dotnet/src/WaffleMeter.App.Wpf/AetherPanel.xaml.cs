using System.Windows;
using System.Windows.Input;

namespace WaffleMeter.App.Wpf;

/// <summary>The 컨텐츠 관리 overlay panel: every character this install has seen, the 오드 it last held, and its
/// weekly 성역 clears (컨텐츠 tab), and its currencies with the per-server 총 키나 (재화 관리 tab). Opened from the
/// meter's 오드 badge, the tray menu or a hotkey. Reuses <see cref="OverlayPanelWindow"/> windowing (drag,
/// park/present, topmost re-assert).</summary>
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

    /// <summary>A 총 키나 line was clicked: an account line (a server split between accounts) unfolds into its
    /// characters for correcting; any other line ignores it.</summary>
    private void OnServerKinaClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ServerKinaViewModel line } && DataContext is AetherPanelViewModel vm
            && line.Expandable)
        {
            vm.ToggleServerKina(line);
            e.Handled = true;
        }
    }

    /// <summary>A move button under an unfolded account line ("계정 2로", "새 계정으로 분리", "자동으로 되돌리기").</summary>
    private void OnAccountMoveClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AccountMoveViewModel move } && DataContext is AetherPanelViewModel vm)
        {
            vm.RequestAccountMove(move);
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

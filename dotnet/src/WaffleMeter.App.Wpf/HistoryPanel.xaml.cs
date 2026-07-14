using System.Windows;
using System.Windows.Input;

namespace WaffleMeter.App.Wpf;

/// <summary>The battle-history overlay panel: a list of saved battles; clicking one replays it in the
/// meter. Reuses <see cref="OverlayPanelWindow"/> windowing.</summary>
public partial class HistoryPanel : OverlayPanelWindow
{
    public HistoryPanel()
    {
        InitializeComponent();
    }

    private void OnRowClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: BattleHistoryRowViewModel row } && DataContext is BattleHistoryViewModel vm)
        {
            vm.SelectBattle(row.Report);
        }
    }

    // ▶ on a row: open that battle's positional replay. Handled here (not via the row click) so it doesn't
    // also swap the meter to this battle.
    private void OnReplayClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: BattleHistoryRowViewModel row } && DataContext is BattleHistoryViewModel vm)
        {
            vm.RequestReplay(row.Report);
            e.Handled = true;
        }
    }
}

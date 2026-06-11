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
}

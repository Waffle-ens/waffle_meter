using System.Windows;

namespace WaffleMeter.App.Wpf;

/// <summary>The join-panel skill-settings flyout (port of JoinRequestSkillSettings). Reuses
/// <see cref="OverlayPanelWindow"/> windowing; the group "전체 선택 / 해제" buttons bulk-toggle.</summary>
public partial class SkillSettingsFlyout : OverlayPanelWindow
{
    public SkillSettingsFlyout()
    {
        InitializeComponent();
    }

    private void OnSelectAll(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SkillJobGroupViewModel group })
        {
            group.SelectAll();
        }
    }

    private void OnDeselectAll(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SkillJobGroupViewModel group })
        {
            group.DeselectAll();
        }
    }
}

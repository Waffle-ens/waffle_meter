using System.Windows;

namespace WaffleMeter.App.Wpf;

/// <summary>쿨타임 오버레이의 "표시할 스킬 선택" 창. 참가요청 배지 픽커와 행 뷰모델은 공유하지만 창은
/// 따로다 — 모수가 221개 × 9직업이라 "내 직업만" 필터와 직업별 선택 수 표시가 필요하고, 그 둘은 배포 중인
/// 저쪽 화면에 넣을 이유가 없다.</summary>
public partial class CooldownPickerFlyout : OverlayPanelWindow
{
    public CooldownPickerFlyout()
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

    // 전체 선택/해제는 <b>지금 보이는</b> 묶음에만 적용한다. "내 직업만"이 켜진 채로 9개 직업을 통째로
    // 꺼 버리면 사용자가 보지도 못한 선택이 사라지고, 되돌릴 방법도 화면에 없다.
    private void OnSelectAllVisible(object sender, RoutedEventArgs e) =>
        (DataContext as CooldownPickerViewModel)?.SelectAllVisible();

    private void OnDeselectAllVisible(object sender, RoutedEventArgs e) =>
        (DataContext as CooldownPickerViewModel)?.DeselectAllVisible();
}

using System.Diagnostics;
using System.Windows;

namespace WaffleMeter.App.Wpf;

/// <summary>후원 안내창. 계좌를 직접 띄우지 않고 후원 플랫폼(<see cref="ExternalLinks.Donate"/>) 링크만 연다 —
/// 계좌번호를 UI에 박아두면 바뀔 때마다 릴리스를 내야 하고, 오탈자 한 글자가 곧 남의 계좌가 된다.
/// 후원이 선택 사항이라는 고지는 창을 열자마자 가장 먼저 보이도록 배치했다.</summary>
public partial class DonateDialog : Window
{
    public DonateDialog()
    {
        InitializeComponent();
    }

    private void OnDonate(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo { FileName = ExternalLinks.Donate, UseShellExecute = true });
        Close();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}

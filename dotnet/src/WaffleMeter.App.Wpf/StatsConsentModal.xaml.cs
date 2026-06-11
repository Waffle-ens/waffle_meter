using System.Windows;

namespace WaffleMeter.App.Wpf;

/// <summary>
/// First-run / new-character stats consent dialog (port of React StatsConsentModal). After ShowDialog:
/// <see cref="Accepted"/> tells whether the user agreed, and <see cref="PublicCharacter"/> their public
/// toggle. Closing without choosing counts as decline (matches React onOpenChange → onDecline).
/// </summary>
public partial class StatsConsentModal : Window
{
    public bool Accepted { get; private set; }
    public bool PublicCharacter { get; private set; }

    public StatsConsentModal(string characterLabel)
    {
        InitializeComponent();
        DescText.Text = $"{characterLabel} 기준으로 보스를 처치해 끝난 전투 요약만 웹 통계에 사용할 수 있습니다.";
    }

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        Accepted = true;
        PublicCharacter = PublicToggle.IsChecked == true;
        DialogResult = true;
        Close();
    }

    private void OnDecline(object sender, RoutedEventArgs e)
    {
        Accepted = false;
        DialogResult = false;
        Close();
    }
}

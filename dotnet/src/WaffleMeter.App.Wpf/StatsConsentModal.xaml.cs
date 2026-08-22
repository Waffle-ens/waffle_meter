using System.Windows;

namespace WaffleMeter.App.Wpf;

/// <summary>
/// First-run / new-character stats consent dialog (port of React StatsConsentModal). After ShowDialog:
/// <see cref="Answered"/> tells whether a button was pressed at all, <see cref="Accepted"/> whether the user
/// agreed, and <see cref="PublicCharacter"/> their public toggle.
/// <para>⚠ Closing the window WITHOUT pressing anything is NOT a decline. It used to be recorded as one
/// ("matches React onOpenChange → onDecline"), and that turned an X click — or this dialog opening behind a
/// full-screen game and being dismissed blind — into a permanent, silent opt-out for that one character: it
/// stopped uploading, vanished from 내 캐릭터 관리 (that list only shows accepted rows), and was never asked
/// again, because <c>NeedsConsentPrompt</c> only fires on <c>unknown</c>. Leaving it unknown re-asks next
/// session, which is the only honest reading of "the user did not answer".</para>
/// </summary>
public partial class StatsConsentModal : Window
{
    /// <summary>A button was pressed. False when the window was closed without answering.</summary>
    public bool Answered { get; private set; }

    public bool Accepted { get; private set; }
    public bool PublicCharacter { get; private set; }

    public StatsConsentModal(string characterLabel)
    {
        InitializeComponent();
        DescText.Text = $"{characterLabel} 기준으로 보스를 처치해 끝난 전투 요약만 웹 통계에 사용할 수 있습니다.";
    }

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        Answered = true;
        Accepted = true;
        PublicCharacter = PublicToggle.IsChecked == true;
        DialogResult = true;
        Close();
    }

    private void OnDecline(object sender, RoutedEventArgs e)
    {
        Answered = true;
        Accepted = false;
        DialogResult = false;
        Close();
    }
}

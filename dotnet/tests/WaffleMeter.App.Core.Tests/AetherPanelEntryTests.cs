using WaffleMeter.App.Core;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

/// <summary>
/// 컨텐츠 관리 패널의 진입 규칙 — 오드 배지·트레이·'컨텐츠 관리' 단축키는 컨텐츠 탭을, '재화 관리' 단축키는 재화
/// 관리 탭을 요청한다. 자기 탭이 떠 있으면 닫고, 다른 탭이 떠 있으면 탭만 바꾸고, 닫혀 있으면 그 탭으로 연다.
/// </summary>
public sealed class AetherPanelEntryTests
{
    [Theory]
    [InlineData(AetherPanelTab.Content, AetherPanelTab.Content)]
    [InlineData(AetherPanelTab.Content, AetherPanelTab.Currency)]
    [InlineData(AetherPanelTab.Currency, AetherPanelTab.Content)]
    [InlineData(AetherPanelTab.Currency, AetherPanelTab.Currency)]
    public void A_hidden_panel_opens_on_the_requested_tab_whatever_it_showed_last(
        AetherPanelTab lastShown, AetherPanelTab requested)
    {
        Assert.Equal(AetherPanelAction.Open, AetherPanelEntry.Decide(visible: false, lastShown, requested));
    }

    /// <summary>탭이 생기기 전의 배지·단축키 동작 그대로 — 컨텐츠 탭이 떠 있을 때 배지를 다시 누르면 닫힌다.</summary>
    [Fact]
    public void The_badge_still_closes_the_panel_it_opened()
    {
        Assert.Equal(
            AetherPanelAction.Close,
            AetherPanelEntry.Decide(visible: true, AetherPanelTab.Content, AetherPanelTab.Content));
    }

    /// <summary>재화 관리 단축키는 토글이다 — 재화 관리 탭이 떠 있을 때 다시 누르면 닫힌다.</summary>
    [Fact]
    public void The_currency_hotkey_closes_its_own_tab()
    {
        Assert.Equal(
            AetherPanelAction.Close,
            AetherPanelEntry.Decide(visible: true, AetherPanelTab.Currency, AetherPanelTab.Currency));
    }

    /// <summary>다른 탭이 떠 있을 때는 닫지 않고 바꿔 보여 준다 — 닫아 버리면 한 번 더 눌러야 한다.</summary>
    [Theory]
    [InlineData(AetherPanelTab.Content, AetherPanelTab.Currency)]
    [InlineData(AetherPanelTab.Currency, AetherPanelTab.Content)]
    public void An_open_panel_on_the_other_tab_switches_instead_of_closing(AetherPanelTab shown, AetherPanelTab requested)
    {
        Assert.Equal(AetherPanelAction.Switch, AetherPanelEntry.Decide(visible: true, shown, requested));
    }

    /// <summary>컨텐츠 탭의 머리줄은 탭이 생기기 전 그대로 — 오드 열 바로 위라 '합계'가 오드 합계로 읽힌다.</summary>
    [Fact]
    public void The_content_tab_header_keeps_the_aether_total()
    {
        Assert.Equal("캐릭터 5명 · 합계 5,740", AetherPanelSummary.Format(AetherPanelTab.Content, 5, 5_740));
    }

    /// <summary>재화 관리 탭에서는 이름 없는 오드 합계가 키나 목록 위에서 재화 합계로 읽힌다 — 캐릭터 수만 남긴다.</summary>
    [Fact]
    public void The_currency_tab_header_drops_the_aether_total()
    {
        string text = AetherPanelSummary.Format(AetherPanelTab.Currency, 5, 5_740);

        Assert.Equal("캐릭터 5명", text);
        Assert.DoesNotContain("합계", text, StringComparison.Ordinal);
        Assert.DoesNotContain("5,740", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(AetherPanelTab.Content)]
    [InlineData(AetherPanelTab.Currency)]
    public void No_characters_means_no_summary(AetherPanelTab tab)
    {
        Assert.Equal(string.Empty, AetherPanelSummary.Format(tab, 0, 0));
    }
}

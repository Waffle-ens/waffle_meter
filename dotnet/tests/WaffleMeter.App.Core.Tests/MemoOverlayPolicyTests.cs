using WaffleMeter.App.Core;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

/// <summary>
/// 메모 오버레이의 표시·잠금 규칙 고정. 창(App.Wpf)에는 테스트 프로젝트가 없으므로 "언제 뜨는가", "잠긴 창이
/// 언제 클릭을 받는가"는 여기서만 붙잡을 수 있다.
/// </summary>
public sealed class MemoOverlayPolicyTests
{
    [Fact]
    public void Off_means_off_even_while_editing()
    {
        Assert.False(MemoOverlayPolicy.IsVisible(show: false, foregroundOk: true, keepWhenMeterHidden: true, meterVisible: true, editing: false));
        Assert.False(MemoOverlayPolicy.IsVisible(show: false, foregroundOk: true, keepWhenMeterHidden: true, meterVisible: true, editing: true));
    }

    [Fact]
    public void Follows_the_meter_when_not_kept()
    {
        // 미터가 떠 있고 게임이 활성 → 뜬다.
        Assert.True(MemoOverlayPolicy.IsVisible(show: true, foregroundOk: true, keepWhenMeterHidden: false, meterVisible: true, editing: false));
        // Ctrl+H·트레이로 미터를 숨기면 같이 숨는다.
        Assert.False(MemoOverlayPolicy.IsVisible(show: true, foregroundOk: true, keepWhenMeterHidden: false, meterVisible: false, editing: false));
    }

    [Fact]
    public void Keep_survives_a_hidden_meter_but_not_a_background_game()
    {
        // '미터를 숨겨도 메모 유지' 는 트레이 숨김만 풀어 준다.
        Assert.True(MemoOverlayPolicy.IsVisible(show: true, foregroundOk: true, keepWhenMeterHidden: true, meterVisible: false, editing: false));
        // 게임이 비활성이면(바탕화면·브라우저) 유지 토글이 켜져 있어도 숨는다 — 기존 오버레이 유지와 같은 축.
        Assert.False(MemoOverlayPolicy.IsVisible(show: true, foregroundOk: false, keepWhenMeterHidden: true, meterVisible: false, editing: false));
        Assert.False(MemoOverlayPolicy.IsVisible(show: true, foregroundOk: false, keepWhenMeterHidden: true, meterVisible: true, editing: false));
        Assert.False(MemoOverlayPolicy.IsVisible(show: true, foregroundOk: false, keepWhenMeterHidden: false, meterVisible: true, editing: false));
    }

    [Fact]
    public void Editing_keeps_it_on_screen_whatever_the_foreground_says()
    {
        // 편집 중에는 메모 자신이 포그라운드다. 그 순간의 판정으로 입력 중인 창을 치우면 안 된다.
        Assert.True(MemoOverlayPolicy.IsVisible(show: true, foregroundOk: false, keepWhenMeterHidden: false, meterVisible: false, editing: true));
    }

    [Theory]
    [InlineData(0.0, true)]
    [InlineData(0.004, true)]
    [InlineData(MemoOverlayPolicy.ChromeHiddenAtOrBelow, true)]
    [InlineData(0.01, false)]
    [InlineData(0.6, false)]
    [InlineData(1.0, false)]
    public void Chrome_hides_only_at_the_bottom_of_the_slider(double opacity, bool hidden) =>
        Assert.Equal(hidden, MemoOverlayPolicy.IsChromeHidden(opacity));

    [Theory]
    [InlineData(-0.5, 0.0)]
    [InlineData(0.25, 0.25)]
    [InlineData(7.0, 1.0)]
    [InlineData(double.NaN, 0.6)]
    [InlineData(double.PositiveInfinity, 1.0)]
    [InlineData(double.NegativeInfinity, 0.0)]
    public void Opacity_is_clamped_and_NaN_falls_back(double raw, double expected) =>
        Assert.Equal(expected, MemoOverlayPolicy.ClampOpacity(raw, 0.6));

    [Fact]
    public void Lock_button_hides_at_minimum_opacity_until_ctrl_hover()
    {
        Assert.True(MemoOverlayPolicy.ShowLockButton(chromeHidden: false, ctrlHover: false));
        Assert.False(MemoOverlayPolicy.ShowLockButton(chromeHidden: true, ctrlHover: false));
        // 최하 투명도에서도 Ctrl 을 쥐고 메모 위에 올리면 꺼내 준다 — 아니면 잠긴 메모를 풀 대상이 사라진다.
        Assert.True(MemoOverlayPolicy.ShowLockButton(chromeHidden: true, ctrlHover: true));
    }

    [Fact]
    public void Peek_needs_ctrl_and_the_lock_button_and_a_locked_window()
    {
        Assert.True(MemoOverlayPolicy.ShouldPeek(locked: true, ctrlDown: true, cursorInLockButton: true, wasPeeking: false, mouseButtonDown: false));

        // Ctrl 없는 평클릭은 메모 어디서든 게임으로 간다.
        Assert.False(MemoOverlayPolicy.ShouldPeek(locked: true, ctrlDown: false, cursorInLockButton: true, wasPeeking: false, mouseButtonDown: false));
        // Ctrl+클릭이라도 잠금 버튼 밖이면 게임으로 간다.
        Assert.False(MemoOverlayPolicy.ShouldPeek(locked: true, ctrlDown: true, cursorInLockButton: false, wasPeeking: false, mouseButtonDown: false));
        // 잠기지 않은 창은 애초에 통과가 아니라 걷을 것도 없다.
        Assert.False(MemoOverlayPolicy.ShouldPeek(locked: false, ctrlDown: true, cursorInLockButton: true, wasPeeking: true, mouseButtonDown: true));
    }

    [Fact]
    public void A_press_in_flight_keeps_the_peek_until_release()
    {
        // 누른 채 Ctrl 을 먼저 뗐거나 커서가 살짝 빠졌다 — 뗌(Up)이 게임으로 새지 않게 버튼을 놓을 때까지 유지.
        Assert.True(MemoOverlayPolicy.ShouldPeek(locked: true, ctrlDown: false, cursorInLockButton: false, wasPeeking: true, mouseButtonDown: true));
        // 버튼을 놓으면 즉시 통과로 돌아간다.
        Assert.False(MemoOverlayPolicy.ShouldPeek(locked: true, ctrlDown: false, cursorInLockButton: false, wasPeeking: true, mouseButtonDown: false));
        // 엿보지 않던 상태에서 버튼만 눌려 있는 것(게임에서 드래그 중)은 유지 사유가 아니다.
        Assert.False(MemoOverlayPolicy.ShouldPeek(locked: true, ctrlDown: false, cursorInLockButton: true, wasPeeking: false, mouseButtonDown: true));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void Poll_runs_only_while_locked_or_at_minimum_opacity(bool locked, bool chromeHidden, bool expected) =>
        Assert.Equal(expected, MemoOverlayPolicy.NeedsPeekPoll(locked, chromeHidden));

    [Fact]
    public void Esc_hands_the_foreground_back()
    {
        Assert.True(MemoOverlayPolicy.ShouldRestoreForeground(requested: true, deactivated: false, memoStillForeground: true, previousAlive: true));
    }

    [Fact]
    public void Ending_an_edit_with_the_memos_own_lock_or_close_button_hands_the_foreground_back()
    {
        // 잠금 버튼·✕ 는 활성 창 안의 클릭이라 비활성화가 없다 — 메모가 그대로 포그라운드다. 돌려주지 않으면
        // 게임은 배경에 남고(FPS 하락) 다음 Alt+F4 가 편집이 끝난 메모 창을 닫는다.
        Assert.True(MemoOverlayPolicy.ShouldRestoreForeground(requested: false, deactivated: false, memoStillForeground: true, previousAlive: true));
    }

    [Fact]
    public void A_non_deactivating_end_after_focus_already_moved_leaves_it_alone()
    {
        Assert.False(MemoOverlayPolicy.ShouldRestoreForeground(requested: false, deactivated: false, memoStillForeground: false, previousAlive: true));
    }

    [Fact]
    public void An_outside_click_keeps_the_foreground_the_user_picked_even_while_it_still_reads_as_the_memo()
    {
        // 바깥 클릭(설정 창·브라우저)으로 끝난 편집 — 되돌리면 가로채기다. 같은 UI 스레드의 설정 창이 활성화될
        // 때는 Deactivated 안에서 GetForegroundWindow 가 아직 메모를 가리킨다(실측) — 그래도 돌려주면 안 된다.
        Assert.False(MemoOverlayPolicy.ShouldRestoreForeground(requested: false, deactivated: true, memoStillForeground: true, previousAlive: true));
        Assert.False(MemoOverlayPolicy.ShouldRestoreForeground(requested: false, deactivated: true, memoStillForeground: false, previousAlive: true));
    }

    [Fact]
    public void Nothing_to_hand_back_to_when_the_previous_window_is_gone()
    {
        Assert.False(MemoOverlayPolicy.ShouldRestoreForeground(requested: true, deactivated: false, memoStillForeground: true, previousAlive: false));
        Assert.False(MemoOverlayPolicy.ShouldRestoreForeground(requested: false, deactivated: false, memoStillForeground: true, previousAlive: false));
    }
}

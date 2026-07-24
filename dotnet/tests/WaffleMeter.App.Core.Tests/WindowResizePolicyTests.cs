using WaffleMeter.App.Core;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

/// <summary>
/// 미터 창 크기 조절 규칙 고정. 두 번의 회귀(v2.7.5 "10인 공대 아래 행 잘림", v2.8.1 "세로 핸들 실종")가
/// 모두 이 규칙을 코드가 아닌 주석으로만 갖고 있어서 생겼으므로, 핸들 표(8방향)와 자동/고정 판정을
/// 헤드리스로 못박는다.
/// </summary>
public sealed class WindowResizePolicyTests
{
    private const double W = 420, H = 300, M = 6;

    [Theory]
    // 네 모서리
    [InlineData(0, 0, WindowResizePolicy.HtTopLeft)]
    [InlineData(W, 0, WindowResizePolicy.HtTopRight)]
    [InlineData(0, H, WindowResizePolicy.HtBottomLeft)]
    [InlineData(W, H, WindowResizePolicy.HtBottomRight)]
    // 네 변 — 세로 핸들(위/아래)이 살아 있어야 사용자가 높이를 고정할 수 있다
    [InlineData(0, H / 2, WindowResizePolicy.HtLeft)]
    [InlineData(W, H / 2, WindowResizePolicy.HtRight)]
    [InlineData(W / 2, 0, WindowResizePolicy.HtTop)]
    [InlineData(W / 2, H, WindowResizePolicy.HtBottom)]
    // 가장자리 margin 안쪽 경계값
    [InlineData(M, M, WindowResizePolicy.HtTopLeft)]
    [InlineData(W - M, H - M, WindowResizePolicy.HtBottomRight)]
    // 본문 = 리사이즈 아님(헤더 드래그·버튼·행 클릭이 그대로 동작해야 한다)
    [InlineData(W / 2, H / 2, WindowResizePolicy.HtClient)]
    [InlineData(M + 1, M + 1, WindowResizePolicy.HtClient)]
    // 창 밖 좌표
    [InlineData(-1, H / 2, WindowResizePolicy.HtClient)]
    [InlineData(W + 1, H / 2, WindowResizePolicy.HtClient)]
    public void Resolve_maps_every_edge_and_corner(double x, double y, int expected) =>
        Assert.Equal(expected, WindowResizePolicy.Resolve(x, y, W, H, M));

    [Fact]
    public void Vertical_hits_are_the_six_that_can_change_height()
    {
        foreach (int code in new[]
                 {
                     WindowResizePolicy.HtTop, WindowResizePolicy.HtTopLeft, WindowResizePolicy.HtTopRight,
                     WindowResizePolicy.HtBottom, WindowResizePolicy.HtBottomLeft, WindowResizePolicy.HtBottomRight,
                 })
        {
            Assert.True(WindowResizePolicy.IsVerticalHit(code));
        }

        foreach (int code in new[]
                 {
                     WindowResizePolicy.HtLeft, WindowResizePolicy.HtRight,
                     WindowResizePolicy.HtClient, WindowResizePolicy.HtUnknown,
                 })
        {
            Assert.False(WindowResizePolicy.IsVerticalHit(code));
        }
    }

    [Fact]
    public void A_vertical_drag_that_moved_the_height_pins_it()
    {
        Assert.True(WindowResizePolicy.IsManualAfterDrag(WindowResizePolicy.HtBottom, 416, 486));
        Assert.True(WindowResizePolicy.IsManualAfterDrag(WindowResizePolicy.HtTop, 416, 300));
        Assert.True(WindowResizePolicy.IsManualAfterDrag(WindowResizePolicy.HtBottomRight, 416, 486));
    }

    [Fact]
    public void A_width_drag_never_pins_the_height()
    {
        // 좌/우 핸들은 폭만 바꾼다. 여기서 고정으로 판정하면 폭을 한 번 조절한 사용자가 그대로
        // 높이까지 잠겨버린다 — v2.8.1 제보(인원이 늘어도 행이 잘림)의 실제 모양.
        Assert.False(WindowResizePolicy.IsManualAfterDrag(WindowResizePolicy.HtRight, 416, 416));
        Assert.False(WindowResizePolicy.IsManualAfterDrag(WindowResizePolicy.HtLeft, 416, 416));

        // 폭 드래그 도중 높이가 변한 것처럼 보여도(행 수가 같이 바뀌는 등) 모드는 건드리지 않는다.
        Assert.False(WindowResizePolicy.IsManualAfterDrag(WindowResizePolicy.HtRight, 416, 486));
    }

    [Fact]
    public void Grabbing_the_bottom_edge_without_moving_does_not_pin()
    {
        // 실측: 픽셀 이동이 0인 테두리 클릭에도 WPF는 SizeToContent를 끈다. 그걸 고정 의도로 읽으면
        // 잘못 집었다 뗀 사용자의 높이가 조용히 잠긴다.
        Assert.False(WindowResizePolicy.IsManualAfterDrag(WindowResizePolicy.HtBottom, 416, 416));
        Assert.False(WindowResizePolicy.IsManualAfterDrag(
            WindowResizePolicy.HtBottom, 416, 416 + WindowResizePolicy.HeightDeltaTolerance));
    }

    [Fact]
    public void An_unknown_hit_code_falls_back_to_the_height_delta()
    {
        // 테두리 클릭 없이 들어온 크기 조절(시스템 메뉴·터치 등): 방향을 모르니 실제 높이 변화로 판정한다.
        Assert.True(WindowResizePolicy.IsManualAfterDrag(WindowResizePolicy.HtUnknown, 416, 486));
        Assert.False(WindowResizePolicy.IsManualAfterDrag(WindowResizePolicy.HtUnknown, 416, 416));
    }

    [Fact]
    public void Auto_survives_any_number_of_width_drags()
    {
        bool manual = false;
        for (int i = 0; i < 5; i++)
        {
            manual = WindowResizePolicy.NextManual(manual, WindowResizePolicy.HtRight, 416, 416);
        }

        Assert.False(manual); // 자동 맞춤이 그대로 → 호출부가 SizeToContent=Height를 다시 켠다
    }

    [Fact]
    public void A_pinned_height_is_not_released_by_a_later_width_drag()
    {
        bool manual = WindowResizePolicy.NextManual(false, WindowResizePolicy.HtBottom, 416, 600);
        Assert.True(manual);

        manual = WindowResizePolicy.NextManual(manual, WindowResizePolicy.HtRight, 600, 600);
        Assert.True(manual); // 폭을 넓혔다고 사용자가 맞춰둔 600이 무너지면 안 된다
    }
}

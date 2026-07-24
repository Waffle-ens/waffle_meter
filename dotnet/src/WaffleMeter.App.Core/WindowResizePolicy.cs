namespace WaffleMeter.App.Core;

/// <summary>
/// Pure resize rules for the borderless overlay windows: which WM_NCHITTEST zone a point falls in, and
/// whether a finished drag means the user PINNED the meter's height. No WPF types, so the rules are covered
/// by the headless test suite.
///
/// <para><b>왜 필요한가 (실측):</b> WPF는 사용자가 창 크기 조절을 <b>시작하는 순간</b>
/// <c>SizeToContent</c>를 꺼버린다. PresentationCore의 <c>HwndSource.LayoutFilterMessage</c>가
/// <c>WM_SYSCOMMAND(SC_SIZE)</c>·<c>WM_SIZING</c>에서 <b>방향을 따지지 않고</b>
/// <c>DisableSizeToContent()</c>를 호출하기 때문에, <b>좌/우 가장자리만 끌어도</b> — 픽셀 이동이 0인
/// 클릭조차 — 높이 자동 맞춤이 죽는다. 따라서 "세로 핸들을 노출하지 않으면 <c>SizeToContent</c>가
/// 지켜진다"는 v2.8.1의 전제는 성립하지 않았고, 폭을 한 번 조절한 사용자는 그 세션 내내 행이 잘린
/// 미터를 봐야 했다(= 10인 공대 아래 행 잘림 제보).</para>
///
/// <para><b>대응:</b> 히트테스트로는 막을 수 없으므로, 드래그가 끝난 뒤 자동 맞춤을 <b>다시 켠다</b>.
/// 단 사용자가 세로/대각 핸들로 실제 높이를 바꿨다면 그건 의도한 고정이니 되돌리지 않는다.</para>
/// </summary>
public static class WindowResizePolicy
{
    /// <summary>WM_NCHITTEST 반환 코드. <see cref="HtUnknown"/>은 "이번 크기 조절이 어느 핸들에서
    /// 시작됐는지 모른다"(테두리 클릭 없이 들어온 SC_SIZE 등)는 뜻이다.</summary>
    public const int HtUnknown = 0, HtClient = 1;

    public const int HtLeft = 10, HtRight = 11, HtTop = 12, HtTopLeft = 13, HtTopRight = 14,
                     HtBottom = 15, HtBottomLeft = 16, HtBottomRight = 17;

    /// <summary>가장자리를 잘못 집었다가 그대로 뗀 경우(실측: 이동 0인 클릭도 SizeToContent를 끈다)를
    /// "높이 고정 의도"로 오인하지 않기 위한 여유(DIP).</summary>
    public const double HeightDeltaTolerance = 2.0;

    /// <summary>Which resize zone the point (<paramref name="x"/>, <paramref name="y"/> — DIPs from the
    /// window's top-left) falls in. <see cref="HtClient"/> means "not an edge", so the normal client
    /// hit-testing (drag handle, header buttons, rows) runs instead.</summary>
    public static int Resolve(double x, double y, double width, double height, double margin)
    {
        if (x < 0 || y < 0 || x > width || y > height)
        {
            return HtClient;
        }

        bool left = x <= margin, right = x >= width - margin;
        bool top = y <= margin, bottom = y >= height - margin;
        return (left, right, top, bottom) switch
        {
            (true, _, true, _) => HtTopLeft,
            (_, true, true, _) => HtTopRight,
            (true, _, _, true) => HtBottomLeft,
            (_, true, _, true) => HtBottomRight,
            (true, _, _, _) => HtLeft,
            (_, true, _, _) => HtRight,
            (_, _, true, _) => HtTop,
            (_, _, _, true) => HtBottom,
            _ => HtClient,
        };
    }

    /// <summary>높이를 바꿀 수 있는 핸들인가(상/하 + 네 모서리).</summary>
    public static bool IsVerticalHit(int hitCode) =>
        hitCode is HtTop or HtTopLeft or HtTopRight or HtBottom or HtBottomLeft or HtBottomRight;

    /// <summary>이번 드래그가 "사용자가 높이를 고정한" 드래그인가 — 세로 핸들(또는 출처를 알 수 없는
    /// 크기 조절)이면서 <b>실제로 높이가 변했을 때만</b> 참. 좌/우 핸들은 폭만 바꾸므로 절대 아니다.</summary>
    public static bool IsManualAfterDrag(int hitCode, double heightBefore, double heightAfter)
    {
        if (hitCode != HtUnknown && !IsVerticalHit(hitCode))
        {
            return false;
        }

        return Math.Abs(heightAfter - heightBefore) > HeightDeltaTolerance;
    }

    /// <summary>드래그가 끝난 뒤의 높이 모드. 한 번 고정하면 <b>폭 드래그로는 풀리지 않는다</b> —
    /// 풀리게 두면 사용자가 맞춰둔 높이가 폭을 조절하는 순간 무너진다. 자동으로 되돌리는 길은
    /// 앱 재시작뿐이다(높이는 저장하지 않는다).</summary>
    public static bool NextManual(bool wasManual, int hitCode, double heightBefore, double heightAfter) =>
        wasManual || IsManualAfterDrag(hitCode, heightBefore, heightAfter);
}

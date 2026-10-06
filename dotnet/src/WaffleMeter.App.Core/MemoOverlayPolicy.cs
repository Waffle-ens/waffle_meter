namespace WaffleMeter.App.Core;

/// <summary>
/// 메모 오버레이의 판단 규칙 — 언제 화면에 올리는가, 투명도 몇에서 머리줄을 지우는가, 잠긴 창을 언제
/// "엿보기"로 잠깐 눌리게 하는가. App.Wpf 에는 테스트 프로젝트가 없어서 결정은 전부 여기 두고 창은 배선만
/// 한다(<see cref="WindowResizePolicy"/> 와 같은 이유).
/// </summary>
public static class MemoOverlayPolicy
{
    /// <summary>이 값 이하의 투명도는 "최하"로 본다 — 머리줄(배경·테두리·제목·닫기)을 지우고 슬라이더와
    /// 글자만 남긴다. 0 과의 정확 비교가 아닌 이유: 슬라이더를 끝까지 끌면 0 이 오지만 키보드·설정 코드는
    /// 부동소수 찌꺼기를 남길 수 있고, 0.5% 배경은 어차피 눈에 안 보인다.</summary>
    public const double ChromeHiddenAtOrBelow = 0.005;

    /// <summary>
    /// 메모를 지금 화면에 올릴지.
    /// <para><paramref name="foregroundOk"/> 는 컨트롤러가 내는 <b>게임 포그라운드 축만</b>의 판정이다(트레이
    /// 숨김과 무관). <paramref name="meterVisible"/> 은 사용자가 Ctrl+H·트레이로 미터를 숨겼는지다.
    /// "미터를 숨겨도 메모 유지"(<paramref name="keepWhenMeterHidden"/>)는 <b>뒤쪽 축만</b> 풀어 준다 —
    /// 게임이 비활성이면 그래도 숨는다. 기존 '미터를 숨겨도 오버레이 유지'(버프·쿨타임)와 같은 축이다.</para>
    /// <para>편집 중이면 무조건 띄운다. 편집하는 동안은 메모 자신이 포그라운드라, 그 순간의 판정이 어떻든
    /// 입력 중인 창을 사용자 손 밑에서 치우면 안 된다. 단 <paramref name="show"/> 를 끈 것은 이긴다.</para>
    /// </summary>
    public static bool IsVisible(bool show, bool foregroundOk, bool keepWhenMeterHidden, bool meterVisible, bool editing) =>
        show && (editing || (foregroundOk && (keepWhenMeterHidden || meterVisible)));

    /// <summary>투명도 0..1 로 정규화. NaN(손으로 고친 설정 파일)은 기본값으로 돌린다 — <c>Math.Clamp</c> 는
    /// NaN 을 그대로 통과시키고, 그 값이 브러시에 들어가면 배경이 조용히 사라진다.</summary>
    public static double ClampOpacity(double opacity, double fallback) =>
        double.IsNaN(opacity) ? fallback : Math.Clamp(opacity, 0.0, 1.0);

    /// <summary>투명도 최하인가 — 머리줄을 지우고 슬라이더·글자만 남기는 상태.</summary>
    public static bool IsChromeHidden(double opacity) => opacity <= ChromeHiddenAtOrBelow;

    /// <summary>
    /// 잠금 버튼을 보일지. 평소엔 항상 보이고, 투명도 최하에서는 <b>Ctrl 을 누른 채 커서가 메모 위에 있을
    /// 때만</b> 보인다. 최하에서 아예 숨기면 잠긴 메모를 풀 대상이 화면에서 사라진다.
    /// </summary>
    public static bool ShowLockButton(bool chromeHidden, bool ctrlHover) => !chromeHidden || ctrlHover;

    /// <summary>
    /// 잠긴 창의 클릭 통과(WS_EX_TRANSPARENT)를 지금 잠깐 걷을지 — "엿보기".
    /// <para>잠긴 창은 클릭 자체를 받지 못하므로(통과 창에는 WM_LBUTTONDOWN 이 오지 않는다) 클릭 <b>전에</b>
    /// 걷어야 한다. 조건은 Ctrl 이 눌려 있고 커서가 <b>잠금 버튼 사각형 안</b>일 때뿐이다 — 메모 다른 곳의
    /// Ctrl+클릭은 그대로 게임으로 간다.</para>
    /// <para>이미 엿보는 중에 마우스 버튼이 눌려 있으면 유지한다. 누른 채 Ctrl 을 먼저 떼거나 커서가 1px
    /// 빠졌다고 통과를 되살리면, 그 클릭의 뗌(Up)이 게임으로 새어 나가 게임 쪽에서 반쪽 클릭이 된다.</para>
    /// </summary>
    public static bool ShouldPeek(bool locked, bool ctrlDown, bool cursorInLockButton, bool wasPeeking, bool mouseButtonDown) =>
        locked && ((ctrlDown && cursorInLockButton) || (wasPeeking && mouseButtonDown));

    /// <summary>
    /// 40ms 엿보기 폴을 돌릴 필요가 있는가. 잠긴 동안(엿보기 판정), 그리고 투명도 최하인 동안(Ctrl+호버로
    /// 잠금 버튼을 꺼내 보여야 한다). 둘 다 아니면 돌 이유가 없다 — 잠금 버튼은 그냥 보이고, 창은 평범하게
    /// 클릭을 받는다.
    /// <para>NOACTIVATE 창의 UI 스레드는 키보드 메시지를 받지 못해 WPF <c>Keyboard.Modifiers</c> 가 갱신되지
    /// 않는다. 그래서 이벤트가 아니라 폴(GetAsyncKeyState)이다. 저수준 훅(WH_*_LL)은 GameGuard 위험으로 쓰지
    /// 않는다.</para>
    /// </summary>
    public static bool NeedsPeekPoll(bool locked, bool chromeHidden) => locked || chromeHidden;

    /// <summary>
    /// 오버레이 안 편집을 끝낼 때 포그라운드를 편집 전 창(대개 게임)으로 돌려줄지.
    /// <para>Esc·Alt+F4(<paramref name="requested"/>)면 돌려준다. 비활성화(<paramref name="deactivated"/> — 바깥
    /// 클릭)로 끝났으면 돌려주지 않는다 — 포그라운드는 이미 사용자가 고른 곳이라 되돌리면 가로채기다.</para>
    /// <para>그 밖의 경로는 메모가 <b>아직 포그라운드일 때만</b> 돌려준다. 메모 자신의 잠금 버튼·✕ 는 활성 창
    /// <b>안의</b> 클릭이라 비활성화가 일어나지 않는다 — NOACTIVATE 를 되세우거나 투명도 0·클릭 통과로 바꿔도
    /// 활성 상태는 그대로다. 그대로 두면 안 보이는(✕) 또는 클릭이 통과하는(잠금) 메모가 활성 창으로 남아 게임은
    /// 배경(FPS 하락)이고 키 입력은 메모로 가며, 이어진 Alt+F4 가 편집이 이미 끝난 메모 창을 닫아 세션 내내
    /// 되살릴 수 없다.</para>
    /// <para>⚠️ 비활성화 경로를 "아직 포그라운드인가"로 가리면 안 된다(실측): 같은 UI 스레드의 다른 창(설정
    /// 창)이 활성화될 때 Deactivated 안에서 GetForegroundWindow 는 <b>아직 메모</b>다 — 비활성화 메시지가 활성
    /// 창 교체보다 먼저 온다. 그 값을 믿으면 설정 창을 누른 사용자에게서 포커스를 게임으로 빼앗는다.</para>
    /// <para>편집 전 창이 없거나 사라졌으면(<paramref name="previousAlive"/>) 돌려줄 곳이 없다.</para>
    /// </summary>
    public static bool ShouldRestoreForeground(bool requested, bool deactivated, bool memoStillForeground, bool previousAlive) =>
        previousAlive && (requested || (!deactivated && memoStillForeground));
}

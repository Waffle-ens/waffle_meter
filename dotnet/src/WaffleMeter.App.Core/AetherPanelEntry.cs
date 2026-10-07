namespace WaffleMeter.App.Core;

/// <summary>컨텐츠 관리 패널의 탭. 위에서부터 컨텐츠(오드·주간 성역·어비스 회랑) / 재화 관리(키나·포인트·서버별 총 키나).</summary>
public enum AetherPanelTab
{
    Content,
    Currency,
}

/// <summary>진입점 하나를 눌렀을 때 패널이 할 일.</summary>
public enum AetherPanelAction
{
    /// <summary>닫혀 있었다 — 요청한 탭으로 연다(자리 잡기·목록 새로 읽기 포함).</summary>
    Open,

    /// <summary>열려 있는데 다른 탭을 보고 있었다 — 창은 그대로 두고 탭만 바꾼다.</summary>
    Switch,

    /// <summary>요청한 탭이 이미 떠 있었다 — 닫는다.</summary>
    Close,
}

/// <summary>
/// 컨텐츠 관리 패널의 진입 규칙. 진입점은 넷이다 — 미터 하단 오드 배지·트레이 메뉴·'컨텐츠 관리' 단축키는
/// 컨텐츠 탭을, '재화 관리' 단축키는 재화 관리 탭을 요청한다. App.Wpf 에는 테스트 프로젝트가 없어서
/// 결정은 여기 두고 App 은 배선만 한다(<see cref="MemoOverlayPolicy"/> 와 같은 이유).
/// <para>규칙은 하나다: <b>자기 탭이 떠 있으면 닫고, 아니면 자기 탭을 보여 준다.</b> 탭이 생기기 전 배지·
/// 단축키는 "열려 있으면 닫기"였고, 컨텐츠 탭에서는 지금도 그대로다. 달라지는 건 재화 관리 탭을 보던 중에
/// 배지를 누른 경우뿐인데, 그때 패널을 닫아 버리면 배지가 가리키는 오드 목록을 보려던 사람이 한 번 더
/// 눌러야 한다 — 재화 관리 단축키가 컨텐츠 탭 위에서 탭을 바꾸는 것과 같은 이유로 바꿔 보여 준다.</para>
/// </summary>
public static class AetherPanelEntry
{
    public static AetherPanelAction Decide(bool visible, AetherPanelTab shown, AetherPanelTab requested) =>
        !visible ? AetherPanelAction.Open
        : shown == requested ? AetherPanelAction.Close
        : AetherPanelAction.Switch;
}

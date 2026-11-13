namespace WaffleMeter.App.Wpf;

/// <summary>
/// 인게임 <b>버프</b> 아이콘(클라 SkillAbnormal.AbnormalIcon)이 그 버프를 준 스킬의 아이콘과 다른 버프들.
/// 버프 오버레이 슬롯과 버프 picker 행이 <see cref="JoinIcons.BuffSlot"/>로 이 묶음을 먼저 보고, 없으면 스킬
/// 아이콘(<see cref="SkillIconManifest"/>)으로 떨어진다. 쿨타임·스킬 화면은 이 묶음을 보지 않는다 — 같은 base
/// 코드가 스킬 아이콘과 버프 아이콘을 동시에 갖기 때문이다.
///
/// <para>키는 런타임 어노멀 코드, 값은 그 코드의 표시 base(= <c>Assets/BuffIcons/&lt;값&gt;.png</c> 파일명)다.
/// 키를 base가 아니라 런타임 코드로 두는 이유: 같은 base로 접히는 디버프가 버프 아이콘을 받으면 안 된다. 지금
/// 그리는 쪽(오버레이·picker)은 값 집합만 쓰지만, 코드 단위로 그리는 화면을 붙일 때 이 구분이 그대로 필요하다.
/// 값은 <c>DataManager.BuffDisplayBase(키)</c>와 같아야 하고, 값 집합과 PNG 집합은 같아야 한다
/// (ShippedBuffIconAssetsTests).</para>
///
/// <para>PNG는 48x48(데이터마인 256x256을 LANCZOS로 줄인 것). 출처는 2026-11-11 클라 텍스처다. 출하 스킬
/// 아이콘을 덤프 기준으로 일괄 교체하지 말 것 — 인게임과 대조해 맞춘 것이 섞여 있다.</para>
///
/// <para><b>주의 — 아래 초기화자 안의 주석에 7~9자리 숫자를 쓰지 마세요.</b> 테스트가 초기화자부터 파일
/// 끝까지의 대괄호 쌍을 전부 매핑으로 읽습니다. 이 doc 주석에는 초기화자 선언 형태를 그대로 적지 마세요.</para>
/// </summary>
internal static class BuffIconManifest
{
    public static readonly Dictionary<int, int> ByAbnormal = new()
    {
        [111000071] = 11100000, // 광분
        [122400502] = 12240052, // 심판의 권능
        [131300071] = 13130000, // 살의
        [131300371] = 13130000, // 살의
        [140800071] = 14080000, // 감응
        [151100301] = 15110000, // 겨울의 파편
        [190900001] = 19090000, // 투기
    };

    /// <summary>버프 아이콘 PNG가 있는 표시 base.</summary>
    public static readonly HashSet<int> Bases = new(ByAbnormal.Values);
}

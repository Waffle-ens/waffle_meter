namespace WaffleMeter.Capture;

/// <summary>
/// 전투력(combat power) 값의 타당성 기준 한 곳. 전투력은 서로 다른 네 경로로 들어오는데
/// (0x3656 본인 브로드캐스트 · 0x3645 스냅샷 마커 스캔 · 0x9702 로스터 바이트 스캔 · 공식 사이트 조회)
/// 앞의 셋은 전부 <b>휴리스틱</b>이라 오프셋을 잘못 잡으면 그냥 "그 자리에 있던 u32"를 전투력으로 삼는다.
/// 그래서 상한이 유일한 방어선인데, 종전 값 10,000,000은 실측 최댓값의 10배가 넘어 사실상 게이트가
/// 아니었다.
/// <para>근거(2026-08-17 코퍼스, 약 12시간 · 서로 다른 캐릭터 577 표본 — 파티원뿐 아니라 도시에 몰려 있던
/// 낯선 유저 35명을 포함한다): 관측된 최대 전투력은 <b>979,329</b>, 1,000,000을 넘는 표본은 <b>0건</b>.
/// 여기에 2배 여유를 둔 값이 아래 상한이다.</para>
/// <para>⚠️ 이 게이트는 fail-closed다 — 게임이 전투력 인플레로 상한을 넘기면 그 값은 <b>버려지고</b>
/// 전투력이 0으로 남는다. 그건 "틀린 전투력"보다 낫다: 0이면 공식 사이트 조회가 정상값으로 채우고
/// (<c>DataManager.ApplyOfficialCharacterInfo</c>), 티어는 표본 밖으로 처리돼 아무것도 표시하지 않는다.
/// 반대로 틀린 값은 배지·티어 구간·통계 업로드(<c>characters.latest_power</c>)까지 조용히 오염시킨다.
/// 상한에 걸리면 파서가 <c>ParserError</c> 흔적을 남기므로 다음 패치 때 바로 보인다.</para>
/// </summary>
public static class CombatPower
{
    /// <summary>받아들일 최댓값. 위 주석의 실측 근거 참조.</summary>
    public const int Max = 2_000_000;

    /// <summary>0/음수와 상한 초과를 모두 막는다. long 오버로드인 이유는 스캔 계열이 u32를 long으로
    /// 읽어 오기 때문(int 캐스팅 전에 걸러야 음수 랩어라운드가 안 생긴다).</summary>
    public static bool IsPlausible(long value) => value is >= 1 and <= Max;
}

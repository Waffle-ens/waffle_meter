namespace WaffleMeter.Capture;

/// <summary>
/// 파티 던전 '1인 모드'(클라 134, 2026-11-25 패치)에 입장하면 서버가 거는 버프 10종.
///
/// <para><b>왜 버프로 가려야 하나.</b> 1인 모드는 파티 판과 <b>같은 dungeonId·같은 보스 mobCode</b>로 진행된다
/// (대상 15던전: 원정 보통·어려움 11행 + 초월 가라앉은 생명의 신전 v2 1~4단계). 그래서 인카운터 카탈로그로는
/// 둘을 가를 수 없고, 134부터는 일반 입장도 인원과 무관하게 파티 보정으로 고정돼 <c>partySize == 1</c> 이 1인
/// 모드를 뜻하지도 않는다. 남는 단서는 입장 버프뿐이다.</para>
///
/// <para><b>왜 전용 가로채기인가.</b> <c>ParseBuffPacket</c> 의 코드 대역 게이트(110,000,000~199,999,999 /
/// 20,000,000~29,999,999)가 1743 과 19996x01 을 모두 버리고, 직업 버프는 지속시간이 무기한이라 무기한 게이트가
/// 한 번 더 버린다. <c>buff.json</c> 에 행을 추가하는 것만으로는 아무것도 잡히지 않는다.
/// <see cref="TrialAffixCatalog"/>·<see cref="AbyssArtifactBuffCatalog"/> 와 같은 모양이고 이유도 같다 —
/// 코드 자체가 값이라 디코딩할 필드가 없다. 버프 저장소로도 보내지 않는다: 무기한 상시 버프라 오버레이를
/// 어지럽히기만 하고, StatConvert(본인 스탯 비례)라 버프 이득 모델에도 넣을 수 없다.</para>
///
/// <para>⚠️ <b>라이브 미검증.</b> 출처는 클라 데이터(Dungeon <c>New134_Dungeon_54</c> 입장 버프 목록, 직업 버프는
/// 시스템 스킬 1999640~1999720 이 Self 로 거는 것)다. 0x382A 로 오는지, 둘 다 오는지는 패치 후 첫 1인 모드
/// 캡처에서 확인해야 한다.</para>
/// </summary>
public static class SoloModeBuffCatalog
{
    /// <summary>군림하는 데바 — 1인 모드 던전 공통 입장 버프.</summary>
    public const int ReigningDaevaCode = 1743;

    /// <summary>직업별 1인 모드 버프: 19996401(검성) ~ 19997201(권성), 100 간격.</summary>
    private const int JobFirstCode = 19_996_401;
    private const int JobLastCode = 19_997_201;
    private const int JobStep = 100;

    /// <summary>이 코드가 1인 모드 입장 버프인가.</summary>
    public static bool IsSoloModeBuff(long skillCode) =>
        skillCode == ReigningDaevaCode
        || (skillCode >= JobFirstCode && skillCode <= JobLastCode && (skillCode - JobFirstCode) % JobStep == 0);
}

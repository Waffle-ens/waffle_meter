namespace WaffleMeter.Data;

/// <summary>
/// 지금 인스턴스가 파티 던전 '1인 모드'(클라 134, 2026-11-25)인지. 입장 버프
/// (<c>SoloModeBuffCatalog</c>)를 봤는지와, 그 버프가 어느 인스턴스에 속하는지를 들고 있다.
///
/// <para><b>판정이 버프 하나에 달린 이유.</b> 1인 모드는 파티 판과 같은 dungeonId·같은 보스 mobCode로
/// 진행되고, 134부터 일반 입장은 인원과 무관하게 파티 보정이라 <c>partySize == 1</c> 도 증거가 아니다.</para>
///
/// <para><b>런 경계.</b> 버프는 무기한이고 해제 패킷이 없어서 "1인 모드가 끝났다"는 신호가 따로 없다. 그래서
/// 인스턴스 phase 창(0x6100)으로 <b>새 입장</b>을 알아채고, 그 입장 근처에서 버프를 못 봤으면 지운다.
/// 새 입장 = 맵이 바뀌었거나, 같은 맵에서 phase 번호가 앞으로 가지 않았는데 시작 시각이 바뀐 경우
/// (재입장·재풀 — 진행 중 1→2→3 은 입장이 아니다). 버프와 phase 창의 도착 순서는 정해져 있지 않으므로
/// <see cref="EntryToleranceMs"/> 안에 든 버프는 그 입장의 것으로 본다.</para>
///
/// <para><b>틀리는 방향.</b> 의심스러우면 끈다. 1인 모드를 놓치면 이 기능 전과 같은 상태(파티 전투로 취급)가
/// 되지만, 파티 전투를 1인 모드로 찍으면 등급 배지가 사라지고 웹 집계에서 빠질 수 있다.
/// 필드·마을에는 phase 창이 없어 이 추적기가 지워지지 않으므로, 전투에 붙일지는 반드시
/// <see cref="AppliesTo"/> 로 그 전투의 던전을 대조해 정한다.</para>
/// </summary>
public sealed class SoloModeTracker
{
    /// <summary>버프와 phase 창 사이에 허용하는 시차. 둘은 같은 로딩에서 몇 초 안에 오지만 순서가 정해져 있지
    /// 않다(아티팩트 어보노멀은 로딩 0.3초 뒤에 왔다). 넉넉히 잡되, 한 런(수 분)보다는 짧게.</summary>
    public const long EntryToleranceMs = 60_000;

    /// <summary>1인 모드가 허용된 dungeonId 15개 — 클라 134 Dungeon 표의 <c>New134_Dungeon_53</c> = True 행.
    /// 원정 보통·어려움 11행 + 초월 가라앉은 생명의 신전 v2 1~4단계. 같은 던전의 쉬움·시련, 성역은 없다.
    /// <para>⚠️ 패치로 대상이 늘면 여기 <b>추가</b>한다.</para></summary>
    public static readonly IReadOnlySet<int> EligibleDungeonIds = new HashSet<int>
    {
        600012, 600022, 600032, 600122, 600142, 600152, 600153, 600162, 600163, 600192, 600193,
        610014, 610015, 610016, 610017,
    };

    private readonly object _gate = new();
    private bool _active;
    private long _seenAtMs;

    /// <summary>마지막 phase 창의 맵. 0 = 아직 못 봤다.</summary>
    private int _mapId;
    private int _lastPhase;
    private long _lastPhaseStartMs;

    /// <summary>1인 모드 입장 버프를 봤다.</summary>
    public void ObserveBuff(long atMs)
    {
        lock (_gate)
        {
            _active = true;
            _seenAtMs = atMs;
        }
    }

    /// <summary>인스턴스 phase 창을 봤다. 새 입장이면, 그 근처에서 버프를 못 본 1인 모드 판정을 지운다.</summary>
    public void ObservePhaseWindow(int mapId, int phase, long startMs, long atMs)
    {
        if (mapId <= 0)
        {
            return;
        }

        lock (_gate)
        {
            bool entry = mapId != _mapId || (phase <= _lastPhase && startMs != _lastPhaseStartMs);
            _mapId = mapId;
            _lastPhase = phase;
            _lastPhaseStartMs = startMs;

            if (entry && _active && Math.Abs(atMs - _seenAtMs) > EntryToleranceMs)
            {
                _active = false;
            }
        }
    }

    /// <summary>1인 모드 버프를 본 상태인가(던전 대조 전).</summary>
    public bool Active
    {
        get
        {
            lock (_gate)
            {
                return _active;
            }
        }
    }

    /// <summary>
    /// <paramref name="dungeonId"/> 의 전투를 1인 모드로 볼 것인가. 버프를 봤고, 그 던전이 1인 모드 대상이고,
    /// 지금 인스턴스 맵을 안다면 그 맵이 바로 이 던전이어야 한다.
    /// <para>던전 대조가 없으면 1인 모드를 마치고 나와 필드 보스를 잡을 때 그 전투에 플래그가 샌다 — 필드엔
    /// phase 창이 없어 추적기가 지워지지 않는다.</para>
    /// </summary>
    public bool AppliesTo(int dungeonId)
    {
        if (!EligibleDungeonIds.Contains(dungeonId))
        {
            return false;
        }

        lock (_gate)
        {
            return _active && (_mapId == 0 || _mapId == dungeonId);
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _active = false;
            _seenAtMs = 0;
            _mapId = 0;
            _lastPhase = 0;
            _lastPhaseStartMs = 0;
        }
    }
}

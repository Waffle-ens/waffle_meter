using System.Globalization;
using WaffleMeter.Capture;

namespace WaffleMeter.App.Core;

/// <summary>One 감시자 카이라 개체의 출현 일정 — 클라 <c>PeriodSpawn</c> 표의 한 행을 그대로 옮긴 것이다
/// (<c>Time</c> = 그날 첫 출현 시각, <c>Cycle</c> = 주기).</summary>
public readonly record struct KairaSpawner(int Code, int FirstHourKst, int IntervalHours);

/// <summary>
/// The 감시자 카이라 (어비스 하층) reminder. This boss is the one field boss the server never times: its
/// 0x9101 record arrives with a zeroed timestamp in every capture we have, so it cannot hang off the
/// respawn-timer alarm at all. It gets a clock of its own, and that clock fires wherever you are rather than
/// only inside the abyss — the whole point is to travel there before it spawns.
/// <para><b>2026-09-02 인게임 패치.</b> 출현 확률 20% → <b>100%</b> 확정. 종전의 "매시 정각, 나올 수도 안 나올
/// 수도"가 아니다. 헛걸음이 없어졌으므로 알림 문구에서도 확률성("출현 가능")을 걷어냈다.</para>
/// <para><b>⚠️ 2026-09-22 정정 — 인게임 문구는 시각의 근거가 아니다.</b> 패치 문구가 "0시를 기준으로
/// 4시간마다"였고 그걸 그대로 믿어 00·04·08·12·16·20시로 넣었는데, 실제 출현은 01·05·09·13·17·21시였다
/// (사용자 제보). 알림이 3주간 매번 <b>한 시간 일찍</b> 울렸다.</para>
/// <para><b>2026-11-11 (클라 125) — 일정이 바뀌었고, 이제 정본이 있다.</b> 클라 <c>PeriodSpawn</c> 표가 이
/// 보스의 출현을 적는다. 직전 패치의 값이 <c>M_AR1_BeritraD_01_001</c> 01:00 / 240분으로 위의 제보 교정과
/// 정확히 일치했고(= 표를 믿을 근거), 이번 패치에서 그 행이 <b>720분</b>이 되면서 둘째 개체
/// <c>M_AR1_BeritraD_01_003</c>(2600099) 09:00 / 720분이 생겼다. 그래서 출현은 <b>01·13시(첫째) +
/// 09·21시(둘째) = 01·09·13·21시</b>이고 05·17시는 더 이상 없다. 간격이 8·4·8·4시간으로 고르지 않으므로
/// "앵커 + 주기" 한 쌍으로는 못 적는다 — <see cref="Spawners"/> 가 표의 두 행을 그대로 들고, 시각 목록·
/// 알림·설정 화면 문구가 전부 거기서 파생된다. 다음에 또 바뀌면 <b>그 표만</b> 고쳐라.</para>
/// <para><b>격자는 머신 로컬이 아니라 고정 +09:00 에 건다.</b> <c>WeeklyContentReset</c>·
/// <c>FieldBossFixedSchedule</c> 과 같은 논리다 — 한국 서버의 콘텐츠 일정은 PC 시간대가 달라도 움직이지
/// 않고, 한국엔 DST 가 없어 고정 오프셋이 tz 데이터베이스와 정확히 동등하다. 매시 정각이던 시절엔 분
/// (minute)만 봐서 정수 오프셋 시간대에선 우연히 맞았지만 <b>시(hour)가 정해진 일정은 그렇지 않다</b>:
/// 로컬 시로 재면 UTC+8(중국·홍콩·대만·싱가포르)은 모든 슬롯이 한 시간 어긋나고, DST 가 있는 지역은
/// 전환일에 사용자가 아무것도 안 했는데 조용히 고장난다. (슈고 페스타는 여전히 매시 정각이라 사용자
/// 벽시계를 쓰는 <see cref="HourlyAlarm"/> 그대로다.)</para>
/// </summary>
public static class KairaAlarm
{
    private static readonly TimeSpan Kst = TimeSpan.FromHours(9);

    private const int DayMinutes = 24 * 60;

    /// <summary>개체별 출현 일정 — 클라 125 <c>PeriodSpawn</c> 의 <c>FieldBoss_AR1_CycleBoss_01</c>(01:00,
    /// Cycle 720) 과 <c>FieldBoss_AR1_CycleBoss_02</c>(09:00, Cycle 720). ⚠️ 이 표가 유일한 정본이다.
    /// <para>주기는 24시간을 나누어떨어뜨려야 한다(그래야 날마다 같은 시각이 된다). 표가 그런 값만 쓰고
    /// 있고, 어긋나는 값이 들어오면 테스트가 잡는다.</para></summary>
    public static IReadOnlyList<KairaSpawner> Spawners { get; } =
    [
        new(FieldBossCatalog.ScheduledSpawnCode, FirstHourKst: 1, IntervalHours: 12),
        new(FieldBossCatalog.ScheduledSpawnTwinCode, FirstHourKst: 9, IntervalHours: 12),
    ];

    /// <summary>출현 정각(KST 시)을 오름차순으로 — 두 개체를 합친 1·9·13·21. 설정 화면 문구와 테스트가
    /// 이걸 읽는다. ⚠️ <see cref="Spawners"/> <b>아래</b>에 둘 것: 정적 초기화식은 소스 순서로 돈다.</summary>
    public static IReadOnlyList<int> SpawnHoursKst { get; } = BuildSpawnHours();

    /// <summary>설정 화면에 그대로 박히는 시각 목록("1·9·13·21"). 화면 문구를 손으로 적지 않기
    /// 위한 것이다 — 그렇게 두면 일정이 움직였을 때 로직만 고치고 문구는 옛 시각을 계속 주장한다.
    /// <para>일부러 <b>계산 속성</b>이다(초기화식이 아니라). 정적 초기화식은 소스 순서로 도는데, 누가 문서를
    /// 정리하며 이 줄을 <see cref="SpawnHoursKst"/> 위로 올리면 그때 null 이라 <c>TypeInitializationException</c>
    /// 이 나고, 그게 터지는 자리는 앱 기동이 아니라 <c>AlarmController.Poll()</c> — UI 스레드의 1초 타이머다.</para></summary>
    public static string SpawnHoursText =>
        string.Join("·", SpawnHoursKst.Select(h => h.ToString(CultureInfo.InvariantCulture)));

    /// <summary>The lead minutes enabled in settings (10 / 5 / 1 minutes before the spawn).</summary>
    public static IReadOnlyCollection<int> EnabledLeads(MeterSettings s)
    {
        var leads = new HashSet<int>();
        if (s.KairaLead10)
        {
            leads.Add(10);
        }

        if (s.KairaLead5)
        {
            leads.Add(5);
        }

        if (s.KairaLead1)
        {
            leads.Add(1);
        }

        return leads;
    }

    /// <summary>
    /// The lead (minutes before the next <see cref="SpawnHoursKst"/> spawn) that is due exactly at
    /// <paramref name="nowMs"/>, or null when none of <paramref name="enabledLeads"/> matches this minute.
    /// At most one lead can be due in any given minute.
    /// <para>Unix ms rather than a <see cref="DateTime"/> on purpose: a <c>DateTime</c> whose <c>Kind</c> is
    /// <c>Unspecified</c> gets read as LOCAL time, so a test written that way would answer one thing on a KST
    /// box and another on a UTC one — and this repo runs no tests in CI, so that drift would never surface.</para>
    /// </summary>
    public static int? DueLead(long nowMs, IReadOnlyCollection<int> enabledLeads)
    {
        int untilSpawn = MinutesUntilSpawn(nowMs);
        return enabledLeads.Contains(untilSpawn) ? untilSpawn : null;
    }

    /// <summary>The next spawn instant at or after <paramref name="nowMs"/>, truncated to the minute so the
    /// toast can print an exact HH:mm. Callers render it in the user's own local time — the schedule is
    /// anchored to KST, but what the user reads off their clock is their own wall time.
    /// <para>날짜를 만지지 않고 "이번 분 + 남은 분"으로 더하므로 자정을 넘는 슬롯(21시 다음 = 익일 1시)도
    /// 그대로 맞는다.</para></summary>
    public static long NextSpawnMs(long nowMs)
    {
        long thisMinute = nowMs - (nowMs % 60_000L);
        return thisMinute + (MinutesUntilSpawn(nowMs) * 60_000L);
    }

    // 슬롯 간격이 고르지 않으므로(8·4·8·4시간) 나머지 연산이 아니라 목록을 훑는다: 오늘 남은 슬롯 중 가장
    // 이른 것, 없으면 내일의 첫 슬롯. 슬롯이 하루에 하나라도 있으면 답은 언제나 24시간 안에 있다.
    private static int MinutesUntilSpawn(long nowMs)
    {
        DateTimeOffset kst = DateTimeOffset.FromUnixTimeMilliseconds(nowMs).ToOffset(Kst);
        int nowMinutes = (kst.Hour * 60) + kst.Minute;
        foreach (int hour in SpawnHoursKst)
        {
            int slot = hour * 60;
            if (slot >= nowMinutes)
            {
                return slot - nowMinutes;
            }
        }

        return (SpawnHoursKst[0] * 60) + DayMinutes - nowMinutes;
    }

    private static int[] BuildSpawnHours()
    {
        var hours = new SortedSet<int>();
        foreach (KairaSpawner s in Spawners)
        {
            // 첫 출현 시각을 주기로 접어 그 개체의 하루 첫 슬롯부터 센다(첫 시각이 주기보다 커도 같은 목록).
            for (int h = s.FirstHourKst % s.IntervalHours; h < 24; h += s.IntervalHours)
            {
                hours.Add(h);
            }
        }

        return [.. hours];
    }
}

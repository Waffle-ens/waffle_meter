namespace WaffleMeter.App.Core;

/// <summary>
/// The 감시자 카이라 (어비스 하층) reminder. This boss is the one field boss the server never times: its
/// 0x9101 record arrives with a zeroed timestamp in every capture we have, so it cannot hang off the
/// respawn-timer alarm at all. It gets a clock of its own, and that clock fires wherever you are rather than
/// only inside the abyss — the whole point is to travel there before it spawns.
/// <para><b>2026-09-02 인게임 패치.</b> 출현 확률 20% → <b>100%</b>, 주기는 "0시를 기준으로 4시간마다" —
/// 즉 <b>00·04·08·12·16·20시 정각 확정 출현</b>이다. 종전의 "매시 정각, 나올 수도 안 나올 수도"가 아니다.
/// 헛걸음이 없어졌으므로 알림 문구에서도 확률성("출현 가능")을 걷어냈다.</para>
/// <para><b>격자는 머신 로컬이 아니라 고정 +09:00 에 건다.</b> <c>WeeklyContentReset</c>·
/// <c>FieldBossFixedSchedule</c> 과 같은 논리다 — 한국 서버의 콘텐츠 일정은 PC 시간대가 달라도 움직이지
/// 않고, 한국엔 DST 가 없어 고정 오프셋이 tz 데이터베이스와 정확히 동등하다. 매시 정각이던 시절엔 분
/// (minute)만 봐서 정수 오프셋 시간대에선 우연히 맞았지만 <b>4시간 격자는 그렇지 않다</b>: 로컬 시로
/// 재면 UTC+8(중국·홍콩·대만·싱가포르)은 여섯 슬롯이 전부 한 시간 어긋나고, DST 가 있는 지역은 전환일에
/// 사용자가 아무것도 안 했는데 조용히 고장난다. (슈고 페스타는 여전히 매시 정각이라 사용자 벽시계를 쓰는
/// <see cref="HourlyAlarm"/> 그대로다.)</para>
/// </summary>
public static class KairaAlarm
{
    private static readonly TimeSpan Kst = TimeSpan.FromHours(9);

    /// <summary>출현 주기(시간). KST 0시를 기준으로 이 간격마다 정각 출현 — 00·04·08·12·16·20시.</summary>
    public const int SpawnIntervalHours = 4;

    private const int CycleMinutes = SpawnIntervalHours * 60;

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
    /// The lead (minutes before the next KST 00/04/08/12/16/20 spawn) that is due exactly at
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
    /// toast can print an exact HH:mm. Callers render it in the user's own local time — the grid is anchored
    /// to KST, but what the user reads off their clock is their own wall time.</summary>
    public static long NextSpawnMs(long nowMs)
    {
        long thisMinute = nowMs - (nowMs % 60_000L);
        return thisMinute + (MinutesUntilSpawn(nowMs) * 60_000L);
    }

    // HourlyAlarm.DueLead 의 `Minute == 0 ? 0 : 60 - Minute` 를 cycle=240 으로 일반화한 것. 공유하지 않고
    // 일부러 복제했다 — 하나로 합치면 다음 사람이 이 4시간 격자를 슈고 페스타 쪽으로 흘리게 된다.
    // 1440 % CycleMinutes == 0 이라 자정에서 격자가 끊기지 않는다.
    private static int MinutesUntilSpawn(long nowMs)
    {
        DateTimeOffset kst = DateTimeOffset.FromUnixTimeMilliseconds(nowMs).ToOffset(Kst);
        int intoCycle = ((kst.Hour * 60) + kst.Minute) % CycleMinutes;
        return intoCycle == 0 ? 0 : CycleMinutes - intoCycle;
    }
}

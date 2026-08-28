using WaffleMeter.Capture;

namespace WaffleMeter.App.Core;

/// <summary>Which abyss layer a corridor's artifact sits on. The client only wires two — the middle layer was
/// re-themed from AR2 to AR3 and the surface never shipped.</summary>
public enum AbyssCorridorTier
{
    /// <summary>하층 (Reshanta_A / AR1). Entry gate: 아이템 레벨 1000.</summary>
    Lower = 0,

    /// <summary>중층 (Reshanta_C / AR3). Entry gate: 아이템 레벨 3000.</summary>
    Middle = 1,

    /// <summary>거점 회랑 — the fallback corridor a losing faction gets. Ticket rows exist but no map,
    /// entrance or dungeon row references them, so this has never been observed live.</summary>
    Stronghold = 2,
}

/// <summary>One 어비스 회랑 as the 컨텐츠 관리 panel presents it.</summary>
/// <param name="TicketId">The client's <c>ContentsTicket.ID</c>, which IS the wire currency id. Used as the
/// PERSISTED key — unlike the weekly raids' hand-written slugs it comes from the game's own table, so there is
/// no rename that could orphan a user's records.</param>
/// <param name="Tier">하층 / 중층.</param>
/// <param name="Name">The artifact whose capture grants this corridor.</param>
/// <param name="ShortName">What fits on a chip.</param>
/// <param name="MapIds">Instance map ids for this corridor, 503xxx then 504xxx. Both races share one ticket.
/// <para>The client tags 503xxx <c>ERace::Light</c> and 504xxx <c>ERace::Dark</c>, but that is a CLIENT tag and
/// not what comes over the wire: a 마족 install (server 2003, seen standing in map 905 = <c>ERace::Dark</c>) was
/// sent 503001/503004/503006 on two separate days, and 504xxx has never arrived. Do not read a character's
/// 진영 off the map id — the server id is where that lives (<see cref="MeterFormat.ServerTier"/>).</para></param>
/// <param name="ArtifactId">The artifact whose capture opens this corridor, as the 점령 현황 broadcast
/// (0xE305/0xE307) names it: <c>1001~1003</c> for 하층 and <c>2001~2003</c> for 중층, in the same RR → STI → WSA
/// order the ticket ids run in.
/// <para><b>Measured, not assumed.</b> On 2026-08-28 the broadcast gave slot 2 artifacts 1001/1003/2001/2003 and
/// the player's 아티팩트 점령 abnormals said 2개 in each layer, i.e. slot 2 was ours; the 0x610B snapshot in the
/// same capture reported 130000 ms on exactly tickets 10000001/10000003/10000004/10000006 and zero on the other
/// two. Two independent sources naming the same four corridors is what pins this column.</para>
/// <para>It agrees with the client, which is the tie-breaker for the ORDER inside a layer (the measurement above
/// cannot separate 1001↔1 from 1001↔3): <c>ar2_artifact_001/002/003</c> point at
/// <c>E_AR3_RR/STI/WSA_L_ArtifactDungeonPortal_01</c>, the same RR → STI → WSA run the ticket blurbs use.</para></param>
public readonly record struct AbyssCorridorInfo(
    int TicketId,
    AbyssCorridorTier Tier,
    string Name,
    string ShortName,
    IReadOnlyList<int> MapIds,
    int ArtifactId = 0);

/// <summary>
/// The 어비스 회랑 (client-internal <c>ArtifactDungeon</c>, <c>EDungeonType::AbyssArtifact</c>) a character can
/// hold time for. Each is a solo room (<c>MaxMember = 1</c>, no boss, no clear UI) opened by capturing the
/// matching artifact in 점령전, and it is stocked with 130 seconds of 이용 시간 rather than a number of entries —
/// entering and leaving is free while the clock has anything left on it.
///
/// <para><b>The ticket → corridor binding is not the obvious one.</b> Authority is the client's
/// <c>MapEntrance.ContentsTicketList</c>, and its indices do NOT line up: map 503001 spends ticket 002, 503002
/// spends 003, and 503003 spends 001. That non-obvious permutation is exactly why this table is trusted — it
/// predicted all three corridors observed live (503001↔10000002, 503004↔10000004, 503006↔10000006).</para>
///
/// <para><b>⚠️ Do NOT source these names from <c>ContentsTicket.InfoDesc</c>.</b> It is what the client ships and
/// it is wrong for the middle layer: 004/005/006 still read 가르시칸 관측소 / 가르시칸 성채 / 지하 사원, which are
/// AR2 (the unreleased surface layer) names. The middle layer was re-themed to AR3 and only the ticket blurb was
/// left behind — <c>AbyssArtifact.dat</c> shows <c>ar2_artifact_001/002/003</c> pointing at
/// <c>E_AR3_RR/STI/WSA_L_ArtifactDungeonPortal_01</c>. Two of the three corridors actually visited would have
/// been mislabelled, and the stale strings are byte-identical across four client dumps, so nothing about them
/// looks out of date.</para>
/// <para>So the two layers take their names from different places. 하층 001~003 use the ticket blurbs, which
/// are correct there (002 = 유황나무 섬 is exactly what the player saw). 중층 004~006 use
/// <c>String_STR_Subzone_AR3_*</c>, reached through the portal each <c>ar2_artifact_*</c> row actually points
/// at — which is how 004 and 006 come out as the names observed live, and 005 = 오염된 늪지 by the same
/// ordering (inferred: that corridor has never been captured while the meter watched).</para>
/// </summary>
public static class AbyssCorridorCatalog
{
    /// <summary>The base grant per corridor, in ms — the panel's denominator.</summary>
    public const long FullGrantMs = AbyssCorridorParser.FullGrantMs;

    /// <summary>The six live corridors, 하층 then 중층, each in artifact order (RR → STI → WSA).</summary>
    public static IReadOnlyList<AbyssCorridorInfo> All { get; } =
    [
        new(10_000_001, AbyssCorridorTier.Lower, "에레슈란타의 뿌리", "에레슈란타", [503003, 504003], 1001),
        new(10_000_002, AbyssCorridorTier.Lower, "유황나무 섬", "유황나무", [503001, 504001], 1002),
        new(10_000_003, AbyssCorridorTier.Lower, "시엘의 날개 군도", "시엘군도", [503002, 504002], 1003),
        new(10_000_004, AbyssCorridorTier.Middle, "침식된 중앙섬", "중앙섬", [503004, 504004], 2001),
        new(10_000_005, AbyssCorridorTier.Middle, "오염된 늪지", "늪지", [503005, 504005], 2002),
        new(10_000_006, AbyssCorridorTier.Middle, "뒤틀린 고목나무 숲", "고목나무", [503006, 504006], 2003),
    ];

    /// <summary>Catalog entry for a ticket id, or null. Ids 10000007~10000012 exist in the client as unwired
    /// stubs (their row names even carry the typo <c>ArtifactDungoen</c>) and deliberately have no entry: if one
    /// ever goes live it is still parsed and stored, and only the label falls back to a generic one.</summary>
    public static AbyssCorridorInfo? ById(int ticketId)
    {
        foreach (AbyssCorridorInfo info in All)
        {
            if (info.TicketId == ticketId)
            {
                return info;
            }
        }

        return null;
    }

    /// <summary>The corridor whose instance map this is, or null. Used to tell "we entered a corridor" from any
    /// other zone change; both races' maps map to the same ticket.</summary>
    public static AbyssCorridorInfo? ByMapId(int mapId)
    {
        if (mapId <= 0)
        {
            return null;
        }

        foreach (AbyssCorridorInfo info in All)
        {
            for (int i = 0; i < info.MapIds.Count; i++)
            {
                if (info.MapIds[i] == mapId)
                {
                    return info;
                }
            }
        }

        return null;
    }

    /// <summary>Whether this instance map is a corridor at all.</summary>
    public static bool IsCorridorMap(int mapId) => ByMapId(mapId) is not null;

    /// <summary>The corridor an 아티팩트 id opens, or null. This is the join between the 점령 현황 broadcast and
    /// everything the panel already knows how to draw.</summary>
    public static AbyssCorridorInfo? ByArtifactId(int artifactId)
    {
        if (artifactId <= 0)
        {
            return null;
        }

        foreach (AbyssCorridorInfo info in All)
        {
            if (info.ArtifactId == artifactId)
            {
                return info;
            }
        }

        return null;
    }

    /// <summary>Display name for a ticket id, including ids this build has no entry for.</summary>
    public static string NameFor(int ticketId) =>
        ById(ticketId)?.Name ?? $"미상 회랑 ({ticketId})";
}

/// <summary>
/// The 점령 cycle a corridor's 이용 시간 belongs to. The client has NO recharge schedule for these tickets —
/// every daily/weekly/time field on all twelve rows is zero, and <c>CurrencyLimitSchedule</c> (which lists the
/// Wednesday 05:00 resets for party dungeons and 어비스 포인트) has no corridor category at all. The server
/// simply hands the time out when the artifact is captured, and the in-game guide says the state "다음 점령전
/// 이전 또는 서버 매칭 변경 전까지 유지됩니다".
///
/// <para><b>The 점령전 timetable, Wednesday and Saturday (KST).</b>
/// <list type="bullet">
/// <item><b>22:00</b> — the war starts. MEASURED, not reported: <c>EventSchedule</c> rows 3001
/// (<c>abyss_ar1_artifactwar</c>) and 3002 (<c>abyss_ar2_artifactwar</c>) each carry
/// <c>bWednesday = bSaturday = true</c> and a single <c>ActiveTimeList</c> entry of 792,000,000,000 ticks
/// = 79,200 s = 22:00:00 exactly, with every other day false (client dump 2026-08-23).</item>
/// <item><b>22:10</b> — the standing occupation is wiped (player-reported, unmeasured).</item>
/// <item><b>22:20</b> — capturing begins and the new holders settle (player-reported, unmeasured).</item>
/// <item><b>22:30</b> — the corridors open, and stay open until the next 점령전 (player-reported).</item>
/// </list></para>
///
/// <para><b>Why the boundary sits at 22:20 rather than the measured 22:00.</b> What is dated against it is a
/// corridor ENTRY — proof that the side held the artifact at that moment. During the war the OLD occupation is
/// still standing (the client's own guide says the corridor entrance is kept "점령 상태와 함께"), so a boundary
/// at the war's start would let a corridor entered at 22:05 under the outgoing occupation count as the new
/// one's — which is the exact shape of the bug this file exists to prevent. Erring late costs a few minutes in
/// which a freshly-won corridor is not yet credited; erring early puts a corridor the side has just LOST back
/// on the panel for three days. 22:00 is measured, the rest of the timetable is not, and the number stays
/// where it is until the moment the new occupation takes effect is measured too.</para>
///
/// <para><b>So the boundary is a fact, not a clock.</b> The moment any corridor reading arrives at or after
/// 22:20, that reading IS this cycle's answer — and everything stored from before it is last cycle's, no
/// matter how recent it looks. Waiting for a fixed hour instead would keep showing the previous occupation
/// through the whole capture, which is exactly the window in which it is most obviously wrong.</para>
///
/// <para>The clock is only the fallback, for the case the meter heard nothing at all — closed during 점령전,
/// or the character never logged in. Then there is no evidence either way, and <b>22:25</b> is where the
/// stored answer is given up rather than shown for another five minutes.</para>
///
/// <para>Fixed +09:00 rather than machine local time, for the same reason as
/// <see cref="WeeklyContentReset"/>: a Korean game's schedule does not move because the PC is set elsewhere,
/// and Korea has no DST.</para>
///
/// <para><b>The day and the period are measured; the minute is not.</b> Across the whole 96-session capture
/// corpus a corridor ticket rose in value 12 times, and all 12 rises straddle a Wed/Sat boundary — inside a
/// cycle there are 113 consecutive readings and not one increase. So 점령전 is the only thing that stocks these
/// tickets, and it really is Wed/Sat. What is still unmeasured is the minute: no capture has ever covered
/// 21:50~22:45 on either day, so the moment a corridor goes 0 → 130000 has never been seen. The tightest
/// bracket the corpus gives is 4 h 21 min (콘팡, 2026-07-08 20:00:23 all-zero → 07-09 00:21:26 four corridors
/// at 130000), which cannot separate 22:00 from 22:30.</para>
/// <para><b>A second, independent witness exists and is not used yet.</b> The 진영 occupation COUNT rides
/// 0x3633/0x3645 as abnormals 12000261~263 (하층 1/2/3개) and 12000264~266 (중층 1/2/3개). Measured 2026-08-23
/// 02:08 on 콘팡's own load packet: 12000261 + 12000264 and nothing else, i.e. exactly one artifact per layer —
/// which is how the corridor the ticket still claimed was proved lost, from the wire rather than from the
/// player. Cross-checked against 2026-08-19, where 12000261 + 12000265 matched the one 하층 and two 중층
/// corridors a character actually walked into that session. It cannot say WHICH corridors, so it cannot replace
/// the entry proof — but it can contradict it, and it is the obvious next thing to read.</para>
/// </summary>
public static class AbyssCorridorCycle
{
    private static readonly TimeSpan Kst = TimeSpan.FromHours(9);

    /// <summary>When capturing starts and the new holders begin to be broadcast. A reading stamped at or after
    /// this describes the CURRENT cycle by construction.</summary>
    public const int OccupationStartHour = 22;
    public const int OccupationStartMinute = 20;

    /// <summary>When a stored answer is given up if nothing new was ever heard. Deliberately before the
    /// corridors open at 22:30: by then the previous occupation has been gone for a quarter of an hour, and
    /// showing "모름" beats showing a corridor the faction may well have lost.</summary>
    public const int FallbackHour = 22;
    public const int FallbackMinute = 25;

    private static readonly DayOfWeek[] Days = [DayOfWeek.Wednesday, DayOfWeek.Saturday];

    /// <summary>Bounds a timestamp must fall inside to be treated as real (2020-01-01 .. 2100-01-01, the same
    /// window the packet parsers use). Not merely defensive: <c>DateTimeOffset</c>'s own range check admits year
    /// 1, and shifting THAT to +09:00 throws — so the obvious "is this a valid DateTimeOffset" guard would still
    /// have let a hand-edited settings file take the panel down.</summary>
    private const long MinPlausibleMs = 1_577_836_800_000L;
    private const long MaxPlausibleMs = 4_102_444_800_000L;

    /// <summary>The most recent 점령 start (Wed/Sat 22:20 KST) at or before <paramref name="atMs"/>, as Unix ms,
    /// or 0 for an unusable timestamp. A reading stamped at or after this is current-cycle evidence.</summary>
    public static long LastOccupationStartAtOrBefore(long atMs) =>
        LastWeeklySlotAtOrBefore(atMs, OccupationStartHour, OccupationStartMinute);

    /// <summary>How long after capturing begins the meter waits before giving up on a character it has heard
    /// nothing from. Five minutes — 22:20 to 22:25 — chosen so the old answer survives the minutes in which the
    /// new one is most likely to arrive, and is dropped before the corridors actually open at 22:30.</summary>
    public const long FallbackGraceMs = ((FallbackHour * 60 + FallbackMinute)
        - (OccupationStartHour * 60 + OccupationStartMinute)) * 60_000L;

    /// <summary>The boundary a stored reading is judged against when NOTHING has been heard since capturing
    /// began — see <see cref="BoundaryFor"/> for the case where something has.
    /// <para>Expressed as the capture start as of five minutes ago, which is the same rule read backwards:
    /// during 22:20~22:25 that still resolves to the PREVIOUS 점령전, so the old answer stays on screen; from
    /// 22:25 it resolves to today's 22:20 and retires everything older in one step.</para></summary>
    public static long LastStartAtOrBefore(long atMs) =>
        atMs < MinPlausibleMs || atMs > MaxPlausibleMs
            ? 0
            : LastOccupationStartAtOrBefore(atMs - FallbackGraceMs);

    /// <summary>The boundary for a character the meter HAS heard from, given the newest timestamp on anything
    /// stored for it. Once one reading lands at or after the capture began, that reading is this cycle's answer
    /// and every older record beside it is last cycle's — so the boundary snaps forward to 22:20 immediately
    /// rather than waiting for the fallback.</summary>
    public static long BoundaryFor(long newestObservedAtMs, long nowMs)
    {
        long occupation = LastOccupationStartAtOrBefore(nowMs);
        bool heardThisCycle = occupation > 0
            && newestObservedAtMs >= occupation
            && newestObservedAtMs <= nowMs + FutureSlackMs;

        return heardThisCycle ? occupation : LastStartAtOrBefore(nowMs);
    }

    /// <summary>Whether a reading taken at <paramref name="savedAtMs"/> is usable against
    /// <paramref name="boundary"/>: recent enough to belong to this cycle, and not stamped in a future that
    /// cannot have happened (see <see cref="FutureSlackMs"/>).</summary>
    public static bool IsWithin(long savedAtMs, long boundary, long nowMs) =>
        boundary > 0 && savedAtMs >= boundary && savedAtMs <= nowMs + FutureSlackMs;

    private static long LastWeeklySlotAtOrBefore(long atMs, int hour, int minute)
    {
        if (atMs < MinPlausibleMs || atMs > MaxPlausibleMs)
        {
            return 0;
        }

        DateTimeOffset now = DateTimeOffset.FromUnixTimeMilliseconds(atMs).ToOffset(Kst);

        // Walk back at most a full week. i == 7 is what catches "today IS Wednesday but it is still 21:00",
        // where the answer is last Saturday rather than today.
        long best = 0;
        for (int i = 0; i <= 7; i++)
        {
            DateTime day = now.Date.AddDays(-i);
            if (Array.IndexOf(Days, day.DayOfWeek) < 0)
            {
                continue;
            }

            var at = new DateTimeOffset(day.Year, day.Month, day.Day, hour, minute, 0, Kst);
            if (at <= now)
            {
                long ms = at.ToUnixTimeMilliseconds();
                if (ms > best)
                {
                    best = ms;
                }
            }
        }

        return best;
    }

    /// <summary>How far ahead of now a stored timestamp may sit and still be believed. A record cannot really
    /// come from the future, but a clock correction or a hand-edited file can produce one — and with no ceiling
    /// such a record satisfies <see cref="IsCurrentCycle"/> forever, pinning a stale corridor on screen through
    /// every 점령전 from then on, as well as convincing <see cref="BoundaryFor"/> that this cycle's answer has
    /// already been heard.</summary>
    public const long FutureSlackMs = 60 * 60 * 1000;

    /// <summary>True when a reading taken at <paramref name="savedAtMs"/> still describes the current cycle.</summary>
    public static bool IsCurrentCycle(long savedAtMs, long nowMs) =>
        savedAtMs > 0
        && savedAtMs <= nowMs + FutureSlackMs
        && savedAtMs >= LastStartAtOrBefore(nowMs);
}

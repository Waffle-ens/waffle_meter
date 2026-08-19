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
/// <param name="MapIds">Instance map ids for this corridor, 천족 first then 마족. Both races share one ticket.</param>
public readonly record struct AbyssCorridorInfo(
    int TicketId,
    AbyssCorridorTier Tier,
    string Name,
    string ShortName,
    IReadOnlyList<int> MapIds);

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
/// looks out of date. The names below come from <c>String_STR_Subzone_AR3_*</c> and match what the player sees.</para>
/// </summary>
public static class AbyssCorridorCatalog
{
    /// <summary>The base grant per corridor, in ms — the panel's denominator.</summary>
    public const long FullGrantMs = AbyssCorridorParser.FullGrantMs;

    /// <summary>The six live corridors, 하층 then 중층, each in artifact order (RR → STI → WSA).</summary>
    public static IReadOnlyList<AbyssCorridorInfo> All { get; } =
    [
        new(10_000_001, AbyssCorridorTier.Lower, "에레슈란타의 뿌리", "에레슈란타", [503003, 504003]),
        new(10_000_002, AbyssCorridorTier.Lower, "유황나무 섬", "유황나무", [503001, 504001]),
        new(10_000_003, AbyssCorridorTier.Lower, "시엘의 날개 군도", "시엘군도", [503002, 504002]),
        new(10_000_004, AbyssCorridorTier.Middle, "침식된 중앙섬", "중앙섬", [503004, 504004]),
        new(10_000_005, AbyssCorridorTier.Middle, "오염된 늪지", "늪지", [503005, 504005]),
        new(10_000_006, AbyssCorridorTier.Middle, "뒤틀린 고목나무 숲", "고목나무", [503006, 504006]),
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
/// <para>So the boundary a stored reading is judged stale against is the 점령전 itself: the content is offered
/// 수 22:30~토 22:00 and 토 22:30~수 22:00 KST, which makes <b>Wednesday and Saturday 22:30 KST</b> the moments
/// a fresh allocation can appear. Anything recorded before the most recent of those is no longer a claim this
/// meter can make.</para>
///
/// <para>Fixed +09:00 rather than machine local time, for the same reason as
/// <see cref="WeeklyContentReset"/>: a Korean game's schedule does not move because the PC is set elsewhere,
/// and Korea has no DST.</para>
///
/// <para><b>Unmeasured.</b> No capture has ever caught the moment a corridor went 0 → 130000, so the boundary
/// above is the published schedule rather than something observed. Every corpus reading is consistent with it
/// (the one apparent counter-example — 12 zeroes at 02:28 and three full tickets at 08:38 the same morning —
/// turned out to be two different characters, 하아앙 and 콘팡). If it is ever shown wrong, this is the single
/// place to change.</para>
/// </summary>
public static class AbyssCorridorCycle
{
    private static readonly TimeSpan Kst = TimeSpan.FromHours(9);

    /// <summary>Days a new allocation can land, with the hour/minute the usable window opens.</summary>
    public const int Hour = 22;
    public const int Minute = 30;

    private static readonly DayOfWeek[] Days = [DayOfWeek.Wednesday, DayOfWeek.Saturday];

    /// <summary>The most recent cycle start at or before <paramref name="atMs"/>, as Unix ms, or 0 when the
    /// input is not a usable timestamp (a hand-edited settings file must not throw the panel down).</summary>
    public static long LastStartAtOrBefore(long atMs)
    {
        if (atMs < DateTimeOffset.MinValue.ToUnixTimeMilliseconds()
            || atMs > DateTimeOffset.MaxValue.ToUnixTimeMilliseconds())
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

            var at = new DateTimeOffset(day.Year, day.Month, day.Day, Hour, Minute, 0, Kst);
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

    /// <summary>True when a reading taken at <paramref name="savedAtMs"/> still describes the current cycle.</summary>
    public static bool IsCurrentCycle(long savedAtMs, long nowMs) =>
        savedAtMs > 0 && savedAtMs >= LastStartAtOrBefore(nowMs);
}

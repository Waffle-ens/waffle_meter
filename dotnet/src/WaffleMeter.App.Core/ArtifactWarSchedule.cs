using WaffleMeter.Capture;

namespace WaffleMeter.App.Core;

/// <summary>
/// What a server's own 아티팩트 점령전(아티쟁) time decides: where one 어비스 회랑 cycle ends and the next
/// begins, and when the six 아티쟁 bosses appear. The source is not a clock but the window the server itself
/// stamps on the 점령 현황 broadcast (0xE305/0xE307 zone header,
/// <see cref="AbyssArtifactStore.LatestWindow(int,long)"/>) — its end IS that server's next war start, R.
///
/// <para><b>Why the clock had to go (2026-10-07).</b> Since the 09-30 patch the war no longer starts at one time
/// for everyone: servers are grouped at 21:20 / 21:50 / 22:20 (owner report). MEASURED on server 2003: the
/// 10-07 00:02 E307 carried end = Wed 10-07 21:20:00 for both zones, and the live client's <c>EventSchedule</c>
/// 3001/3002 now read Wed/Sat 21:20 — the other two groups are not in the client at all, they are the server's.
/// So the fixed Wed/Sat 22:20 snap / 22:25 give-up in <see cref="AbyssCorridorCycle"/> is wrong for two groups
/// out of three. Run against the live blob, the shipped build kept the previous cycle's corridors on a 21:20
/// server for 65 more minutes (until 22:25), and threw away a corridor spent at 21:47 — after that day's war —
/// at 22:25 as "last cycle's".</para>
///
/// <para><b>The rule.</b> While the window is still running (now &lt; end) the boundary is its start — the
/// earliest zone settle, since anything heard after the settle describes this occupation. Once now reaches the
/// end, the war has started and the boundary is the end itself: everything from the previous cycle is retired
/// on the spot, not at some later hour. Between R and the new settle nothing of the old cycle is shown; the
/// next 0xE305/0xE307 (sent from the settle on — the parser only accepts a frame whose event state is End)
/// brings the new window and its start takes over.</para>
///
/// <para><b>No window, the old clock.</b> A server the meter has never heard a window for (an alt's server, a
/// fresh install), or one whose window ended more than <see cref="AbyssArtifactStore.WindowStaleAfterMs"/>
/// ago, gets exactly the legacy <see cref="AbyssCorridorCycle"/> behaviour — 0 from
/// <see cref="CorridorBoundaryMs(AbyssArtifactWindow?,long)"/> is what tells the corridor store to use it.</para>
///
/// <para><b>The 아티쟁 bosses (owner decision 2026-10-07): R + <see cref="BossSpawnMinutesAfterWarStart"/>
/// minutes, nothing when R is unknown, and a time the server sent in 0x9101 always wins.</b> See
/// <see cref="WithWarBossTargets"/>.</para>
///
/// <para>Server epochs are compared with the local clock, the same assumption
/// <see cref="AbyssArtifactZoneState.IsCurrent"/> already makes. Pure, so all of it is unit-testable.</para>
/// </summary>
public static class ArtifactWarSchedule
{
    /// <summary>How many minutes after the server's war start (R = its window's end) the 아티쟁 bosses spawn.
    /// ⚠️ 이 값이 유일한 정본이다 — 화면·알림에 이 시각을 적어야 하면 여기서 파생시켜라.
    /// <para>Where 25 comes from: the live client (1.0.51, 09-30 patch) moved <c>EventSchedule</c> 3001/3002 to
    /// Wed/Sat 21:20 and, in the same patch, <c>PeriodSpawn</c> 1018-1020 / 1153-1155 — exactly the six boss
    /// slots — to 21:45: R + 25 for the base group. The pre-patch wire agrees: R = Wed 22:10 (E307, 08-19 and
    /// 08-26) against a server-sent boss time of Wed 22:35 (0x9101, 07-27). Unmeasured: whether the 21:50 and
    /// 22:20 groups get the same +25 (22:15 / 22:45), and whether the old wire's extra +5 over the client table
    /// came back. That is why a 0x9101 time, when there is one, overrides this.</para></summary>
    public const int BossSpawnMinutesAfterWarStart = 25;

    /// <summary><see cref="BossSpawnMinutesAfterWarStart"/> in ms.</summary>
    public const long BossSpawnOffsetMs = BossSpawnMinutesAfterWarStart * 60_000L;

    /// <summary>How long after its spawn a target is still offered. Two minutes — the same lag the 0x9101 parser
    /// tolerates on a server-sent time — so a server value a minute stale still "exists" and still wins, and a
    /// derived one does not vanish the second it is due.</summary>
    public const long BossTargetGraceMs = 2 * 60_000L;

    private static readonly Dictionary<int, int> WarZoneByBoss = BuildWarZones();

    /// <summary>The 회랑 cycle boundary a server's window gives at <paramref name="nowMs"/>: the window's start
    /// while it is running, its end once the war has started, or 0 when there is no window — which
    /// <see cref="AbyssCorridorStore"/> reads as "use the legacy clock".</summary>
    public static long CorridorBoundaryMs(AbyssArtifactWindow? window, long nowMs) =>
        window is { } w
            ? nowMs < w.EndMs ? w.StartMs : w.EndMs
            : 0;

    /// <summary>As <see cref="CorridorBoundaryMs(AbyssArtifactWindow?,long)"/>, for a server's newest window on
    /// file. 0 for an unknown server (id 0) or one with no usable window.</summary>
    public static long CorridorBoundaryMs(AbyssArtifactStore? artifacts, int serverId, long nowMs) =>
        serverId > 0 && artifacts is not null
            ? CorridorBoundaryMs(artifacts.LatestWindow(serverId, nowMs), nowMs)
            : 0;

    /// <summary>Whose war time the boss alarm follows: the current character's server, or — before any
    /// character has been identified this session (the meter usually starts first) — the server whose window
    /// was filed most recently. 0 when neither is known.</summary>
    public static int ServerFor(int currentServer, AbyssArtifactStore? artifacts) =>
        currentServer > 0 ? currentServer : artifacts?.LatestObservedServer() ?? 0;

    /// <summary>The 점령 zone whose war a 아티쟁 boss follows — 하층 bosses zone 1001, 중층 bosses zone 2001,
    /// decided by the map the catalog puts the boss on — or 0 for any other boss.</summary>
    public static int ZoneFor(int bossCode) =>
        WarZoneByBoss.TryGetValue(bossCode, out int zone) ? zone : 0;

    /// <summary>The spawn time this server's window implies for one 아티쟁 boss, or null when there is nothing
    /// to say: not a 아티쟁 boss, no server, no usable window, or a spawn more than
    /// <see cref="BossTargetGraceMs"/> in the past (a window from a war already over says nothing about the next
    /// one, and the meter does not guess the next R).
    /// <para>The boss's own zone answers first. Only when that zone was never filed for this server — a 0xE305
    /// carries one zone, so a player who only visited 하층 has no 중층 row — does the server's newest window
    /// stand in: both zones have carried the identical end in every frame measured.</para></summary>
    public static long? BossTargetMs(int bossCode, AbyssArtifactStore? artifacts, int serverId, long nowMs)
    {
        int zone = ZoneFor(bossCode);
        if (zone == 0 || artifacts is null || serverId <= 0)
        {
            return null;
        }

        if ((artifacts.LatestWindow(serverId, zone, nowMs) ?? artifacts.LatestWindow(serverId, nowMs))
            is not { } window)
        {
            return null;
        }

        long target = window.EndMs + BossSpawnOffsetMs;
        return nowMs <= target + BossTargetGraceMs ? target : null;
    }

    /// <summary>
    /// The field-boss timer table the alarm should evaluate: the 0x9101 timers as received, plus a derived
    /// spawn for each 아티쟁 boss the server has not timed.
    /// <para><b>The server wins.</b> A 0x9101 time for a boss that has not yet passed (within
    /// <see cref="BossTargetGraceMs"/>) is kept as is and nothing derived replaces it — one value per boss, so
    /// the alarm's <c>code:targetMs:lead</c> de-dup never sees two targets for one spawn. A server time that is
    /// already past is a spawn that has happened (the table lives for the whole session and is never cleared),
    /// so the next one derived from the window takes over.</para>
    /// <para>No window, no alarm — the meter does not invent a time for these six any more than it does for
    /// 감시자 카이라's zeroed record. <see cref="ArtifactWarBossTimers"/> is the session-stateful wrapper the app
    /// actually polls.</para>
    /// </summary>
    public static IReadOnlyDictionary<int, long> WithWarBossTargets(
        IReadOnlyDictionary<int, long> serverTimers, AbyssArtifactStore? artifacts, int currentServer, long nowMs)
    {
        int server = ServerFor(currentServer, artifacts);
        var derived = new Dictionary<int, long>();
        foreach (int code in FieldBossFixedSchedule.ArtifactWarTiedCodes)
        {
            if (BossTargetMs(code, artifacts, server, nowMs) is long target)
            {
                derived[code] = target;
            }
        }

        return Merge(serverTimers, derived, nowMs);
    }

    /// <summary>Lay derived 아티쟁 targets over the server's timers, the server's still-live value winning per
    /// boss. Returns <paramref name="serverTimers"/> itself when there is nothing to add.</summary>
    public static IReadOnlyDictionary<int, long> Merge(
        IReadOnlyDictionary<int, long> serverTimers, IReadOnlyDictionary<int, long> derived, long nowMs)
    {
        Dictionary<int, long>? merged = null;
        foreach ((int code, long target) in derived)
        {
            if (serverTimers.TryGetValue(code, out long sent) && sent >= nowMs - BossTargetGraceMs)
            {
                continue; // the server timed this spawn itself
            }

            merged ??= new Dictionary<int, long>(serverTimers);
            merged[code] = target;
        }

        return merged ?? serverTimers;
    }

    private static Dictionary<int, int> BuildWarZones()
    {
        var zones = new Dictionary<int, int>();
        foreach (FieldBossInfo boss in FieldBossCatalog.All())
        {
            if (!FieldBossFixedSchedule.IsArtifactWarTied(boss.Code))
            {
                continue;
            }

            int zone = boss.MapId switch
            {
                FieldBossCatalog.AbyssLowerMapId => AbyssArtifactBuffCatalog.LowerZoneId,
                FieldBossCatalog.AbyssMiddleMapId => AbyssArtifactBuffCatalog.MiddleZoneId,
                _ => 0,
            };
            if (zone > 0)
            {
                zones[boss.Code] = zone;
            }
        }

        return zones;
    }
}

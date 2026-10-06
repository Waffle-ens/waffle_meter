using WaffleMeter.App.Core;
using WaffleMeter.Capture;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

/// <summary>
/// Spec for the 아티쟁-tied abyss field-boss alarm (<see cref="ArtifactWarSchedule.WithWarBossTargets"/>,
/// <see cref="ArtifactWarBossTimers"/>). Owner decision 2026-10-07: spawn = R + 25 min where R is that server's
/// war start (its 점령 window's end), no alarm when R is unknown, and a 0x9101 time from the server always wins.
/// <para>R = Wed 10-07 21:20:00 is the measured 10-07 window of server 2003; the 21:50 and 22:20 groups are the
/// other two war starts the owner reported, built the same way.</para>
/// </summary>
public sealed class ArtifactWarBossTests
{
    private const int Server = 2003;

    private const int Lower = AbyssArtifactBuffCatalog.LowerZoneId;   // 1001
    private const int Middle = AbyssArtifactBuffCatalog.MiddleZoneId; // 2001

    private const long LowerStart = 1_791_031_077_000;  // Sat 10-03 21:37:57 KST
    private const long MiddleStart = 1_791_031_302_000; // Sat 10-03 21:41:42 KST
    private const long WarStart = 1_791_375_600_000;    // Wed 10-07 21:20:00 KST

    // 하층 집행자 ×3 (slots 2006-2008) and the three 중층 bosses on slots 2203-2205, as the meter maps them.
    private static readonly int[] LowerBosses = [2600096, 2600097, 2600098];
    private static readonly int[] MiddleBosses = [2600156, 2600521, 2600522];

    private static readonly IReadOnlyDictionary<int, long> NoServerTimers = new Dictionary<int, long>();

    private static long Kst(int mo, int d, int h, int mi, int s = 0) =>
        new DateTimeOffset(2026, mo, d, h, mi, s, TimeSpan.FromHours(9)).ToUnixTimeMilliseconds();

    private static IReadOnlyList<AbyssArtifactHolding> Holdings(int zoneId, params int[] owners) =>
        owners.Select((side, i) => new AbyssArtifactHolding(zoneId + i, side)).ToList();

    private static AbyssArtifactStore Window(
        int server, long lowerEnd, long middleEnd, long observedAt = 1_791_031_434_666)
    {
        var store = AbyssArtifactStore.Parse(null);
        store.UpsertOwnership(server, Lower, LowerStart, lowerEnd, Holdings(Lower, 1, 2, 1), observedAt);
        store.UpsertOwnership(server, Middle, MiddleStart, middleEnd, Holdings(Middle, 2, 2, 2), observedAt);
        return store;
    }

    private static AbyssArtifactStore Measured() => Window(Server, WarStart, WarStart);

    /// <summary>The war-tied set is exactly the six slots the client ties to the war (2006-2008, 2203-2205) — the
    /// current WedSat group, middle-floor names as the catalog has them — and nothing else gets a derived time.</summary>
    [Fact]
    public void Exactly_the_six_war_bosses_are_tied_to_the_war()
    {
        Assert.Equal(
            LowerBosses.Concat(MiddleBosses).Order(),
            FieldBossFixedSchedule.ArtifactWarTiedCodes.Order());

        Assert.All(LowerBosses, code => Assert.Equal(Lower, ArtifactWarSchedule.ZoneFor(code)));
        Assert.All(MiddleBosses, code => Assert.Equal(Middle, ArtifactWarSchedule.ZoneFor(code)));

        Assert.Equal(0, ArtifactWarSchedule.ZoneFor(2600084));                          // 수호신장 나흐마 — 금·일
        Assert.Equal(0, ArtifactWarSchedule.ZoneFor(2600520));                          // 금·일 slot
        Assert.Equal(0, ArtifactWarSchedule.ZoneFor(FieldBossCatalog.ScheduledSpawnCode)); // 감시자 카이라
        Assert.Equal(0, ArtifactWarSchedule.ZoneFor(2406034));                          // 모르헤임
    }

    /// <summary>The offset is one constant and the measured case reads off it: R 21:20 → 21:45.</summary>
    [Fact]
    public void The_offset_is_twenty_five_minutes_after_the_war_start()
    {
        Assert.Equal(25, ArtifactWarSchedule.BossSpawnMinutesAfterWarStart);
        Assert.Equal(25 * 60_000L, ArtifactWarSchedule.BossSpawnOffsetMs);
    }

    /// <summary>The three war groups. Literal expectations on purpose — derived from the constant they would
    /// stay green with the constant wrong.</summary>
    [Theory]
    [InlineData(21, 20, 21, 45)]
    [InlineData(21, 50, 22, 15)]
    [InlineData(22, 20, 22, 45)]
    public void Each_war_group_spawns_its_bosses_twenty_five_minutes_after_r(int rh, int rm, int sh, int sm)
    {
        long r = Kst(10, 7, rh, rm);
        AbyssArtifactStore store = Window(Server, r, r);

        IReadOnlyDictionary<int, long> timers =
            ArtifactWarSchedule.WithWarBossTargets(NoServerTimers, store, Server, Kst(10, 7, 20, 0));

        Assert.Equal(6, timers.Count);
        Assert.All(timers.Values, t => Assert.Equal(Kst(10, 7, sh, sm), t));
    }

    /// <summary>The server's own 0x9101 time wins for the boss it names, whatever it says; the bosses it does not
    /// name still get the derived time. One value per boss, so one alarm per spawn.</summary>
    [Fact]
    public void A_server_sent_time_wins()
    {
        long now = Kst(10, 7, 21, 0);
        var server = new Dictionary<int, long>
        {
            [2600096] = Kst(10, 7, 21, 50),  // the server says 21:50, not 21:45
            [2406034] = now + 600_000,       // an unrelated 모르헤임 timer passes through untouched
        };

        IReadOnlyDictionary<int, long> timers = ArtifactWarSchedule.WithWarBossTargets(server, Measured(), Server, now);

        Assert.Equal(Kst(10, 7, 21, 50), timers[2600096]);
        Assert.Equal(now + 600_000, timers[2406034]);
        Assert.All(
            LowerBosses.Skip(1).Concat(MiddleBosses),
            code => Assert.Equal(Kst(10, 7, 21, 45), timers[code]));
    }

    /// <summary>The 0x9101 table is kept for the whole session and never cleared, so a server time from a spawn
    /// that already happened must not block the next one.</summary>
    [Fact]
    public void A_server_time_already_past_does_not_block_the_next_spawn()
    {
        var server = new Dictionary<int, long> { [2600096] = Kst(10, 3, 21, 45) }; // last Saturday's
        IReadOnlyDictionary<int, long> timers =
            ArtifactWarSchedule.WithWarBossTargets(server, Measured(), Server, Kst(10, 7, 21, 0));

        Assert.Equal(Kst(10, 7, 21, 45), timers[2600096]);
    }

    /// <summary>R unknown → no alarm. No store, no window for this server, or no server at all: the six get no
    /// time, and the table is handed back exactly as the server sent it.</summary>
    [Fact]
    public void Unknown_r_gives_no_alarm()
    {
        long now = Kst(10, 7, 21, 0);
        var server = new Dictionary<int, long> { [2406034] = now + 600_000 };

        Assert.Same(server, ArtifactWarSchedule.WithWarBossTargets(server, null, Server, now));
        Assert.Same(server, ArtifactWarSchedule.WithWarBossTargets(server, AbyssArtifactStore.Parse(null), Server, now));
        Assert.Same(server, ArtifactWarSchedule.WithWarBossTargets(server, Window(1003, WarStart, WarStart), Server, now));
        Assert.Empty(ArtifactWarSchedule.WithWarBossTargets(NoServerTimers, AbyssArtifactStore.Parse(null), 0, now));
    }

    /// <summary>A window whose war is over says nothing about the next war, so once its spawn has passed (beyond
    /// the two-minute grace) nothing is offered — the meter does not guess the next R.</summary>
    [Fact]
    public void An_expired_window_gives_no_alarm()
    {
        long spawn = Kst(10, 7, 21, 45);

        Assert.Equal(spawn, ArtifactWarSchedule.BossTargetMs(2600096, Measured(), Server, spawn + 60_000));
        Assert.Null(ArtifactWarSchedule.BossTargetMs(2600096, Measured(), Server, spawn + 180_000));
        Assert.Empty(ArtifactWarSchedule.WithWarBossTargets(NoServerTimers, Measured(), Server, Kst(10, 8, 12, 0)));

        // Two weeks on the window is not read at all.
        Assert.Null(ArtifactWarSchedule.BossTargetMs(2600096, Measured(), Server, WarStart + (15L * 86_400_000)));
    }

    /// <summary>하층 bosses follow zone 1001's war, 중층 bosses zone 2001's. They have always carried the same end,
    /// so to see the selection the two ends are made to differ here.</summary>
    [Fact]
    public void Each_floor_follows_its_own_zone()
    {
        AbyssArtifactStore store = Window(Server, lowerEnd: Kst(10, 7, 21, 20), middleEnd: Kst(10, 7, 21, 50));

        IReadOnlyDictionary<int, long> timers =
            ArtifactWarSchedule.WithWarBossTargets(NoServerTimers, store, Server, Kst(10, 7, 20, 0));

        Assert.All(LowerBosses, code => Assert.Equal(Kst(10, 7, 21, 45), timers[code]));
        Assert.All(MiddleBosses, code => Assert.Equal(Kst(10, 7, 22, 15), timers[code]));
    }

    /// <summary>A 0xE305 carries one zone, so a player who only visited 하층 has no 중층 row. The server's newest
    /// window stands in rather than leaving the 중층 bosses silent with R known.</summary>
    [Fact]
    public void A_zone_never_filed_falls_back_to_the_servers_window()
    {
        var store = AbyssArtifactStore.Parse(null);
        store.UpsertOwnership(Server, Lower, LowerStart, WarStart, Holdings(Lower, 1, 2, 1), 1_791_031_133_452);

        IReadOnlyDictionary<int, long> timers =
            ArtifactWarSchedule.WithWarBossTargets(NoServerTimers, store, Server, Kst(10, 7, 20, 0));

        Assert.Equal(6, timers.Count);
        Assert.All(timers.Values, t => Assert.Equal(Kst(10, 7, 21, 45), t));
    }

    /// <summary>The current character's server decides; with no identity yet (the meter started before the game)
    /// the server whose window was filed last stands in.</summary>
    [Fact]
    public void The_current_server_decides_and_the_last_observed_one_stands_in()
    {
        AbyssArtifactStore store = Window(Server, Kst(10, 7, 21, 20), Kst(10, 7, 21, 20), observedAt: Kst(10, 4, 9, 0));
        store.UpsertOwnership(1003, Lower, LowerStart, Kst(10, 7, 22, 20), Holdings(Lower, 2, 2, 1), Kst(10, 5, 9, 0));
        store.UpsertOwnership(1003, Middle, MiddleStart, Kst(10, 7, 22, 20), Holdings(Middle, 1, 1, 1), Kst(10, 5, 9, 0));
        long now = Kst(10, 7, 20, 0);

        Assert.Equal(Kst(10, 7, 21, 45), ArtifactWarSchedule.WithWarBossTargets(NoServerTimers, store, Server, now)[2600096]);
        Assert.Equal(Kst(10, 7, 22, 45), ArtifactWarSchedule.WithWarBossTargets(NoServerTimers, store, 1003, now)[2600096]);

        Assert.Equal(1003, ArtifactWarSchedule.ServerFor(0, store));
        Assert.Equal(Kst(10, 7, 22, 45), ArtifactWarSchedule.WithWarBossTargets(NoServerTimers, store, 0, now)[2600096]);
    }

    /// <summary>End to end with the alarm: at 21:15 the 30-minute lead is due for all six; a server value equal
    /// to the derived one produces the same de-dup key, so the two can never ring twice.</summary>
    [Fact]
    public void The_alarm_fires_the_derived_spawn_once()
    {
        long now = Kst(10, 7, 21, 15);
        IReadOnlyDictionary<int, long> derived =
            ArtifactWarSchedule.WithWarBossTargets(NoServerTimers, Measured(), Server, now);

        IReadOnlyList<FieldBossAlarm.Due> due = FieldBossAlarm.DueAlerts(derived, now, [30, 10, 5]);
        Assert.Equal(6, due.Count);
        Assert.All(due, d => Assert.Equal(30, d.LeadMinutes));

        var sameFromServer = new Dictionary<int, long> { [2600096] = Kst(10, 7, 21, 45) };
        FieldBossAlarm.Due fromServer = FieldBossAlarm.DueAlerts(
                ArtifactWarSchedule.WithWarBossTargets(sameFromServer, Measured(), Server, now), now, [30])
            .Single(d => d.Code == 2600096);
        Assert.Equal(FieldBossAlarm.Key(due.Single(d => d.Code == 2600096)), FieldBossAlarm.Key(fromServer));
    }

    // ---- the session memory ----

    /// <summary>The reason <see cref="ArtifactWarBossTimers"/> exists. The window filed right after the war (its
    /// settle, ~21:38) already names the NEXT war, so a stateless read loses today's 21:45 before its 5-minute
    /// lead at 21:40. The session memory keeps it until it has passed, then moves on to the next one.</summary>
    [Fact]
    public void A_window_filed_after_the_war_does_not_steal_todays_spawn()
    {
        var timers = new ArtifactWarBossTimers();
        AbyssArtifactStore store = Measured();

        Assert.Equal(Kst(10, 7, 21, 45), timers.Merge(NoServerTimers, store, Server, Kst(10, 7, 21, 15))[2600096]);

        // 21:39 — the new cycle's window is filed over both zones (next war: Sat 10-10 21:20).
        store.UpsertOwnership(Server, Lower, Kst(10, 7, 21, 38), Kst(10, 10, 21, 20), Holdings(Lower, 1, 2, 1), Kst(10, 7, 21, 39));
        store.UpsertOwnership(Server, Middle, Kst(10, 7, 21, 41), Kst(10, 10, 21, 20), Holdings(Middle, 2, 2, 2), Kst(10, 7, 21, 42));

        long at2140 = Kst(10, 7, 21, 40);
        Assert.Equal(Kst(10, 10, 21, 45), ArtifactWarSchedule.WithWarBossTargets(NoServerTimers, store, Server, at2140)[2600096]);

        IReadOnlyDictionary<int, long> remembered = timers.Merge(NoServerTimers, store, Server, at2140);
        Assert.All(LowerBosses.Concat(MiddleBosses), code => Assert.Equal(Kst(10, 7, 21, 45), remembered[code]));
        Assert.Contains(
            FieldBossAlarm.DueAlerts(remembered, at2140, [5]),
            d => d.Code == 2600096 && d.TargetMs == Kst(10, 7, 21, 45));

        // Once today's spawn has passed (beyond the grace), Saturday's takes over.
        Assert.Equal(Kst(10, 10, 21, 45), timers.Merge(NoServerTimers, store, Server, Kst(10, 7, 21, 48))[2600096]);
    }

    /// <summary>The memory is per server: what was derived for one server's war never rings for a character on
    /// another, and the server's own time still wins over a remembered one.</summary>
    [Fact]
    public void The_session_memory_is_per_server_and_still_loses_to_the_server()
    {
        var timers = new ArtifactWarBossTimers();
        Assert.Equal(6, timers.Merge(NoServerTimers, Measured(), Server, Kst(10, 7, 21, 15)).Count);

        Assert.Empty(timers.Merge(NoServerTimers, Measured(), 1003, Kst(10, 7, 21, 16)));

        var server = new Dictionary<int, long> { [2600521] = Kst(10, 7, 21, 50) };
        Assert.Equal(Kst(10, 7, 21, 50), timers.Merge(server, Measured(), Server, Kst(10, 7, 21, 17))[2600521]);
    }

    /// <summary>No server known at all — no identity and nothing filed — leaves the table as the server sent it.</summary>
    [Fact]
    public void The_session_supply_with_nothing_known_is_the_servers_table()
    {
        var server = new Dictionary<int, long> { [2406034] = Kst(10, 7, 21, 15) };
        Assert.Same(server, new ArtifactWarBossTimers().Merge(server, AbyssArtifactStore.Parse(null), 0, Kst(10, 7, 21, 0)));
    }
}

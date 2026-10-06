using WaffleMeter.App.Core;
using WaffleMeter.Capture;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

/// <summary>
/// Spec for the 회랑 cycle boundary taken from the server's own 점령 window (<see cref="ArtifactWarSchedule"/>,
/// <see cref="AbyssArtifactStore.LatestWindow(int,long)"/>) instead of the Wed/Sat 22:20 clock.
///
/// <para>The fixture is the one window measured under the new staggered schedule: server 2003's 0xE307 at
/// 2026-10-07 00:02:12 KST — 하층 settled Sat 10-03 21:37:57, 중층 21:41:42, both ending Wed 10-07 21:20:00
/// (the "21:20 group"). Scenarios A and B are the two defects the shipped build showed when run against the live
/// blob; C is the promise that a server with no window behaves exactly as before.</para>
/// </summary>
public sealed class ArtifactWarScheduleTests
{
    private const int Server = 2003;

    private const int Lower = AbyssArtifactBuffCatalog.LowerZoneId;   // 1001
    private const int Middle = AbyssArtifactBuffCatalog.MiddleZoneId; // 2001

    // The measured 10-07 window (server 2003).
    private const long LowerStart = 1_791_031_077_000;  // Sat 10-03 21:37:57 KST
    private const long MiddleStart = 1_791_031_302_000; // Sat 10-03 21:41:42 KST
    private const long WarStart = 1_791_375_600_000;    // Wed 10-07 21:20:00 KST — R

    private const string Hash = "h1";
    private const string Sibling = "h2";

    private static long Kst(int mo, int d, int h, int mi, int s = 0) =>
        new DateTimeOffset(2026, mo, d, h, mi, s, TimeSpan.FromHours(9)).ToUnixTimeMilliseconds();

    // The next window, as it would arrive after Wednesday's war: settled 21:38 / 21:41, ending Sat 10-10 21:20.
    private static readonly long NextLowerStart = Kst(10, 7, 21, 38);
    private static readonly long NextMiddleStart = Kst(10, 7, 21, 41);
    private static readonly long NextWarStart = Kst(10, 10, 21, 20);

    private static IReadOnlyList<AbyssArtifactHolding> Holdings(int zoneId, params int[] owners) =>
        owners.Select((side, i) => new AbyssArtifactHolding(zoneId + i, side)).ToList();

    /// <summary>The 10-07 frame as filed: owners 1,2,1 (하층) and 2,2,2 (중층), observed the way the live blob
    /// has it (56 s and 2m12s after each settle), plus the 점령 개수 that puts us in slot 2.</summary>
    private static AbyssArtifactStore MeasuredWindow(bool withCount = true)
    {
        var store = AbyssArtifactStore.Parse(null);
        store.UpsertOwnership(Server, Lower, LowerStart, WarStart, Holdings(Lower, 1, 2, 1), 1_791_031_133_452);
        store.UpsertOwnership(Server, Middle, MiddleStart, WarStart, Holdings(Middle, 2, 2, 2), 1_791_031_434_666);
        if (withCount)
        {
            store.UpsertCount(Hash, Lower, 1, Kst(10, 4, 20, 0));   // 12000261 — 하층 1개
            store.UpsertCount(Hash, Middle, 3, Kst(10, 4, 20, 0));  // 12000266 — 중층 3개
        }

        return store;
    }

    /// <summary>The same store after Wednesday's war: the next window has been filed over both zones.</summary>
    private static AbyssArtifactStore AfterWednesdaysWar(bool withCount = true)
    {
        AbyssArtifactStore store = MeasuredWindow(withCount: false);
        store.UpsertOwnership(Server, Lower, NextLowerStart, NextWarStart, Holdings(Lower, 1, 2, 1), Kst(10, 7, 21, 39));
        store.UpsertOwnership(Server, Middle, NextMiddleStart, NextWarStart, Holdings(Middle, 2, 2, 2), Kst(10, 7, 21, 42));
        if (withCount)
        {
            store.UpsertCount(Hash, Lower, 1, Kst(10, 7, 21, 43));
            store.UpsertCount(Hash, Middle, 3, Kst(10, 7, 21, 43));
        }

        return store;
    }

    private static AetherPerCharacterStore TwoCharacters()
    {
        var store = AetherPerCharacterStore.Parse(null);
        store.Upsert(Hash, new AetherSnapshot(100, 0, Kst(10, 4, 20, 0), "밀피", Server));
        store.Upsert(Sibling, new AetherSnapshot(80, 0, Kst(10, 4, 19, 0), "부캐", Server));
        return store;
    }

    private static AetherRosterRow Row(AbyssCorridorStore corridors, AbyssArtifactStore? artifacts, long nowMs) =>
        AetherRoster.Build(TwoCharacters(), currentHash: Hash, nowMs: nowMs, corridors: corridors, artifacts: artifacts)
            .Single(r => r.IdentityHash == Hash);

    private static void Entered(AbyssCorridorStore store, string hash, int ticketId, long atMs, long remainingMs)
    {
        store.MarkEntered(hash, ticketId, atMs);
        store.Upsert(hash, ticketId, remainingMs, atMs, markGranted: remainingMs > 0);
    }

    // ---- the window accessor ----

    /// <summary>The window survives its own end. <see cref="AbyssArtifactStore.Zones"/> goes silent at R by
    /// design — and R is the one moment the corridor boundary most needs to hear about.</summary>
    [Fact]
    public void The_latest_window_is_still_readable_after_it_ends()
    {
        AbyssArtifactStore store = MeasuredWindow();

        Assert.False(store.HasOwnership(Server, WarStart + 60_000));
        Assert.Equal(new AbyssArtifactWindow(LowerStart, WarStart), store.LatestWindow(Server, WarStart + 60_000));
    }

    /// <summary>The two zones settle 3m45s apart; a reading taken between the two settles already belongs to the
    /// new occupation, so the server-wide start is the EARLIER one. Each zone still answers for itself.</summary>
    [Fact]
    public void The_server_window_starts_at_the_earliest_zone_settle()
    {
        AbyssArtifactStore store = MeasuredWindow();
        long now = Kst(10, 6, 12, 0);

        Assert.Equal(LowerStart, store.LatestWindow(Server, now)!.Value.StartMs);
        Assert.Equal(new AbyssArtifactWindow(MiddleStart, WarStart), store.LatestWindow(Server, Middle, now));
        Assert.Equal(new AbyssArtifactWindow(LowerStart, WarStart), store.LatestWindow(Server, Lower, now));
        Assert.Null(store.LatestWindow(Server, 3001, now));
        Assert.Null(store.LatestWindow(1003, now));
    }

    /// <summary>Only the 하층 0xE305 has arrived since the war: the 중층 row still carries the previous cycle and
    /// must not drag the new cycle's start back four days.</summary>
    [Fact]
    public void A_zone_still_on_the_previous_cycle_does_not_drag_the_start_back()
    {
        AbyssArtifactStore store = MeasuredWindow(withCount: false);
        store.UpsertOwnership(Server, Lower, NextLowerStart, NextWarStart, Holdings(Lower, 1, 2, 1), Kst(10, 7, 21, 39));

        Assert.Equal(
            new AbyssArtifactWindow(NextLowerStart, NextWarStart),
            store.LatestWindow(Server, Kst(10, 7, 21, 40)));
        Assert.Equal(
            new AbyssArtifactWindow(MiddleStart, WarStart),
            store.LatestWindow(Server, Middle, Kst(10, 7, 21, 40)));
    }

    /// <summary>A start in the future beyond the slack is a clock or a file that cannot be right; an end more
    /// than two weeks old is a matchup that may well have moved since. Neither is believed.</summary>
    [Fact]
    public void Implausible_windows_are_unknown()
    {
        AbyssArtifactStore store = MeasuredWindow();

        // PC clock two hours behind the settle: the settle "has not happened yet".
        Assert.Null(store.LatestWindow(Server, LowerStart - (2 * 3_600_000L)));
        Assert.NotNull(store.LatestWindow(Server, LowerStart - (30 * 60_000L)));

        // Thirteen days after R it still answers; fifteen days after, it does not.
        Assert.NotNull(store.LatestWindow(Server, WarStart + (13L * 86_400_000)));
        Assert.Null(store.LatestWindow(Server, WarStart + (15L * 86_400_000)));
        Assert.Null(store.LatestWindow(Server, Lower, WarStart + (15L * 86_400_000)));
    }

    [Fact]
    public void The_most_recently_observed_server_is_the_one_last_filed()
    {
        Assert.Equal(0, AbyssArtifactStore.Parse(null).LatestObservedServer());

        AbyssArtifactStore store = MeasuredWindow();
        store.UpsertOwnership(1003, Lower, LowerStart, WarStart, Holdings(Lower, 2, 2, 1), 1_791_000_000_000);
        Assert.Equal(Server, store.LatestObservedServer());

        store.UpsertOwnership(1003, Middle, MiddleStart, WarStart, Holdings(Middle, 1, 1, 1), Kst(10, 5, 9, 0));
        Assert.Equal(1003, store.LatestObservedServer());
    }

    /// <summary>The window is read, never rewritten: the blob keeps its seven-field shape, so an older build
    /// rolled back onto it still parses every record.</summary>
    [Fact]
    public void Reading_the_window_does_not_change_the_blob_format()
    {
        AbyssArtifactStore store = MeasuredWindow();
        string before = store.Serialize();

        _ = store.LatestWindow(Server, WarStart + 60_000);

        Assert.Equal(before, store.Serialize());
        Assert.All(before.Split(';'), record => Assert.Equal(7, record.Split(',').Length));
    }

    // ---- the boundary rule ----

    [Fact]
    public void The_boundary_is_the_settle_while_the_window_runs_and_r_once_it_ends()
    {
        var window = new AbyssArtifactWindow(LowerStart, WarStart);

        Assert.Equal(LowerStart, ArtifactWarSchedule.CorridorBoundaryMs(window, WarStart - 1));
        Assert.Equal(WarStart, ArtifactWarSchedule.CorridorBoundaryMs(window, WarStart));
        Assert.Equal(WarStart, ArtifactWarSchedule.CorridorBoundaryMs(window, WarStart + 3_600_000));
        Assert.Equal(0, ArtifactWarSchedule.CorridorBoundaryMs((AbyssArtifactWindow?)null, WarStart));

        // Per server: unknown id, no store or no window → 0, i.e. the legacy clock.
        Assert.Equal(LowerStart, ArtifactWarSchedule.CorridorBoundaryMs(MeasuredWindow(), Server, Kst(10, 6, 12, 0)));
        Assert.Equal(0, ArtifactWarSchedule.CorridorBoundaryMs(MeasuredWindow(), 0, Kst(10, 6, 12, 0)));
        Assert.Equal(0, ArtifactWarSchedule.CorridorBoundaryMs(MeasuredWindow(), 1003, Kst(10, 6, 12, 0)));
        Assert.Equal(0, ArtifactWarSchedule.CorridorBoundaryMs(null, Server, Kst(10, 6, 12, 0)));
    }

    // ---- Scenario A: the war has started, the previous cycle goes NOW ----

    /// <summary><b>Scenario A, store level.</b> A corridor entered on Sunday is last cycle's answer from the
    /// moment the 21:20 war starts — at 21:21, not at the clock's 22:25 give-up. The legacy clock is shown
    /// alongside to pin what changed: it kept the chip for another 65 minutes.</summary>
    [Fact]
    public void Previous_cycle_entry_proofs_retire_one_minute_after_r()
    {
        var corridors = AbyssCorridorStore.Parse(null);
        Entered(corridors, Hash, 10_000_002, Kst(10, 4, 20, 0), 130_000);
        corridors.MarkWitness(Hash, Kst(10, 4, 20, 0));
        AbyssArtifactStore artifacts = MeasuredWindow();

        long before = Kst(10, 7, 21, 19);
        long after = Kst(10, 7, 21, 21);
        long boundaryBefore = ArtifactWarSchedule.CorridorBoundaryMs(artifacts, Server, before);
        long boundaryAfter = ArtifactWarSchedule.CorridorBoundaryMs(artifacts, Server, after);

        Assert.Equal(130_000, corridors.Standing(Hash, 10_000_002, before, boundaryBefore));
        Assert.True(corridors.HasCycleWitness(Hash, before, boundaryBefore));

        Assert.Equal(WarStart, boundaryAfter);
        Assert.Null(corridors.Standing(Hash, 10_000_002, after, boundaryAfter));
        Assert.False(corridors.EnteredThisCycle(Hash, 10_000_002, after, boundaryAfter));
        Assert.Null(corridors.Reading(Hash, 10_000_002, after, boundaryAfter));
        Assert.False(corridors.HasCycleWitness(Hash, after, boundaryAfter));

        // The old clock, for contrast: still claimed at 21:21 and at 22:24, given up only at its 22:25.
        Assert.Equal(130_000, corridors.Standing(Hash, 10_000_002, after));
        Assert.Equal(130_000, corridors.Standing(Hash, 10_000_002, Kst(10, 7, 22, 24)));
        Assert.Null(corridors.Standing(Hash, 10_000_002, Kst(10, 7, 22, 25)));
    }

    /// <summary><b>Scenario A, on the panel.</b> At 21:19 the broadcast holds the row (four chips, confirmed). At
    /// 21:21 the broadcast has expired — and the entry proof the row used to fall back on is dated before R, so
    /// it goes too. The shipped build showed it until 22:25.</summary>
    [Fact]
    public void The_panel_empties_at_r_instead_of_falling_back_to_last_cycles_entries()
    {
        var corridors = AbyssCorridorStore.Parse(null);
        Entered(corridors, Sibling, 10_000_002, Kst(10, 4, 19, 0), 130_000);
        corridors.MarkWitness(Hash, Kst(10, 4, 20, 0));
        AbyssArtifactStore artifacts = MeasuredWindow();

        AetherRosterRow at2119 = Row(corridors, artifacts, Kst(10, 7, 21, 19));
        Assert.True(at2119.CorridorsConfirmed);
        Assert.Equal(
            [10_000_002, 10_000_004, 10_000_005, 10_000_006],
            at2119.CorridorCells.Select(c => c.Corridor.TicketId));

        AetherRosterRow at2121 = Row(corridors, artifacts, Kst(10, 7, 21, 21));
        Assert.Empty(at2121.CorridorCells);
        Assert.False(at2121.CorridorsKnown);
        Assert.False(at2121.CorridorsConfirmed);
    }

    // ---- Scenario B: the new cycle's evidence survives the old clock's 22:25 ----

    /// <summary><b>Scenario B, store level.</b> The war ended at 21:38 and its window has been heard. A
    /// corridor entered at 21:45 and spent at 21:47 is THIS cycle's — at 22:30 it still reads 0:00. The clock
    /// threw it away at 22:25 as "last cycle's", because 21:47 is before its 22:20.</summary>
    [Fact]
    public void New_cycle_evidence_survives_past_2225()
    {
        var corridors = AbyssCorridorStore.Parse(null);
        Entered(corridors, Hash, 10_000_002, Kst(10, 7, 21, 45), 130_000);
        corridors.Upsert(Hash, 10_000_002, 0, Kst(10, 7, 21, 47), markGranted: false);
        AbyssArtifactStore artifacts = AfterWednesdaysWar();

        long at2230 = Kst(10, 7, 22, 30);
        long boundary = ArtifactWarSchedule.CorridorBoundaryMs(artifacts, Server, at2230);

        Assert.Equal(NextLowerStart, boundary);
        Assert.True(corridors.EnteredThisCycle(Hash, 10_000_002, at2230, boundary));
        Assert.Equal(0, corridors.Reading(Hash, 10_000_002, at2230, boundary));
        Assert.Equal(0, corridors.Standing(Hash, 10_000_002, at2230, boundary));

        // The old clock, for contrast.
        Assert.False(corridors.EnteredThisCycle(Hash, 10_000_002, at2230));
        Assert.Null(corridors.Reading(Hash, 10_000_002, at2230));
    }

    /// <summary><b>Scenario B, on the panel.</b> The broadcast names the corridors; the 21:47 spend puts 0:00 on
    /// 유황나무 and the other three read the full grant as guesses. The clock-dated build showed 유황나무 as a
    /// fresh ~2:10 after 22:25.</summary>
    [Fact]
    public void A_corridor_spent_after_the_war_reads_spent_on_the_panel_after_2225()
    {
        var corridors = AbyssCorridorStore.Parse(null);
        Entered(corridors, Hash, 10_000_002, Kst(10, 7, 21, 45), 130_000);
        corridors.Upsert(Hash, 10_000_002, 0, Kst(10, 7, 21, 47), markGranted: false);

        AetherRosterRow row = Row(corridors, AfterWednesdaysWar(), Kst(10, 7, 22, 30));

        AbyssCorridorCell spent = row.CorridorCells.Single(c => c.Corridor.TicketId == 10_000_002);
        Assert.True(spent.Spent);
        Assert.False(spent.Inferred);
        Assert.All(
            row.CorridorCells.Where(c => c.Corridor.TicketId != 10_000_002),
            c => Assert.Equal(AbyssCorridorCatalog.FullGrantMs, c.RemainingMs));
    }

    /// <summary>The previous cycle's 0:00 on a corridor the side holds AGAIN this cycle is not this cycle's
    /// reading: the corridor was re-stocked at the war, so it reads the full grant (inferred), not 0:00. The
    /// shipped build showed the old 0:00 until 22:25.</summary>
    [Fact]
    public void Last_cycles_zero_is_not_shown_on_a_corridor_held_again()
    {
        var corridors = AbyssCorridorStore.Parse(null);
        corridors.Upsert(Hash, 10_000_004, 0, Kst(10, 4, 20, 30), markGranted: false);

        AetherRosterRow row = Row(corridors, AfterWednesdaysWar(), Kst(10, 7, 21, 50));

        AbyssCorridorCell cell = row.CorridorCells.Single(c => c.Corridor.TicketId == 10_000_004);
        Assert.Equal(AbyssCorridorCatalog.FullGrantMs, cell.RemainingMs);
        Assert.True(cell.Inferred);
    }

    // ---- Scenario C: no window, nothing changes ----

    /// <summary><b>Scenario C.</b> A server with no window on file — another server's window does not count —
    /// takes the legacy clock exactly: the same chips at 22:24, gone after the 22:25 give-up, with or without the
    /// artifact store passed in.</summary>
    [Fact]
    public void A_server_with_no_window_keeps_the_legacy_clock()
    {
        var corridors = AbyssCorridorStore.Parse(null);
        Entered(corridors, Hash, 10_000_002, Kst(10, 4, 20, 0), 130_000);

        var otherServerOnly = AbyssArtifactStore.Parse(null);
        otherServerOnly.UpsertOwnership(1003, Lower, LowerStart, WarStart, Holdings(Lower, 1, 2, 1), Kst(10, 4, 9, 0));

        foreach (long now in new[] { Kst(10, 7, 21, 21), Kst(10, 7, 22, 24), Kst(10, 7, 22, 26) })
        {
            AetherRosterRow legacy = Row(corridors, artifacts: null, now);
            AetherRosterRow withStore = Row(corridors, otherServerOnly, now);

            Assert.Equal(
                legacy.CorridorCells.Select(c => (c.Corridor.TicketId, c.RemainingMs, c.Inferred)),
                withStore.CorridorCells.Select(c => (c.Corridor.TicketId, c.RemainingMs, c.Inferred)));
            Assert.Equal(legacy.CorridorsKnown, withStore.CorridorsKnown);
        }

        Assert.Single(Row(corridors, otherServerOnly, Kst(10, 7, 22, 24)).CorridorCells);
        Assert.Empty(Row(corridors, otherServerOnly, Kst(10, 7, 22, 26)).CorridorCells);
    }

    /// <summary>A window that ended more than two weeks ago is no longer an answer, so the server goes back to
    /// the clock rather than dating everything since against a war that long past.</summary>
    [Fact]
    public void A_stale_window_falls_back_to_the_legacy_clock()
    {
        long threeWeeksOn = WarStart + (21L * 86_400_000);
        Assert.Equal(0, ArtifactWarSchedule.CorridorBoundaryMs(MeasuredWindow(), Server, threeWeeksOn));
    }

    /// <summary>A server boundary of 0 is the legacy rule, byte for byte — every reader's default. The existing
    /// <c>AbyssCorridorCycleTests</c>/<c>AbyssCorridorStoreTests</c> call the readers without it and must stay
    /// green; this pins that passing 0 explicitly is the same call.</summary>
    [Theory]
    [InlineData(10, 4, 20, 0)]
    [InlineData(10, 7, 21, 21)]
    [InlineData(10, 7, 22, 25)]
    [InlineData(10, 7, 22, 26)]
    public void A_zero_server_boundary_is_the_legacy_rule(int mo, int d, int h, int mi)
    {
        var corridors = AbyssCorridorStore.Parse(null);
        Entered(corridors, Hash, 10_000_002, Kst(10, 4, 20, 0), 130_000);
        corridors.MarkWitness(Hash, Kst(10, 4, 20, 0));
        long now = Kst(mo, d, h, mi);

        Assert.Equal(corridors.Standing(Hash, 10_000_002, now), corridors.Standing(Hash, 10_000_002, now, 0));
        Assert.Equal(corridors.Reading(Hash, 10_000_002, now), corridors.Reading(Hash, 10_000_002, now, 0));
        Assert.Equal(
            corridors.EnteredThisCycle(Hash, 10_000_002, now),
            corridors.EnteredThisCycle(Hash, 10_000_002, now, 0));
        Assert.Equal(corridors.HasCycleWitness(Hash, now), corridors.HasCycleWitness(Hash, now, 0));
    }

    /// <summary>The grant stamp's carry-over follows the same boundary. A grant from Sunday is not carried onto a
    /// reading taken after R; the legacy clock still carried it until 22:25.</summary>
    [Fact]
    public void A_grant_stamp_does_not_carry_across_r()
    {
        long sunday = Kst(10, 4, 20, 0);
        long afterR = Kst(10, 7, 21, 30);
        long boundary = ArtifactWarSchedule.CorridorBoundaryMs(MeasuredWindow(), Server, afterR);

        var withWindow = AbyssCorridorStore.Parse(null);
        withWindow.Upsert(Hash, 10_000_002, 130_000, sunday, markGranted: true);
        withWindow.Upsert(Hash, 10_000_002, 0, afterR, markGranted: false, serverBoundaryMs: boundary);
        Assert.Equal(0, withWindow.Get(Hash, 10_000_002)!.Value.GrantedAtMs);

        var legacy = AbyssCorridorStore.Parse(null);
        legacy.Upsert(Hash, 10_000_002, 130_000, sunday, markGranted: true);
        legacy.Upsert(Hash, 10_000_002, 0, afterR, markGranted: false);
        Assert.Equal(sunday, legacy.Get(Hash, 10_000_002)!.Value.GrantedAtMs);
    }
}

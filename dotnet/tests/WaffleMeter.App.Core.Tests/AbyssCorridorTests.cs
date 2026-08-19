using WaffleMeter.App.Core;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

/// <summary>
/// The corridor catalog is a hand-written table standing in for client data, so what it is worth is exactly how
/// well it survives the three corridors that were actually visited. Those three pairs are the fixture.
/// </summary>
public class AbyssCorridorCatalogTests
{
    /// <summary>The measured (mapId, ticketId, name) triples, 2026-08-17 and 2026-08-19. The map→ticket mapping
    /// is a permutation, not an offset — 503001 spends ticket 002 — which is why hand-deriving it from the id
    /// would have been wrong in two of these three.</summary>
    [Theory]
    [InlineData(503001, 10_000_002, "유황나무 섬", AbyssCorridorTier.Lower)]
    [InlineData(503004, 10_000_004, "침식된 중앙섬", AbyssCorridorTier.Middle)]
    [InlineData(503006, 10_000_006, "뒤틀린 고목나무 숲", AbyssCorridorTier.Middle)]
    public void Observed_corridors_map_to_the_ticket_and_name_the_player_saw(
        int mapId, int ticketId, string name, AbyssCorridorTier tier)
    {
        AbyssCorridorInfo corridor = Assert.NotNull(AbyssCorridorCatalog.ByMapId(mapId));

        Assert.Equal(ticketId, corridor.TicketId);
        Assert.Equal(name, corridor.Name);
        Assert.Equal(tier, corridor.Tier);
    }

    /// <summary>Both factions' maps spend the same ticket (client <c>MapEntrance.ContentsTicketList</c>), so a
    /// 마족 character has to resolve to the same corridor. Never observed live — this build has only ever watched
    /// 천족 — so it is asserted here to make the assumption visible rather than implied.</summary>
    [Theory]
    [InlineData(504001, 10_000_002)]
    [InlineData(504003, 10_000_001)]
    [InlineData(504006, 10_000_006)]
    public void Dark_faction_maps_resolve_to_the_same_ticket(int mapId, int ticketId) =>
        Assert.Equal(ticketId, AbyssCorridorCatalog.ByMapId(mapId)?.TicketId);

    [Fact]
    public void Every_ticket_and_map_id_appears_once()
    {
        int[] ticketIds = AbyssCorridorCatalog.All.Select(c => c.TicketId).ToArray();
        int[] mapIds = AbyssCorridorCatalog.All.SelectMany(c => c.MapIds).ToArray();

        Assert.Equal(ticketIds.Length, ticketIds.Distinct().Count());
        Assert.Equal(mapIds.Length, mapIds.Distinct().Count());
        Assert.All(AbyssCorridorCatalog.All, c => Assert.Equal(c, AbyssCorridorCatalog.ById(c.TicketId)));
    }

    /// <summary>하층 001~003 / 중층 004~006 — the split the client draws with its 아이템 레벨 1000 vs 3000 entry
    /// gate. A corridor filed on the wrong layer sends the user to the wrong end of the abyss.</summary>
    [Fact]
    public void Tiers_follow_the_ticket_ranges()
    {
        Assert.All(
            AbyssCorridorCatalog.All.Where(c => c.TicketId <= 10_000_003),
            c => Assert.Equal(AbyssCorridorTier.Lower, c.Tier));
        Assert.All(
            AbyssCorridorCatalog.All.Where(c => c.TicketId >= 10_000_004),
            c => Assert.Equal(AbyssCorridorTier.Middle, c.Tier));
    }

    /// <summary>The client also ships six unwired stub tickets (10000007~10000012). They are deliberately not in
    /// the catalog, and asking about one has to be answerable rather than fatal — if the server ever activates
    /// them the store still keeps the record and only the label degrades.</summary>
    [Fact]
    public void Unwired_stub_tickets_have_no_entry_but_still_have_a_label()
    {
        Assert.Null(AbyssCorridorCatalog.ById(10_000_007));
        Assert.Null(AbyssCorridorCatalog.ByMapId(0));
        Assert.False(AbyssCorridorCatalog.IsCorridorMap(21));
        Assert.Contains("10000007", AbyssCorridorCatalog.NameFor(10_000_007));
    }
}

/// <summary>The 점령 cycle boundary a stored corridor reading is judged against: Wednesday and Saturday
/// 22:30 KST, when the usable window opens.</summary>
public class AbyssCorridorCycleTests
{
    private static long Kst(int year, int month, int day, int hour, int minute) =>
        new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.FromHours(9)).ToUnixTimeMilliseconds();

    [Theory]
    // Wednesday afternoon — the cycle that opened last Saturday is still the current one.
    [InlineData(2026, 8, 19, 18, 0, 2026, 8, 15, 22, 30)]
    // Wednesday 22:29, one minute before the handover: still last Saturday's.
    [InlineData(2026, 8, 19, 22, 29, 2026, 8, 15, 22, 30)]
    // Wednesday 22:31 — the new cycle has opened.
    [InlineData(2026, 8, 19, 22, 31, 2026, 8, 19, 22, 30)]
    // Monday morning, the 08-17 capture: last Saturday's cycle.
    [InlineData(2026, 8, 17, 8, 38, 2026, 8, 15, 22, 30)]
    // Saturday 21:00, before the handover: still Wednesday's.
    [InlineData(2026, 8, 22, 21, 0, 2026, 8, 19, 22, 30)]
    public void Cycle_starts_at_the_most_recent_wednesday_or_saturday_2230_kst(
        int y, int mo, int d, int h, int mi, int ey, int emo, int ed, int eh, int emi) =>
        Assert.Equal(Kst(ey, emo, ed, eh, emi), AbyssCorridorCycle.LastStartAtOrBefore(Kst(y, mo, d, h, mi)));

    [Fact]
    public void A_reading_from_before_the_last_handover_is_no_longer_current()
    {
        long now = Kst(2026, 8, 19, 23, 0);       // just after Wednesday's handover

        Assert.False(AbyssCorridorCycle.IsCurrentCycle(Kst(2026, 8, 19, 18, 0), now));
        Assert.True(AbyssCorridorCycle.IsCurrentCycle(Kst(2026, 8, 19, 22, 45), now));
        Assert.False(AbyssCorridorCycle.IsCurrentCycle(0, now));
    }

    /// <summary>A hand-edited settings file can carry any long at all; the panel must not come down over one.</summary>
    [Fact]
    public void An_impossible_timestamp_answers_zero_instead_of_throwing()
    {
        Assert.Equal(0, AbyssCorridorCycle.LastStartAtOrBefore(long.MinValue));
        Assert.Equal(0, AbyssCorridorCycle.LastStartAtOrBefore(long.MaxValue));
    }
}

public class AbyssCorridorStoreTests
{
    private const string Hash = "abc123";
    private const int Ticket = 10_000_002;

    private static long Kst(int y, int mo, int d, int h, int mi) =>
        new DateTimeOffset(y, mo, d, h, mi, 0, TimeSpan.FromHours(9)).ToUnixTimeMilliseconds();

    private static readonly long Entry = Kst(2026, 8, 19, 18, 8);   // inside the Sat 08-15 cycle
    private static readonly long Now = Kst(2026, 8, 19, 18, 20);

    [Fact]
    public void A_reading_round_trips_through_the_blob()
    {
        var store = AbyssCorridorStore.Parse(null);
        Assert.True(store.Upsert(Hash, Ticket, 130_000, Entry, markGranted: true));

        AbyssCorridorStore reloaded = AbyssCorridorStore.Parse(store.Serialize());

        Assert.Equal(130_000, reloaded.Standing(Hash, Ticket, Now));
    }

    /// <summary>Repeat broadcasts are the norm; the caller skips re-serializing when nothing moved.</summary>
    [Fact]
    public void An_identical_reading_reports_no_change()
    {
        var store = AbyssCorridorStore.Parse(null);
        Assert.True(store.Upsert(Hash, Ticket, 130_000, Entry, markGranted: true));
        Assert.False(store.Upsert(Hash, Ticket, 130_000, Entry, markGranted: true));
    }

    /// <summary>A running clock is projected at read time, never baked into the stored value — the same rule the
    /// 오드 regen follows, so re-reading cannot compound its own estimate.</summary>
    [Fact]
    public void A_running_clock_is_projected_at_read_time_and_floors_at_zero()
    {
        var store = AbyssCorridorStore.Parse(null);
        store.Upsert(Hash, Ticket, 130_000, Entry, markGranted: true, tickingSinceMs: Entry);

        Assert.Equal(130_000, store.Standing(Hash, Ticket, Entry));
        Assert.Equal(70_000, store.Standing(Hash, Ticket, Entry + 60_000));
        Assert.Equal(0, store.Standing(Hash, Ticket, Entry + 200_000));

        // stored, not projected
        Assert.Equal(130_000, store.Get(Hash, Ticket)!.Value.RemainingMs);
    }

    /// <summary>Leaving early is the only case the server never reports, so freezing the clock on exit is the
    /// entire mechanism for it: 60 s in, the corridor keeps the 70 s it still has.</summary>
    [Fact]
    public void Leaving_early_freezes_the_remaining_time()
    {
        var store = AbyssCorridorStore.Parse(null);
        store.Upsert(Hash, Ticket, 130_000, Entry, markGranted: true, tickingSinceMs: Entry);

        Assert.True(store.StopTicking(Hash, Entry + 60_000));

        Assert.Equal(70_000, store.Standing(Hash, Ticket, Entry + 60_000));
        Assert.Equal(70_000, store.Standing(Hash, Ticket, Entry + 600_000)); // no longer running
        Assert.False(store.StopTicking(Hash, Entry + 90_000));               // nothing left to stop
    }

    /// <summary>A reading taken before the last 점령전 says nothing about now: the server may have re-stocked
    /// the corridor, or the artifact may have changed hands. Either way it is not a claim to repeat.</summary>
    [Fact]
    public void A_reading_from_a_previous_cycle_reads_as_unknown()
    {
        var store = AbyssCorridorStore.Parse(null);
        store.Upsert(Hash, Ticket, 130_000, Kst(2026, 8, 15, 23, 0), markGranted: true);

        Assert.Equal(130_000, store.Standing(Hash, Ticket, Kst(2026, 8, 19, 20, 0)));  // same cycle
        Assert.Null(store.Standing(Hash, Ticket, Kst(2026, 8, 19, 23, 0)));            // after the handover
    }

    /// <summary>Zero without a grant behind it is not "다 썼다" — every corridor the faction does not hold reads
    /// zero too. Only a record that was seen holding time this cycle may be shown at all.</summary>
    [Fact]
    public void Zero_without_a_grant_this_cycle_is_not_a_claim()
    {
        var store = AbyssCorridorStore.Parse(null);
        store.Upsert(Hash, Ticket, 0, Entry, markGranted: false);

        Assert.Null(store.Standing(Hash, Ticket, Now));

        // ...but zero after the corridor was seen stocked is exactly the "spent" the panel should show.
        store.Upsert(Hash, Ticket, 130_000, Entry, markGranted: true);
        store.Upsert(Hash, Ticket, 0, Entry + 130_000, markGranted: true);
        Assert.Equal(0, store.Standing(Hash, Ticket, Now));
    }

    /// <summary>A grant stamp must not survive its own cycle — otherwise a corridor the faction lost keeps
    /// rendering as one it still holds.</summary>
    [Fact]
    public void A_grant_stamp_does_not_carry_across_a_handover()
    {
        var store = AbyssCorridorStore.Parse(null);
        store.Upsert(Hash, Ticket, 130_000, Kst(2026, 8, 15, 23, 0), markGranted: true);
        store.Upsert(Hash, Ticket, 0, Kst(2026, 8, 19, 23, 0), markGranted: false);

        Assert.Equal(0, store.Get(Hash, Ticket)!.Value.GrantedAtMs);
        Assert.Null(store.Standing(Hash, Ticket, Kst(2026, 8, 19, 23, 30)));
    }

    /// <summary>"이 캐릭터는 점령한 회랑이 없다" is only sayable after a login snapshot has been seen for it this
    /// cycle. Without the witness the panel has to stay quiet rather than imply an empty answer.</summary>
    [Fact]
    public void The_witness_is_what_separates_none_from_unwatched()
    {
        var store = AbyssCorridorStore.Parse(null);
        Assert.False(store.HasCycleWitness(Hash, Now));

        Assert.True(store.MarkWitness(Hash, Entry));
        Assert.True(store.HasCycleWitness(Hash, Now));
        Assert.False(store.MarkWitness(Hash, Entry - 1)); // an older snapshot changes nothing

        Assert.False(store.HasCycleWitness(Hash, Kst(2026, 8, 19, 23, 0))); // stale after the handover
    }

    [Fact]
    public void Forgetting_a_character_leaves_nothing_behind()
    {
        var store = AbyssCorridorStore.Parse(null);
        store.Upsert(Hash, Ticket, 130_000, Entry, markGranted: true);
        store.MarkWitness(Hash, Entry);

        Assert.True(store.RemoveAll([Hash]));

        Assert.Null(store.Standing(Hash, Ticket, Now));
        Assert.False(store.HasCycleWitness(Hash, Now));
        Assert.Equal(string.Empty, store.Serialize());
    }

    /// <summary>A ticket this build has no catalog entry for is kept and re-serialized, so shipping a corridor
    /// later cannot be undone by a user who rolls back once.</summary>
    [Fact]
    public void An_unknown_ticket_id_survives_a_round_trip()
    {
        var store = AbyssCorridorStore.Parse(null);
        store.Upsert(Hash, 10_000_011, 130_000, Entry, markGranted: true);

        Assert.Equal(130_000, AbyssCorridorStore.Parse(store.Serialize()).Standing(Hash, 10_000_011, Now));
    }

    /// <summary>A hand-edited or truncated blob must lose only the broken records.</summary>
    [Fact]
    public void Malformed_records_are_skipped_rather_than_fatal()
    {
        AbyssCorridorStore store = AbyssCorridorStore.Parse(
            $"garbage;{Hash},nope,1,2,3,4;;{Hash},{Ticket},130000,{Entry},{Entry},0");

        Assert.Equal(130_000, store.Standing(Hash, Ticket, Now));
    }

    [Fact]
    public void Unusable_arguments_are_refused()
    {
        var store = AbyssCorridorStore.Parse(null);

        Assert.False(store.Upsert(null, Ticket, 130_000, Entry, markGranted: true));
        Assert.False(store.Upsert("  ", Ticket, 130_000, Entry, markGranted: true));
        Assert.False(store.Upsert(Hash, 0, 130_000, Entry, markGranted: true));
        Assert.False(store.Upsert(Hash, Ticket, 130_000, 0, markGranted: true));
        Assert.Null(store.Standing(null, Ticket, Now));
    }

    /// <summary>The store is capped like its 오드 sibling; the oldest character goes first.</summary>
    [Fact]
    public void The_oldest_character_is_evicted_past_the_cap()
    {
        var store = AbyssCorridorStore.Parse(null);
        for (int i = 0; i <= AbyssCorridorStore.MaxCharacters; i++)
        {
            store.Upsert($"hash{i}", Ticket, 130_000, Entry + i, markGranted: true);
        }

        Assert.Null(store.Standing("hash0", Ticket, Now));
        Assert.Equal(130_000, store.Standing($"hash{AbyssCorridorStore.MaxCharacters}", Ticket, Now));
    }
}

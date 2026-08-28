using WaffleMeter.App.Core;
using WaffleMeter.Capture;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

/// <summary>
/// Spec for <see cref="AbyssArtifactStore"/> — the 어비스 아티팩트 점령 현황 the panel now hangs the corridor
/// chips off, in place of "was this character watched walking into the instance map".
///
/// <para>The two facts it joins are measured, and both are in <c>AbyssArtifactParserTests</c> as raw frames:
/// the broadcast says which SLOT holds each artifact, and the 점령 개수 abnormal says how many OUR side holds.
/// Neither answers alone — the slot is an index inside the current server matchup and flipped on one character
/// between 2026-08-23 and 2026-08-28.</para>
/// </summary>
public sealed class AbyssArtifactStoreTests
{
    private const int Server = 2003;

    private const string Hash = "h1";

    private const int Lower = AbyssArtifactBuffCatalog.LowerZoneId;   // 1001
    private const int Middle = AbyssArtifactBuffCatalog.MiddleZoneId; // 2001

    // The real 2026-08-28 window, from the frame: Wed 22:15:54 KST → Sat 22:05:00 KST.
    private const long CycleStart = 1_787_750_154_000;
    private const long CycleEnd = 1_788_008_700_000;
    private const long Now = 1_787_928_471_518; // the abyss zone load in that capture

    private static IReadOnlyList<AbyssArtifactHolding> Holdings(int zoneId, params int[] owners) =>
        owners.Select((side, i) => new AbyssArtifactHolding(zoneId + i, side)).ToList();

    /// <summary>The 08-28 state: slot 2 holds two artifacts in each zone, slot 1 holds one.</summary>
    private static AbyssArtifactStore Loaded(long observedAt = Now - 1000)
    {
        var store = AbyssArtifactStore.Parse(null);
        store.UpsertOwnership(Server, Lower, CycleStart, CycleEnd, Holdings(Lower, 2, 1, 2), observedAt);
        store.UpsertOwnership(Server, Middle, CycleStart, CycleEnd, Holdings(Middle, 2, 1, 2), observedAt);
        return store;
    }

    /// <summary>The abnormal picks the slot, and the slot names the corridors. This is the whole feature: the
    /// four corridors here are exactly the four the 0x610B snapshot in the same capture reported 130000 ms
    /// on.</summary>
    [Fact]
    public void The_occupation_count_picks_our_slot_and_names_the_corridors()
    {
        AbyssArtifactStore store = Loaded();
        store.UpsertCount(Hash, Lower, 2, Now);   // 12000262 — 하층 아티팩트 2개
        store.UpsertCount(Hash, Middle, 2, Now);  // 12000265 — 중층 아티팩트 2개

        Assert.Equal(2, store.SideFor(Server, Hash, Now));
        Assert.Equal(
            [10_000_001, 10_000_003, 10_000_004, 10_000_006],
            store.HeldTicketIds(Server, 2, Now));
    }

    /// <summary>One zone is enough. Three artifacts split between two slots can never tie, so a count settles
    /// the slot on its own — the second zone is a cross-check, not a requirement. It matters because a
    /// character that holds nothing in a zone gets no abnormal for it at all.</summary>
    [Fact]
    public void One_zone_with_a_count_settles_the_slot()
    {
        AbyssArtifactStore store = Loaded();
        store.UpsertCount(Hash, Middle, 1, Now);

        Assert.Equal(1, store.SideFor(Server, Hash, Now));
        Assert.Equal([10_000_002, 10_000_005], store.HeldTicketIds(Server, 1, Now));
    }

    /// <summary>Without a count there is no answer. The broadcast alone cannot say which slot is ours, and
    /// guessing would show the enemy's corridors half the time — the slot really did flip between the two
    /// captured cycles.</summary>
    [Fact]
    public void Ownership_without_a_count_settles_nothing()
    {
        Assert.Null(Loaded().SideFor(Server, Hash, Now));
    }

    /// <summary>Zones that name different slots are not reconciled — one of the two readings is stale or
    /// misparsed, and claiming corridors off the wrong slot is the failure this feature exists to avoid.</summary>
    [Fact]
    public void Zones_that_disagree_about_the_slot_answer_null()
    {
        AbyssArtifactStore store = Loaded();
        store.UpsertCount(Hash, Lower, 2, Now);   // → slot 2
        store.UpsertCount(Hash, Middle, 1, Now);  // → slot 1

        Assert.Null(store.SideFor(Server, Hash, Now));
    }

    /// <summary>A count taken before this 점령 주기 describes an occupation that has since been redealt. The
    /// broadcast's own window is the boundary — no schedule arithmetic, no grace period.</summary>
    [Fact]
    public void A_count_from_the_previous_cycle_is_not_used()
    {
        AbyssArtifactStore store = Loaded();
        store.UpsertCount(Hash, Lower, 2, CycleStart - 60_000);

        Assert.Null(store.SideFor(Server, Hash, Now));
    }

    /// <summary>Once the cycle the reading belongs to has ended, the reading is gone — including for the panel's
    /// "우리가 아무것도 점령 못했다" line, which may only be printed off a live reading.</summary>
    [Fact]
    public void Ownership_expires_exactly_at_the_cycle_end_the_server_stated()
    {
        AbyssArtifactStore store = Loaded();
        store.UpsertCount(Hash, Lower, 2, Now);

        Assert.True(store.HasOwnership(Server, CycleEnd - 1));
        Assert.Equal(2, store.SideFor(Server, Hash, CycleEnd - 1));

        Assert.False(store.HasOwnership(Server, CycleEnd));
        Assert.Null(store.SideFor(Server, Hash, CycleEnd));
        Assert.Empty(store.HeldTicketIds(Server, 2, CycleEnd));
    }

    /// <summary>A server answers only for itself. Two servers are matched into different abyss instances, so
    /// one server's occupation says nothing about another's.</summary>
    [Fact]
    public void One_servers_occupation_does_not_answer_for_another()
    {
        AbyssArtifactStore store = Loaded();
        store.UpsertCount(Hash, Lower, 2, Now);

        Assert.Null(store.SideFor(1003, Hash, Now));
        Assert.Empty(store.HeldTicketIds(1003, 2, Now));
        Assert.False(store.HasOwnership(1003, Now));
    }

    /// <summary>The blob round-trips, ownership and counts alike, so a meter restarted mid-cycle answers the
    /// same as one that watched the broadcast arrive. That is the case the whole persistence exists for: the
    /// abnormal is only applied while the character is in the abyss.</summary>
    [Fact]
    public void The_blob_round_trips()
    {
        AbyssArtifactStore store = Loaded();
        store.UpsertCount(Hash, Lower, 2, Now);
        store.UpsertCount(Hash, Middle, 2, Now);

        AbyssArtifactStore reloaded = AbyssArtifactStore.Parse(store.Serialize());

        Assert.Equal(2, reloaded.SideFor(Server, Hash, Now));
        Assert.Equal(
            [10_000_001, 10_000_003, 10_000_004, 10_000_006],
            reloaded.HeldTicketIds(Server, 2, Now));
        Assert.Equal(store.Serialize(), reloaded.Serialize());
    }

    /// <summary>A corrupt record is skipped, never thrown on, and never allowed to half-load a zone: a zone
    /// with two of three owners would silently understate the occupation.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("o,2003,1001,21,1,1,2")]                     // two owners, not three
    [InlineData("o,2003,1001,213,1,1,2")]                    // owner 3 does not exist
    [InlineData("o,2003,1001,212,1,1")]                      // six fields, not seven
    [InlineData("c,h1,1001,4,1,0,0")]                        // a zone has only three artifacts
    public void A_malformed_record_is_skipped(string blob)
    {
        AbyssArtifactStore store = AbyssArtifactStore.Parse(blob);

        Assert.False(store.HasOwnership(Server, Now));
        Assert.Null(store.SideFor(Server, Hash, Now));
    }

    /// <summary>Holdings that do not cover the zone are refused outright rather than filed with a hole.</summary>
    [Fact]
    public void Holdings_that_do_not_cover_the_zone_are_refused()
    {
        var store = AbyssArtifactStore.Parse(null);

        Assert.False(store.UpsertOwnership(
            Server, Lower, CycleStart, CycleEnd, Holdings(Middle, 2, 1, 2), Now));
        Assert.False(store.HasOwnership(Server, Now));
    }

    /// <summary>Re-filing the same answer reports no change, so the settings blob is not rewritten on every
    /// world-map open — the broadcast repeats verbatim each time.</summary>
    [Fact]
    public void Refiling_the_same_answer_changes_nothing()
    {
        AbyssArtifactStore store = Loaded();

        Assert.False(store.UpsertOwnership(
            Server, Lower, CycleStart, CycleEnd, Holdings(Lower, 2, 1, 2), Now));
        Assert.True(store.UpsertOwnership(
            Server, Lower, CycleStart, CycleEnd, Holdings(Lower, 1, 2, 2), Now));
    }

    /// <summary>Forgetting a character drops what it said about our slot but keeps what the server said about
    /// the abyss — the second is not that character's to take with it.</summary>
    [Fact]
    public void Forgetting_a_character_drops_its_count_but_not_the_servers_occupation()
    {
        AbyssArtifactStore store = Loaded();
        store.UpsertCount(Hash, Lower, 2, Now);

        Assert.True(store.RemoveAll([Hash]));
        Assert.Null(store.SideFor(Server, Hash, Now));
        Assert.True(store.HasOwnership(Server, Now));
    }

    /// <summary>End to end from the real frames: the 2026-08-28 broadcast plus that session's two abnormals
    /// resolve to the four corridors the ticket snapshot independently reported time on. This is the exact case
    /// the user reported as blank.</summary>
    [Fact]
    public void The_2026_08_28_capture_resolves_to_the_four_corridors_the_tickets_named()
    {
        byte[] packet = Convert.FromHexString(
            "9E0107E300000201D107000003002023373EA001000060989F4DA001000001E90300000300107F363EA0010000"
            + "60989F4DA001000006D1070000B0CA100002B0CA100000000000D2070000B1CA100001B1CA100000000000D3"
            + "070000B2CA100002B2CA100000000000E9030000F1C8100002F1C8100000000000EA030000F3C8100001F3C8"
            + "100000000000EB030000F5C8100002F5C8100000000000");

        var zones = new AbyssArtifactZone[AbyssArtifactParser.MaxZones];
        var holdings = new AbyssArtifactHolding[AbyssArtifactParser.MaxArtifacts];
        int count = AbyssArtifactParser.TryParse(packet, 4, true, zones, holdings, out int zoneCount);
        Assert.Equal(6, count);

        var store = AbyssArtifactStore.Parse(null);
        for (int z = 0; z < zoneCount; z++)
        {
            AbyssArtifactZone zone = zones[z];
            store.UpsertOwnership(
                Server,
                zone.ZoneId,
                zone.StartMs,
                zone.EndMs,
                holdings.Take(count).Where(h => h.ArtifactId / 1000 == zone.ZoneId / 1000).ToList(),
                Now);
        }

        // The two abnormals that arrived 0.30 s after the abyss load.
        AbyssArtifactBuffCatalog.TryResolve(12_000_262, out int lowerZone, out int lowerCount);
        AbyssArtifactBuffCatalog.TryResolve(12_000_265, out int middleZone, out int middleCount);
        store.UpsertCount(Hash, lowerZone, lowerCount, Now);
        store.UpsertCount(Hash, middleZone, middleCount, Now);

        Assert.Equal(
            [10_000_001, 10_000_003, 10_000_004, 10_000_006],
            store.HeldTicketIds(Server, store.SideFor(Server, Hash, Now)!.Value, Now));
    }

    /// <summary>The 2026-08-23 case, which is the one that cost a release. That character's side held ONE
    /// artifact per zone, and 유황나무 was not among them — yet the server was still reporting unspent 이용 시간
    /// on it three and three-quarter hours after the 점령전 that lost it. The occupation table refuses it
    /// whatever the ticket says, which is what the entry-only rule was reaching for.</summary>
    [Fact]
    public void The_2026_08_23_lost_corridor_is_not_offered()
    {
        const long satStart = 1_787_404_863_000; // 2026-08-22 Sat 22:21:03 KST
        const long satEnd = 1_787_749_800_000;   // 2026-08-26 Wed 22:10:00 KST
        const long sunday = 1_787_418_496_763;   // 2026-08-23 02:08 KST

        var store = AbyssArtifactStore.Parse(null);
        store.UpsertOwnership(Server, Lower, satStart, satEnd, Holdings(Lower, 2, 2, 1), sunday);
        store.UpsertOwnership(Server, Middle, satStart, satEnd, Holdings(Middle, 2, 2, 1), sunday);
        store.UpsertCount(Hash, Lower, 1, sunday);   // 12000261
        store.UpsertCount(Hash, Middle, 1, sunday);  // 12000264

        Assert.Equal(1, store.SideFor(Server, Hash, sunday));

        IReadOnlyList<int> held = store.HeldTicketIds(Server, 1, sunday);
        Assert.Equal([10_000_003, 10_000_006], held);
        Assert.DoesNotContain(10_000_002, held); // 유황나무 — the stale stock the panel used to believe
    }
}

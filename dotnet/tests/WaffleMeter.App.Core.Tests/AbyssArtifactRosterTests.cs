using WaffleMeter.App.Core;
using WaffleMeter.Capture;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

/// <summary>
/// Spec for the 어비스 회랑 chips once the 점령 현황 broadcast is on file — the fix for "어비스에 들어가도
/// 컨텐츠 관리에 점령한 회랑이 안 보인다" (2026-08-29).
///
/// <para>Before this, a chip needed the character to have been watched walking into the corridor's own instance
/// map. That proof is real but rare: the player enters the abyss far more often than the corridor, and the game
/// itself started showing the corridor's remaining time on abyss entry. The broadcast answers the occupation
/// question outright, so it is the primary source and the entry proof is the fallback for a session that has
/// not heard one.</para>
///
/// <para>The old rule's tests still stand next door in <c>AbyssCorridorRosterTests</c> and still pass — nothing
/// here weakens them, because a server with no broadcast on file takes exactly the old path.</para>
/// </summary>
public sealed class AbyssArtifactRosterTests
{
    private const string Hash = "h1";

    private const int Server = 2003;

    private const int Lower = AbyssArtifactBuffCatalog.LowerZoneId;
    private const int Middle = AbyssArtifactBuffCatalog.MiddleZoneId;

    // The real 2026-08-28 cycle, from the captured frame.
    private const long CycleStart = 1_787_750_154_000; // Wed 22:15:54 KST
    private const long CycleEnd = 1_788_008_700_000;   // Sat 22:05:00 KST
    private const long Now = 1_787_928_471_518;        // the abyss zone load

    private static AetherPerCharacterStore OneCharacter()
    {
        var store = AetherPerCharacterStore.Parse(null);
        store.Upsert(Hash, new AetherSnapshot(100, 0, Now - 60_000, "콘팡", Server));
        return store;
    }

    private static IReadOnlyList<AbyssArtifactHolding> Holdings(int zoneId, params int[] owners) =>
        owners.Select((side, i) => new AbyssArtifactHolding(zoneId + i, side)).ToList();

    /// <summary>The 08-28 state: our slot (2) holds the first and third artifact of each zone.</summary>
    private static AbyssArtifactStore Broadcast(bool withCount = true)
    {
        var store = AbyssArtifactStore.Parse(null);
        store.UpsertOwnership(Server, Lower, CycleStart, CycleEnd, Holdings(Lower, 2, 1, 2), Now - 1000);
        store.UpsertOwnership(Server, Middle, CycleStart, CycleEnd, Holdings(Middle, 2, 1, 2), Now - 1000);
        if (withCount)
        {
            store.UpsertCount(Hash, Lower, 2, Now);
            store.UpsertCount(Hash, Middle, 2, Now);
        }

        return store;
    }

    private static AetherRosterRow Build(AbyssCorridorStore? corridors, AbyssArtifactStore? artifacts) =>
        Assert.Single(AetherRoster.Build(
            OneCharacter(),
            currentHash: Hash,
            nowMs: Now,
            corridors: corridors ?? AbyssCorridorStore.Parse(null),
            artifacts: artifacts));

    /// <summary>THE FIX. Walking into the abyss — not into a corridor — is now enough, because the server states
    /// the occupation on arrival. Four chips, no entry proof anywhere.</summary>
    [Fact]
    public void The_occupation_broadcast_alone_puts_the_held_corridors_on_the_row()
    {
        AetherRosterRow row = Build(corridors: null, artifacts: Broadcast());

        Assert.Equal(
            [10_000_001, 10_000_003, 10_000_004, 10_000_006],
            row.CorridorCells.Select(c => c.Corridor.TicketId));
        Assert.All(row.CorridorCells, c => Assert.Equal(AbyssCorridorCatalog.FullGrantMs, c.RemainingMs));
        Assert.All(row.CorridorCells, c => Assert.True(c.Inferred));
    }

    /// <summary>A corridor our side does NOT hold stays off the row even when the server is still reporting
    /// unspent 이용 시간 on it. This is the 2026-08-23 defect (유황나무 shown 3h45m after the 점령전 that lost it)
    /// and the broadcast settles it without falling back on "did we walk in".</summary>
    [Fact]
    public void A_corridor_the_side_lost_is_refused_even_with_time_still_on_the_ticket()
    {
        var corridors = AbyssCorridorStore.Parse(null);
        corridors.Upsert(Hash, 10_000_002, 130_000, Now, markGranted: true);

        AetherRosterRow row = Build(corridors, Broadcast());

        Assert.DoesNotContain(10_000_002, row.CorridorCells.Select(c => c.Corridor.TicketId));
    }

    /// <summary>A reading this character actually has wins over the assumed full grant, and drops the "~".
    /// Otherwise a corridor the player just burned would keep reading 2:10.</summary>
    [Fact]
    public void An_own_reading_beats_the_assumed_full_grant()
    {
        var corridors = AbyssCorridorStore.Parse(null);
        corridors.Upsert(Hash, 10_000_001, 40_000, Now, markGranted: true);

        AetherRosterRow row = Build(corridors, Broadcast());

        AbyssCorridorCell cell = row.CorridorCells.Single(c => c.Corridor.TicketId == 10_000_001);
        Assert.Equal(40_000, cell.RemainingMs);
        Assert.False(cell.Inferred);
        Assert.True(row.CorridorCells.Single(c => c.Corridor.TicketId == 10_000_003).Inferred);
    }

    /// <summary>A corridor watched running down to zero reads 0:00 rather than being dropped — it is held, it is
    /// just spent, and hiding it would read as "we lost it".</summary>
    [Fact]
    public void A_spent_corridor_still_shows_as_held()
    {
        var corridors = AbyssCorridorStore.Parse(null);
        corridors.Upsert(Hash, 10_000_004, 0, Now, markGranted: false);

        AetherRosterRow row = Build(corridors, Broadcast());

        AbyssCorridorCell cell = row.CorridorCells.Single(c => c.Corridor.TicketId == 10_000_004);
        Assert.True(cell.Spent);
        Assert.Equal(4, row.CorridorCells.Count);
    }

    /// <summary>Ownership with no 점령 개수 to read it by is not an answer, so the row falls back to the entry
    /// proof exactly as before. Guessing the slot would show the enemy's corridors half the time.</summary>
    [Fact]
    public void Ownership_without_a_count_falls_back_to_the_entry_proof()
    {
        var corridors = AbyssCorridorStore.Parse(null);
        corridors.MarkEntered(Hash, 10_000_006, Now);
        corridors.Upsert(Hash, 10_000_006, 130_000, Now, markGranted: true);

        AetherRosterRow row = Build(corridors, Broadcast(withCount: false));

        Assert.Equal([10_000_006], row.CorridorCells.Select(c => c.Corridor.TicketId));
    }

    /// <summary>No broadcast at all — an install that has not been to the abyss since the update — behaves
    /// exactly as the shipped build does. The old path is untouched, not merely usually-unreached.</summary>
    [Fact]
    public void With_no_broadcast_the_entry_proof_still_drives_the_row()
    {
        var corridors = AbyssCorridorStore.Parse(null);
        corridors.MarkEntered(Hash, 10_000_002, Now);
        corridors.Upsert(Hash, 10_000_002, 130_000, Now, markGranted: true);

        AetherRosterRow withNull = Build(corridors, artifacts: null);
        AetherRosterRow withEmpty = Build(corridors, AbyssArtifactStore.Parse(null));

        Assert.Equal([10_000_002], withNull.CorridorCells.Select(c => c.Corridor.TicketId));
        Assert.Equal([10_000_002], withEmpty.CorridorCells.Select(c => c.Corridor.TicketId));
    }

    /// <summary>Once the broadcast and the count are both on file, an empty list is a FACT — our side holds
    /// nothing — so the row is allowed to say so. Without them it is only silence.</summary>
    [Fact]
    public void The_broadcast_licenses_the_empty_line()
    {
        var store = AbyssArtifactStore.Parse(null);
        store.UpsertOwnership(Server, Lower, CycleStart, CycleEnd, Holdings(Lower, 1, 1, 1), Now - 1000);
        store.UpsertOwnership(Server, Middle, CycleStart, CycleEnd, Holdings(Middle, 1, 1, 2), Now - 1000);
        store.UpsertCount(Hash, Middle, 1, Now); // we hold one middle artifact and no lower ones

        AetherRosterRow row = Build(corridors: null, artifacts: store);

        Assert.Equal([10_000_006], row.CorridorCells.Select(c => c.Corridor.TicketId));
        Assert.True(row.CorridorsKnown);
    }

    /// <summary>The broadcast expires with the cycle the server stamped on it, and the row goes back to the
    /// entry proof rather than carrying last week's occupation forward.</summary>
    [Fact]
    public void After_the_cycle_ends_the_broadcast_stops_answering()
    {
        AetherRosterRow row = Assert.Single(AetherRoster.Build(
            OneCharacter(),
            currentHash: Hash,
            nowMs: CycleEnd + 1,
            corridors: AbyssCorridorStore.Parse(null),
            artifacts: Broadcast()));

        Assert.Empty(row.CorridorCells);
        Assert.False(row.CorridorsKnown);
    }

    /// <summary>An occupation is a fact about the 진영, and a server id IS a 진영, so one character's abnormal
    /// answers for its siblings — the same reasoning the entry union already ran on. Here the alt never
    /// entered the abyss at all.</summary>
    [Fact]
    public void A_sibling_on_the_same_server_inherits_the_occupation()
    {
        var characters = AetherPerCharacterStore.Parse(null);
        characters.Upsert(Hash, new AetherSnapshot(100, 0, Now - 60_000, "콘팡", Server));
        characters.Upsert("h2", new AetherSnapshot(80, 0, Now - 30_000, "부캐", Server));

        IReadOnlyList<AetherRosterRow> rows = AetherRoster.Build(
            characters, currentHash: Hash, nowMs: Now, artifacts: Broadcast());

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(
            [10_000_001, 10_000_003, 10_000_004, 10_000_006],
            r.CorridorCells.Select(c => c.Corridor.TicketId)));
    }

    /// <summary>A character on a different server is matched into a different abyss and must not inherit
    /// anything — the one place the server-wide spread has to stop.</summary>
    [Fact]
    public void A_character_on_another_server_inherits_nothing()
    {
        var characters = AetherPerCharacterStore.Parse(null);
        characters.Upsert(Hash, new AetherSnapshot(100, 0, Now - 60_000, "콘팡", Server));
        characters.Upsert("h3", new AetherSnapshot(80, 0, Now - 30_000, "타서버", 1003));

        IReadOnlyList<AetherRosterRow> rows = AetherRoster.Build(
            characters, currentHash: Hash, nowMs: Now, artifacts: Broadcast());

        Assert.Empty(rows.Single(r => r.IdentityHash == "h3").CorridorCells);
        Assert.Equal(4, rows.Single(r => r.IdentityHash == Hash).CorridorCells.Count);
    }
}

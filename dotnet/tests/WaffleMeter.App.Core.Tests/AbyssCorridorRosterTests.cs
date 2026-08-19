using WaffleMeter.App.Core;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

/// <summary>
/// Spec for the 어비스 회랑 chips on a 컨텐츠 관리 row. The rule that matters is the one the user asked for:
/// show ONLY the corridors this character's side actually captured — and be able to tell that apart from
/// "we have not watched this character", because the wire reports both as zero.
/// </summary>
public sealed class AbyssCorridorRosterTests
{
    private const string Hash = "h1";

    private static long Kst(int y, int mo, int d, int h, int mi) =>
        new DateTimeOffset(y, mo, d, h, mi, 0, TimeSpan.FromHours(9)).ToUnixTimeMilliseconds();

    private static readonly long Entry = Kst(2026, 8, 19, 18, 8);
    private static readonly long Now = Kst(2026, 8, 19, 18, 20);

    private static AetherPerCharacterStore OneCharacter()
    {
        var store = AetherPerCharacterStore.Parse(null);
        store.Upsert(Hash, new AetherSnapshot(100, 0, Entry, "파일러", 2003));
        return store;
    }

    private static AetherRosterRow Build(AbyssCorridorStore corridors) =>
        Assert.Single(AetherRoster.Build(OneCharacter(), nowMs: Now, corridors: corridors));

    /// <summary>The three corridors measured on 2026-08-19: two 중층 and one 하층, all spent. Only those three
    /// get a chip — the other three never held time for this character, and inventing a chip for one would be
    /// telling the user their faction holds an artifact it does not.</summary>
    [Fact]
    public void Only_corridors_seen_holding_time_get_a_chip()
    {
        var corridors = AbyssCorridorStore.Parse(null);
        corridors.Upsert(Hash, 10_000_002, 130_000, Entry, markGranted: true);
        corridors.Upsert(Hash, 10_000_004, 0, Entry + 200_000, markGranted: true);

        AetherRosterRow row = Build(corridors);

        Assert.Equal(
            [10_000_002, 10_000_004],
            row.CorridorCells.Select(c => c.Corridor.TicketId));
        Assert.Equal(130_000, row.CorridorCells[0].RemainingMs);
        Assert.False(row.CorridorCells[0].Spent);
        Assert.True(row.CorridorCells[1].Spent);
    }

    /// <summary>Cells come out in catalog order (하층 first) no matter what order the packets arrived in, so the
    /// chips do not reshuffle between refreshes.</summary>
    [Fact]
    public void Chips_are_listed_in_catalog_order()
    {
        var corridors = AbyssCorridorStore.Parse(null);
        corridors.Upsert(Hash, 10_000_006, 130_000, Entry + 2, markGranted: true);
        corridors.Upsert(Hash, 10_000_001, 130_000, Entry + 1, markGranted: true);

        Assert.Equal(
            [10_000_001, 10_000_006],
            Build(corridors).CorridorCells.Select(c => c.Corridor.TicketId));
    }

    /// <summary>A running clock is reported as running, so the panel can keep redrawing it; a spent one is not.</summary>
    [Fact]
    public void A_corridor_being_stood_in_reports_as_ticking()
    {
        var corridors = AbyssCorridorStore.Parse(null);
        corridors.Upsert(Hash, 10_000_002, 130_000, Now - 60_000, markGranted: true, tickingSinceMs: Now - 60_000);

        AbyssCorridorCell cell = Assert.Single(Build(corridors).CorridorCells);

        Assert.True(cell.Ticking);
        Assert.Equal(70_000, cell.RemainingMs);
    }

    /// <summary>"어비스 회랑 없음" may only be said after a login snapshot for this character has been seen this
    /// cycle. Without it the row stays silent rather than implying an answer it does not have.</summary>
    [Fact]
    public void Empty_is_only_claimed_when_a_snapshot_backs_it()
    {
        Assert.False(Build(AbyssCorridorStore.Parse(null)).CorridorsKnown);

        var witnessed = AbyssCorridorStore.Parse(null);
        witnessed.MarkWitness(Hash, Entry);

        AetherRosterRow row = Build(witnessed);
        Assert.True(row.CorridorsKnown);
        Assert.Empty(row.CorridorCells);
    }

    /// <summary>A row built without a corridor store at all (an older settings file, or the store not wired)
    /// must render as "unknown", never as "none".</summary>
    [Fact]
    public void No_store_reads_as_unknown_rather_than_none()
    {
        AetherRosterRow row = Assert.Single(AetherRoster.Build(OneCharacter(), nowMs: Now));

        Assert.Empty(row.CorridorCells);
        Assert.False(row.CorridorsKnown);
    }
}

using WaffleMeter.App.Core;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

/// <summary>
/// Spec for the 어비스 회랑 chips on a 컨텐츠 관리 row. The rule that matters: show the corridors this
/// character's SIDE was watched holding — measured on this character where we have that, and inherited from a
/// character beside it on the same server where we do not — and never invent one from a bare zero, because the
/// wire sends the same zero for 미점령, 소진 and 미방문 alike.
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

    private static AetherRosterRow Build(AbyssCorridorStore corridors, bool current = true) =>
        Assert.Single(AetherRoster.Build(
            OneCharacter(), currentHash: current ? Hash : null, nowMs: Now, corridors: corridors));

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

    /// <summary>"지금 입장 중" is a claim about the character being played. A clock that outlived its visit —
    /// the meter was closed inside a corridor, or the user switched characters — must not light up a row the
    /// user is not even controlling.</summary>
    [Fact]
    public void Only_the_character_being_played_can_report_as_being_inside()
    {
        var corridors = AbyssCorridorStore.Parse(null);
        corridors.Upsert(Hash, 10_000_002, 130_000, Now - 60_000, markGranted: true, tickingSinceMs: Now - 60_000);

        AbyssCorridorCell cell = Assert.Single(Build(corridors, current: false).CorridorCells);

        Assert.False(cell.Ticking);
        Assert.Equal(70_000, cell.RemainingMs);
    }

    /// <summary>"어비스 회랑 기록 없음" may only be said after a login snapshot for this character has been seen this
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

    private const string Sibling = "h2";
    private const string Stranger = "h3";

    /// <summary>Two characters on 2003 (마족) and one on 1001 (천족) — the server id is the 진영, so the third
    /// belongs to a different occupation entirely.</summary>
    private static AetherPerCharacterStore ThreeCharacters()
    {
        AetherPerCharacterStore store = OneCharacter();
        store.Upsert(Sibling, new AetherSnapshot(60, 0, Entry, "콘팡", 2003));
        store.Upsert(Stranger, new AetherSnapshot(70, 0, Entry, "남의서버", 1001));
        return store;
    }

    private static AetherRosterRow RowFor(AbyssCorridorStore corridors, string hash) =>
        AetherRoster.Build(ThreeCharacters(), currentHash: Hash, nowMs: Now, corridors: corridors)
            .Single(r => string.Equals(r.IdentityHash, hash, StringComparison.Ordinal));

    /// <summary>The 2026-08-20 measurement, as a spec: five characters on server 2003 reported zero on every
    /// corridor within an hour of a sixth reporting the full grant on two, all of them a day after the 점령전.
    /// The zeros are therefore "이 캐릭터는 점령 후 어비스에 안 갔다", not "우리 진영은 못 뺏었다" — so a
    /// character that has never reported a corridor still gets the ones its server was seen holding, at full
    /// time, because time it never entered a corridor to spend is time it still has.
    /// <para>Confirmed live 2026-08-21: a character whose town snapshot read zero everywhere was walked into the
    /// abyss, and the corridor it had never entered came back at the full grant — the number this test fixes is
    /// the number the server went on to give.</para></summary>
    [Fact]
    public void A_corridor_seen_on_one_character_is_offered_to_its_server_siblings()
    {
        var corridors = AbyssCorridorStore.Parse(null);
        corridors.Upsert(Sibling, 10_000_002, 130_000, Entry, markGranted: true);
        corridors.MarkWitness(Hash, Entry + 60_000);

        AbyssCorridorCell cell = Assert.Single(RowFor(corridors, Hash).CorridorCells);

        Assert.Equal(10_000_002, cell.Corridor.TicketId);
        Assert.Equal(AbyssCorridorCatalog.FullGrantMs, cell.RemainingMs);
        Assert.True(cell.Inferred);
        Assert.False(cell.Spent);

        // Nobody is standing in a corridor we only inferred — the clock belongs to the character being played.
        Assert.False(cell.Ticking);
    }

    /// <summary>The character that actually reported the corridor keeps reporting it as measured, not as a
    /// guess — otherwise the one row the meter is sure of would be labelled the same as the ones it assumed.</summary>
    [Fact]
    public void The_character_that_was_watched_is_not_marked_as_inferred()
    {
        var corridors = AbyssCorridorStore.Parse(null);
        corridors.Upsert(Sibling, 10_000_002, 130_000, Entry, markGranted: true);

        Assert.False(Assert.Single(RowFor(corridors, Sibling).CorridorCells).Inferred);
    }

    /// <summary>A reading taken ON this character always wins, including a zero: it was measured here, and
    /// replacing it with the server's optimism would tell a user who just spent a corridor that it is full.</summary>
    [Fact]
    public void A_characters_own_reading_wins_over_its_servers()
    {
        var corridors = AbyssCorridorStore.Parse(null);
        corridors.Upsert(Sibling, 10_000_002, 130_000, Entry, markGranted: true);
        corridors.Upsert(Hash, 10_000_002, 0, Entry + 130_000, markGranted: true);

        AbyssCorridorCell cell = Assert.Single(RowFor(corridors, Hash).CorridorCells);

        Assert.True(cell.Spent);
        Assert.False(cell.Inferred);
    }

    /// <summary>Different servers are different 진영 and can be matched into different abyss instances, so one
    /// never answers for the other — 1001 is 천족 and 2003 마족.</summary>
    [Fact]
    public void One_server_never_answers_for_another()
    {
        var corridors = AbyssCorridorStore.Parse(null);
        corridors.Upsert(Sibling, 10_000_002, 130_000, Entry, markGranted: true);

        Assert.Empty(RowFor(corridors, Stranger).CorridorCells);
    }

    /// <summary>Last occupation's sighting is not this one's answer. The cycle filter that retires a character's
    /// own record has to retire it as a donor too, or a corridor the side lost on Saturday would be handed to
    /// every other character on the server for another three days.</summary>
    [Fact]
    public void Evidence_from_a_previous_occupation_is_not_handed_out()
    {
        var corridors = AbyssCorridorStore.Parse(null);
        corridors.Upsert(Sibling, 10_000_002, 130_000, Kst(2026, 8, 15, 20, 0), markGranted: true);

        Assert.Empty(RowFor(corridors, Hash).CorridorCells);
    }

    /// <summary>Each character only materialises the corridors it personally walked into, so each is a lower
    /// bound on what the side holds; the union of them is the closest thing to the occupation result.</summary>
    [Fact]
    public void Every_corridor_the_server_was_seen_holding_is_offered()
    {
        var corridors = AbyssCorridorStore.Parse(null);
        corridors.Upsert(Sibling, 10_000_006, 130_000, Entry, markGranted: true);
        corridors.Upsert(Hash, 10_000_002, 0, Entry, markGranted: true);

        Assert.Equal(
            [10_000_002, 10_000_006],
            RowFor(corridors, Hash).CorridorCells.Select(c => c.Corridor.TicketId));
        Assert.Equal([false, true], RowFor(corridors, Hash).CorridorCells.Select(c => c.Inferred));
    }

    /// <summary>A character whose server is unknown (a record written before the id was stored, or the
    /// 2026-07-30 identity corruption) has no side to inherit from, and must not be lumped in with everyone
    /// else's.</summary>
    [Fact]
    public void A_character_with_no_server_inherits_nothing()
    {
        var store = AetherPerCharacterStore.Parse(null);
        store.Upsert(Sibling, new AetherSnapshot(60, 0, Entry, "콘팡", 2003));
        store.Upsert(Hash, new AetherSnapshot(10, 0, Entry, "서버모름"));

        var corridors = AbyssCorridorStore.Parse(null);
        corridors.Upsert(Sibling, 10_000_002, 130_000, Entry, markGranted: true);

        AetherRosterRow row = AetherRoster.Build(store, nowMs: Now, corridors: corridors)
            .Single(r => string.Equals(r.IdentityHash, Hash, StringComparison.Ordinal));

        Assert.Empty(row.CorridorCells);
    }
}

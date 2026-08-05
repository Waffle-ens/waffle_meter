using WaffleMeter.App.Core;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

/// <summary>
/// Spec for the 오드 목록 rows: which characters appear, how they get named (the store is keyed by a one-way
/// hash), and the order they are listed in.
/// </summary>
public sealed class AetherRosterTests
{
    private static AetherPerCharacterStore StoreWith(params (string Hash, AetherSnapshot Snapshot)[] entries)
    {
        var store = AetherPerCharacterStore.Parse(null);
        foreach ((string hash, AetherSnapshot snapshot) in entries)
        {
            store.Upsert(hash, snapshot);
        }

        return store;
    }

    [Fact]
    public void Names_a_character_from_its_own_record()
    {
        AetherPerCharacterStore store = StoreWith(("h1", new AetherSnapshot(120, 30, 1000, "와플", 3)));

        AetherRosterRow row = Assert.Single(AetherRoster.Build(store));

        Assert.StartsWith("와플", row.Label);
        Assert.Equal(120, row.Base);
        Assert.Equal(30, row.Bonus);
        Assert.Equal(150, row.Total);
        Assert.Equal("120(+30)", row.AetherText);
    }

    /// <summary>Records written before the nickname was stored still have to show — the consent list is the
    /// fallback name source.</summary>
    [Fact]
    public void Falls_back_to_the_consent_list_for_a_nameless_record()
    {
        AetherPerCharacterStore store = StoreWith(("h1", new AetherSnapshot(90, 0, 1000)));

        AetherRosterRow row = Assert.Single(AetherRoster.Build(
            store, [new AetherRosterName("h1", "옛캐릭", 3, "검성")]));

        Assert.StartsWith("옛캐릭", row.Label);
        Assert.Equal("검성", row.SubLabel);
        Assert.Equal("90", row.AetherText); // no bonus half when there is none
    }

    /// <summary>The record's own nickname is written from the live executor every broadcast, so it wins over a
    /// consent entry that may predate a rename.</summary>
    [Fact]
    public void The_records_own_nickname_wins_over_the_consent_list()
    {
        AetherPerCharacterStore store = StoreWith(("h1", new AetherSnapshot(10, 0, 1000, "새이름", 3)));

        AetherRosterRow row = Assert.Single(AetherRoster.Build(
            store, [new AetherRosterName("h1", "옛이름", 3, null)]));

        Assert.StartsWith("새이름", row.Label);
    }

    /// <summary>A balance with no name anywhere still shows: the number is the point of the list.</summary>
    [Fact]
    public void An_unnamed_character_still_gets_a_row()
    {
        AetherPerCharacterStore store = StoreWith(("h1", new AetherSnapshot(55, 5, 1000)));

        AetherRosterRow row = Assert.Single(AetherRoster.Build(store));

        Assert.Equal("이름 없는 캐릭터", row.Label);
        Assert.Equal(60, row.Total);
    }

    /// <summary>Deliberately NOT gated on consent state — this list reports a local balance and has nothing to
    /// do with uploading, unlike the settings screen's character-management list.</summary>
    [Fact]
    public void Lists_every_remembered_character_regardless_of_consent()
    {
        AetherPerCharacterStore store = StoreWith(
            ("consented", new AetherSnapshot(10, 0, 3000, "동의함", 3)),
            ("never-asked", new AetherSnapshot(20, 0, 2000, "미동의", 3)));

        var rows = AetherRoster.Build(store, [new AetherRosterName("consented", "동의함", 3, null)]);

        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void Current_character_first_then_most_recently_seen()
    {
        AetherPerCharacterStore store = StoreWith(
            ("old", new AetherSnapshot(1, 0, 1000, "예전", 3)),
            ("newest", new AetherSnapshot(2, 0, 3000, "최근", 3)),
            ("current", new AetherSnapshot(3, 0, 2000, "지금", 3)));

        var rows = AetherRoster.Build(store, names: null, currentHash: "current");

        Assert.Equal(["current", "newest", "old"], rows.Select(r => r.IdentityHash));
        Assert.True(rows[0].IsCurrent);
        Assert.False(rows[1].IsCurrent);
    }

    [Fact]
    public void An_empty_store_produces_no_rows()
    {
        Assert.Empty(AetherRoster.Build(AetherPerCharacterStore.Parse(null)));
    }
}

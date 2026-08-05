using WaffleMeter.App.Core;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

/// <summary>Spec for remembering each character's last-seen 오드 balance across the flat settings string.</summary>
public sealed class AetherPerCharacterStoreTests
{
    [Fact]
    public void Round_trips_a_characters_balance_by_identity_hash()
    {
        var store = AetherPerCharacterStore.Parse(null);
        Assert.True(store.Upsert("hashA", new AetherSnapshot(120, 30, 1000)));

        AetherPerCharacterStore reloaded = AetherPerCharacterStore.Parse(store.Serialize());
        AetherSnapshot? got = reloaded.Get("hashA");
        Assert.NotNull(got);
        Assert.Equal(new AetherSnapshot(120, 30, 1000), got);
        Assert.Equal(150, got!.Value.Total); // spendable total is derived, never stored
        Assert.Null(reloaded.Get("unknown"));
    }

    [Fact]
    public void Upsert_replaces_the_same_characters_value()
    {
        var store = AetherPerCharacterStore.Parse(null);
        store.Upsert("h", new AetherSnapshot(10, 0, 1));
        store.Upsert("h", new AetherSnapshot(200, 40, 2));

        Assert.Equal(new AetherSnapshot(200, 40, 2), store.Get("h"));
        Assert.Single(AetherPerCharacterStore.Parse(store.Serialize()).Serialize().Split(';'));
    }

    [Fact]
    public void A_blank_hash_is_rejected_without_corrupting_the_store()
    {
        var store = AetherPerCharacterStore.Parse(null);
        Assert.False(store.Upsert("", new AetherSnapshot(1, 1, 1)));
        Assert.False(store.Upsert(null, new AetherSnapshot(1, 1, 1)));
        Assert.Equal(string.Empty, store.Serialize());
    }

    [Fact]
    public void Evicts_the_oldest_once_past_the_cap()
    {
        var store = AetherPerCharacterStore.Parse(null);
        for (int i = 0; i < AetherPerCharacterStore.MaxCharacters + 5; i++)
        {
            store.Upsert($"h{i}", new AetherSnapshot(i, 0, i)); // SavedAtMs = i, so h0 is oldest
        }

        Assert.Null(store.Get("h0"));  // oldest evicted
        Assert.NotNull(store.Get($"h{AetherPerCharacterStore.MaxCharacters + 4}")); // newest kept
        Assert.Equal(AetherPerCharacterStore.MaxCharacters, store.Serialize().Split(';').Length);
    }

    [Fact]
    public void Malformed_records_are_skipped_not_thrown()
    {
        AetherPerCharacterStore store = AetherPerCharacterStore.Parse("garbage;h,1,2,3;short,1,2;h2,x,y,z");
        Assert.Equal(new AetherSnapshot(1, 2, 3), store.Get("h"));
        Assert.Null(store.Get("short"));
        Assert.Null(store.Get("h2"));
    }

    /// <summary>Pre-2026-07-30 records carried a stored total as a fifth field. Their 자연회복/추가 split was
    /// written while the parser mis-read the single-pool packet, so they must be DROPPED rather than migrated —
    /// each character's chip refills from that character's next live broadcast.</summary>
    [Fact]
    public void Legacy_five_field_records_are_dropped_not_migrated()
    {
        AetherPerCharacterStore store = AetherPerCharacterStore.Parse("old,20,590,610,1000;new,470,610,1000");

        Assert.Null(store.Get("old"));
        Assert.Equal(new AetherSnapshot(470, 610, 1000), store.Get("new"));
    }

    /// <summary>기동 시 위생 정리가 쓰는 경로: 존재할 수 없는 신원으로 남은 오드 기록을 지운다. 2026-07-30
    /// 사고에서는 server 47200짜리 가짜 캐릭터 해시 밑에 오드가 기록됐다.</summary>
    [Fact]
    public void RemoveAll_drops_only_the_named_characters()
    {
        var store = AetherPerCharacterStore.Parse(null);
        store.Upsert("real", new AetherSnapshot(15, 1330, 2));
        store.Upsert("garbage", new AetherSnapshot(15, 1330, 3));

        Assert.True(store.RemoveAll(["garbage", "never-seen"]));

        Assert.Null(store.Get("garbage"));
        Assert.NotNull(store.Get("real"));
    }

    /// <summary>The store's key is a one-way hash, so the 오드 목록 can only name a character from the record
    /// itself or from a consent entry — and a character with no consent decision has no consent entry at all.
    /// The nickname is Base64'd on the wire because <c>,</c> and <c>;</c> are the separators.</summary>
    [Fact]
    public void Round_trips_the_character_name_and_server()
    {
        var store = AetherPerCharacterStore.Parse(null);
        store.Upsert("h", new AetherSnapshot(120, 30, 1000, "와플, 님;)", 3));

        AetherSnapshot? got = AetherPerCharacterStore.Parse(store.Serialize()).Get("h");

        Assert.Equal("와플, 님;)", got!.Value.Nickname);
        Assert.Equal(3, got.Value.Server);
        Assert.Equal(120, got.Value.Base);
        Assert.Equal(30, got.Value.Bonus);
    }

    /// <summary>A record with no name known yet stays in the original 4-field shape, so an install that has
    /// never seen one writes byte-identical settings to what earlier versions wrote.</summary>
    [Fact]
    public void A_nameless_record_keeps_the_four_field_form()
    {
        var store = AetherPerCharacterStore.Parse(null);
        store.Upsert("h", new AetherSnapshot(120, 30, 1000));

        Assert.Equal("h,120,30,1000", store.Serialize());
    }

    /// <summary>Field count alone separates the three formats: 4 = current-without-name, 5 = the bad
    /// pre-2026-07-30 records (dropped), 6 = current-with-name.</summary>
    [Fact]
    public void The_named_form_does_not_collide_with_the_dropped_legacy_form()
    {
        var named = AetherPerCharacterStore.Parse(null);
        named.Upsert("new", new AetherSnapshot(470, 610, 1000, "이름", 3));

        AetherPerCharacterStore store = AetherPerCharacterStore.Parse(
            "old,20,590,610,1000;" + named.Serialize());

        Assert.Null(store.Get("old"));
        Assert.Equal("이름", store.Get("new")!.Value.Nickname);
    }

    [Fact]
    public void A_corrupt_name_field_costs_only_the_name()
    {
        AetherPerCharacterStore store = AetherPerCharacterStore.Parse("h,1,2,3,4,!!!not-base64!!!");

        AetherSnapshot? got = store.Get("h");
        Assert.NotNull(got);
        Assert.Null(got!.Value.Nickname);
        Assert.Equal(1, got.Value.Base);
    }

    [Fact]
    public void All_lists_every_character_newest_first()
    {
        var store = AetherPerCharacterStore.Parse(null);
        store.Upsert("a", new AetherSnapshot(1, 0, 100));
        store.Upsert("b", new AetherSnapshot(2, 0, 300));
        store.Upsert("c", new AetherSnapshot(3, 0, 200));

        Assert.Equal(["b", "c", "a"], store.All().Select(kv => kv.Key));
    }

    [Fact]
    public void RemoveAll_reports_false_when_nothing_matched()
    {
        var store = AetherPerCharacterStore.Parse(null);
        store.Upsert("real", new AetherSnapshot(1, 2, 4));

        Assert.False(store.RemoveAll(["never-seen", "", "  "]));
        Assert.NotNull(store.Get("real"));
    }
}

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

    [Fact]
    public void RemoveAll_reports_false_when_nothing_matched()
    {
        var store = AetherPerCharacterStore.Parse(null);
        store.Upsert("real", new AetherSnapshot(1, 2, 4));

        Assert.False(store.RemoveAll(["never-seen", "", "  "]));
        Assert.NotNull(store.Get("real"));
    }
}

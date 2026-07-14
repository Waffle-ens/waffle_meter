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
        Assert.True(store.Upsert("hashA", new AetherSnapshot(120, 30, 150, 1000)));

        AetherPerCharacterStore reloaded = AetherPerCharacterStore.Parse(store.Serialize());
        AetherSnapshot? got = reloaded.Get("hashA");
        Assert.NotNull(got);
        Assert.Equal(new AetherSnapshot(120, 30, 150, 1000), got);
        Assert.Null(reloaded.Get("unknown"));
    }

    [Fact]
    public void Upsert_replaces_the_same_characters_value()
    {
        var store = AetherPerCharacterStore.Parse(null);
        store.Upsert("h", new AetherSnapshot(10, 0, 10, 1));
        store.Upsert("h", new AetherSnapshot(200, 40, 240, 2));

        Assert.Equal(new AetherSnapshot(200, 40, 240, 2), store.Get("h"));
        Assert.Single(AetherPerCharacterStore.Parse(store.Serialize()).Serialize().Split(';'));
    }

    [Fact]
    public void A_blank_hash_is_rejected_without_corrupting_the_store()
    {
        var store = AetherPerCharacterStore.Parse(null);
        Assert.False(store.Upsert("", new AetherSnapshot(1, 1, 2, 1)));
        Assert.False(store.Upsert(null, new AetherSnapshot(1, 1, 2, 1)));
        Assert.Equal(string.Empty, store.Serialize());
    }

    [Fact]
    public void Evicts_the_oldest_once_past_the_cap()
    {
        var store = AetherPerCharacterStore.Parse(null);
        for (int i = 0; i < AetherPerCharacterStore.MaxCharacters + 5; i++)
        {
            store.Upsert($"h{i}", new AetherSnapshot(i, 0, i, i)); // SavedAtMs = i, so h0 is oldest
        }

        Assert.Null(store.Get("h0"));  // oldest evicted
        Assert.NotNull(store.Get($"h{AetherPerCharacterStore.MaxCharacters + 4}")); // newest kept
        Assert.Equal(AetherPerCharacterStore.MaxCharacters, store.Serialize().Split(';').Length);
    }

    [Fact]
    public void Malformed_records_are_skipped_not_thrown()
    {
        AetherPerCharacterStore store = AetherPerCharacterStore.Parse("garbage;h,1,2,3,4;short,1,2;h2,x,y,z,w");
        Assert.Equal(new AetherSnapshot(1, 2, 3, 4), store.Get("h"));
        Assert.Null(store.Get("short"));
        Assert.Null(store.Get("h2"));
    }
}

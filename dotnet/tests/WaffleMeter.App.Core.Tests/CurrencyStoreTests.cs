using WaffleMeter.App.Core;
using WaffleMeter.Capture;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

/// <summary>
/// Spec for the persisted currency store (<c>content.currencies</c>): per-character balances keyed by identity
/// hash, the 서버 창고 keyed by server, and the blob rules every per-character store follows.
/// </summary>
public sealed class CurrencyStoreTests
{
    private const long At = 1_791_298_889_113;
    private const int Server = 2003;

    [Fact]
    public void Round_trips_character_and_server_rows()
    {
        var store = CurrencyStore.Parse(null);
        store.UpsertCharacter("h1", CurrencyCatalog.BoundKina, 13_000, At);
        store.UpsertCharacter("h1", CurrencyCatalog.Kina, 4_454_882, At);
        store.UpsertCharacter("h2", CurrencyCatalog.AbyssPoint, 25_611, At + 1);
        store.UpsertServer(Server, CurrencyCatalog.ServerStorageKina, 325_000_000, At);

        var reloaded = CurrencyStore.Parse(store.Serialize());

        Assert.Equal(new CurrencyRecord(13_000, At), reloaded.Character("h1", CurrencyCatalog.BoundKina));
        Assert.Equal(new CurrencyRecord(4_454_882, At), reloaded.Character("h1", CurrencyCatalog.Kina));
        Assert.Equal(new CurrencyRecord(25_611, At + 1), reloaded.Character("h2", CurrencyCatalog.AbyssPoint));
        Assert.Equal(new CurrencyRecord(325_000_000, At), reloaded.Server(Server, CurrencyCatalog.ServerStorageKina));
        Assert.Equal(store.Serialize(), reloaded.Serialize());
    }

    /// <summary>A balance past the int range — a warehouse does not stop at 21억.</summary>
    [Fact]
    public void Keeps_balances_past_the_int_range()
    {
        var store = CurrencyStore.Parse(null);
        store.UpsertServer(Server, CurrencyCatalog.ServerStorageKina, 12_345_678_901, At);

        Assert.Equal(12_345_678_901, CurrencyStore.Parse(store.Serialize()).Server(Server, CurrencyCatalog.ServerStorageKina)?.Count);
    }

    /// <summary>A currency a newer meter tracks must survive one launch of this one — the blob is rewritten on
    /// every broadcast, so dropping it would make a single rollback permanent.</summary>
    [Fact]
    public void Preserves_a_slug_this_build_does_not_know()
    {
        var store = CurrencyStore.Parse($"c,h1,someFutureCoin,77,{At};c,h1,kina,5,{At}");
        store.UpsertCharacter("h1", CurrencyCatalog.Kina, 6, At + 1);

        var reloaded = CurrencyStore.Parse(store.Serialize());

        Assert.Equal(77, reloaded.Character("h1", "someFutureCoin")?.Count);
        Assert.Equal(6, reloaded.Character("h1", CurrencyCatalog.Kina)?.Count);
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("c,h1,kina,5")]                         // four fields
    [InlineData("c,h1,kina,5,1,extra")]                 // six fields
    [InlineData("c,h1,kina,many,1")]                    // non-numeric count
    [InlineData("c,h1,kina,-5,1")]                      // negative count
    [InlineData("c,h1,kina,5,yesterday")]               // non-numeric stamp
    [InlineData("c, ,kina,5,1")]                        // blank hash
    [InlineData("s,0,kinaServerStorage,5,1")]           // server 0
    [InlineData("s,trinity,kinaServerStorage,5,1")]     // server not a number
    [InlineData("x,h1,kina,5,1")]                       // unknown kind
    [InlineData("c,h1,kina,99999999999999999999,1")]    // count overflows long
    public void Skips_a_malformed_record_and_keeps_the_rest(string bad)
    {
        var store = CurrencyStore.Parse($"{bad};c,h2,kina,9,{At}");

        Assert.Equal(9, store.Character("h2", CurrencyCatalog.Kina)?.Count);
        Assert.Null(store.Character("h1", CurrencyCatalog.Kina));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(";;;")]
    [InlineData(",,,,")]
    [InlineData("ÿ\u0000;c")]
    public void Parsing_never_throws(string? blob)
    {
        CurrencyStore store = CurrencyStore.Parse(blob);
        Assert.Equal(string.Empty, store.Serialize());
    }

    /// <summary>The snapshot is filed seconds after it arrived; a change that landed in between carries a newer
    /// stamp and must not be rewound by it.</summary>
    [Fact]
    public void An_older_reading_never_overwrites_a_newer_one()
    {
        var store = CurrencyStore.Parse(null);
        Assert.True(store.UpsertCharacter("h1", CurrencyCatalog.Kina, 329_454_882, At + 20_000));

        Assert.False(store.UpsertCharacter("h1", CurrencyCatalog.Kina, 4_454_882, At));
        Assert.Equal(329_454_882, store.Character("h1", CurrencyCatalog.Kina)?.Count);

        Assert.True(store.UpsertCharacter("h1", CurrencyCatalog.Kina, 4_454_882, At + 30_000));
    }

    [Fact]
    public void A_repeat_of_what_is_on_file_is_not_a_change()
    {
        var store = CurrencyStore.Parse(null);
        Assert.True(store.UpsertServer(Server, CurrencyCatalog.ServerStorageKina, 325_000_000, At));
        Assert.False(store.UpsertServer(Server, CurrencyCatalog.ServerStorageKina, 325_000_000, At));
    }

    /// <summary>Filing splits the ledger by scope: character balances under the hash, the 서버 창고 under the
    /// server. A balance the catalog does not map (큐나 in container 3) is ignored.</summary>
    [Fact]
    public void File_puts_the_warehouse_under_the_server_and_the_rest_under_the_character()
    {
        var store = CurrencyStore.Parse(null);
        bool changed = store.File("h1", Server,
        [
            new CurrencyBalance(CurrencyItemParser.KinaId, 1, 4_454_882, At),
            new CurrencyBalance(CurrencyItemParser.KinaId, 2, 325_000_000, At),
            new CurrencyBalance(CurrencyItemParser.KinaId, 15, 0, At),
            new CurrencyBalance(930_200_001, 3, 5_060, At),
        ]);

        Assert.True(changed);
        Assert.Equal(4_454_882, store.Character("h1", CurrencyCatalog.Kina)?.Count);
        Assert.Equal(0, store.Character("h1", CurrencyCatalog.CharacterStorageKina)?.Count);
        Assert.Null(store.Character("h1", CurrencyCatalog.ServerStorageKina));
        Assert.Equal(325_000_000, store.Server(Server, CurrencyCatalog.ServerStorageKina)?.Count);
    }

    /// <summary>Without a server the warehouse cannot be filed — it is skipped, never guessed onto server 0 — and
    /// without a hash the character half is skipped the same way.</summary>
    [Fact]
    public void File_skips_the_half_it_has_no_key_for()
    {
        var store = CurrencyStore.Parse(null);
        store.File("h1", 0,
        [
            new CurrencyBalance(CurrencyItemParser.KinaId, 1, 10, At),
            new CurrencyBalance(CurrencyItemParser.KinaId, 2, 20, At),
        ]);
        store.File(null, Server, [new CurrencyBalance(CurrencyItemParser.BoundKinaId, 1, 30, At)]);

        Assert.Equal($"c,h1,kina,10,{At}", store.Serialize());
    }

    /// <summary>Forgetting a character takes its rows, and only its rows: the 서버 창고 belongs to the account.</summary>
    [Fact]
    public void RemoveAll_forgets_characters_but_keeps_the_server_warehouse()
    {
        var store = CurrencyStore.Parse(null);
        store.UpsertCharacter("h1", CurrencyCatalog.Kina, 1, At);
        store.UpsertCharacter("h2", CurrencyCatalog.Kina, 2, At);
        store.UpsertServer(Server, CurrencyCatalog.ServerStorageKina, 3, At);

        Assert.True(store.RemoveAll(["h1"]));
        Assert.False(store.RemoveAll(["h1", " "]));

        Assert.Null(store.Character("h1", CurrencyCatalog.Kina));
        Assert.Equal(2, store.Character("h2", CurrencyCatalog.Kina)?.Count);
        Assert.Equal(3, store.Server(Server, CurrencyCatalog.ServerStorageKina)?.Count);
    }

    [Fact]
    public void Keeps_the_most_recent_characters_up_to_the_cap()
    {
        var store = CurrencyStore.Parse(null);
        for (int i = 0; i <= CurrencyStore.MaxCharacters; i++)
        {
            store.UpsertCharacter($"h{i}", CurrencyCatalog.Kina, i, At + i);
        }

        var reloaded = CurrencyStore.Parse(store.Serialize());
        Assert.Null(reloaded.Character("h0", CurrencyCatalog.Kina));
        Assert.Equal(CurrencyStore.MaxCharacters, reloaded.Character($"h{CurrencyStore.MaxCharacters}", CurrencyCatalog.Kina)?.Count);
    }

    /// <summary>The blob passes through the settings file's Latin-1 → EUC-KR re-decode, so it must stay ASCII —
    /// no names, no Korean. Slugs, hashes, ids and numbers only.</summary>
    [Fact]
    public void The_blob_is_ascii()
    {
        var store = CurrencyStore.Parse(null);
        store.File("0291206e", Server,
        [
            new CurrencyBalance(CurrencyItemParser.BoundKinaId, 1, 13_000, At),
            new CurrencyBalance(CurrencyItemParser.KinaId, 2, 325_000_000, At),
        ]);

        Assert.All(store.Serialize(), c => Assert.True(c < 0x80));
    }

    [Fact]
    public void Every_catalog_slug_is_unique_and_separator_free()
    {
        Assert.Equal(CurrencyCatalog.All.Count, CurrencyCatalog.All.Select(c => c.Slug).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(CurrencyCatalog.All.Count, CurrencyCatalog.All.Select(c => (c.ItemId, c.Container)).Distinct().Count());
        Assert.All(CurrencyCatalog.All, c => Assert.DoesNotContain(c.Slug, ch => ch is ',' or ';' or >= (char)0x80));
    }
}

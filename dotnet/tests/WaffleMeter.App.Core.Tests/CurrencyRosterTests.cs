using WaffleMeter.App.Core;
using WaffleMeter.Capture;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

/// <summary>
/// Spec for the currency side of the 컨텐츠 관리 list — the per-character cells and, above all, the 총 키나 line:
/// per server, every character's own kinah (각인 + 거래 가능 + 캐릭터 창고) plus the 서버 창고 counted ONCE
/// (owner decision, 2026-10-07).
/// </summary>
public sealed class CurrencyRosterTests
{
    private const long At = 1_791_298_889_113;
    private const int Trinity = 2003;
    private const int Siel = 1001;

    private static AetherPerCharacterStore Characters(params (string Hash, string Name, int Server)[] entries)
    {
        var store = AetherPerCharacterStore.Parse(null);
        long t = At;
        foreach ((string hash, string name, int server) in entries)
        {
            store.Upsert(hash, new AetherSnapshot(10, 0, t++, name, server));
        }

        return store;
    }

    private static CurrencyStore Kina(params (string Hash, string Slug, long Count)[] rows)
    {
        var store = CurrencyStore.Parse(null);
        foreach ((string hash, string slug, long count) in rows)
        {
            store.UpsertCharacter(hash, slug, count, At);
        }

        return store;
    }

    /// <summary>Two characters on one server and the warehouse they share. The warehouse is added once — summing
    /// it per character is exactly the double count the server-keyed row exists to prevent.</summary>
    [Fact]
    public void The_server_total_counts_the_warehouse_once()
    {
        AetherPerCharacterStore characters = Characters(("h1", "밀피", Trinity), ("h2", "콘팡", Trinity));
        CurrencyStore currencies = Kina(
            ("h1", CurrencyCatalog.BoundKina, 13_000),
            ("h1", CurrencyCatalog.Kina, 4_454_882),
            ("h2", CurrencyCatalog.BoundKina, 2_251_000),
            ("h2", CurrencyCatalog.Kina, 8_042_286));
        currencies.UpsertServer(Trinity, CurrencyCatalog.ServerStorageKina, 325_000_000, At);

        ServerKinaLine line = Assert.Single(CurrencyRoster.ServerKina(characters, currencies: currencies));

        Assert.Equal(Trinity, line.Server);
        Assert.Equal("트리", line.ServerLabel);
        Assert.Equal(13_000 + 4_454_882 + 2_251_000 + 8_042_286 + 325_000_000L, line.Total);
        Assert.Equal(325_000_000, line.ServerStorage);
        Assert.Equal(["콘팡", "밀피"], line.Characters.Select(c => c.Label)); // highest kinah first
        Assert.Equal(0, line.CharactersWithoutRecord);
    }

    /// <summary>The 10-07 numbers. Withdrawing everything only moves kinah between the inventory and the warehouse,
    /// so the server's total must not move at all — the check that the two halves are filed consistently.</summary>
    [Fact]
    public void Moving_kinah_between_inventory_and_warehouse_does_not_change_the_total()
    {
        AetherPerCharacterStore characters = Characters(("h1", "밀피", Trinity));

        var ledger = new CurrencyLedger();
        var store = CurrencyStore.Parse(null);
        ledger.ApplySnapshot(
        [
            new CurrencyItem(1, CurrencyItemParser.BoundKinaId, 13_000, 1),
            new CurrencyItem(2, CurrencyItemParser.KinaId, 4_454_882, 1),
            new CurrencyItem(3, CurrencyItemParser.KinaId, 325_000_000, 2),
        ], exact: true, At);
        Assert.True(ledger.TryTakeFiling(At + 7_190, At + 7_190, out IReadOnlyList<CurrencyBalance> filed));
        store.File("h1", Trinity, filed);

        long before = Assert.Single(CurrencyRoster.ServerKina(characters, currencies: store)).Total;
        Assert.Equal(13_000 + 4_454_882 + 325_000_000L, before);

        ledger.ApplyChanges(
        [
            new CurrencyItemChange(ItemChangeType.Update, new CurrencyItem(2, CurrencyItemParser.KinaId, 329_454_882, 1)),
            new CurrencyItemChange(ItemChangeType.Remove, new CurrencyItem(3, CurrencyItemParser.KinaId, 0, 2)),
        ], At + 20_000);
        Assert.True(ledger.TryTakeFiling(At + 7_190, At + 20_000, out filed));
        store.File("h1", Trinity, filed);

        ServerKinaLine after = Assert.Single(CurrencyRoster.ServerKina(characters, currencies: store));
        Assert.Equal(before, after.Total);
        Assert.Equal(0, after.ServerStorage);
        Assert.Equal(13_000 + 329_454_882L, Assert.Single(after.Characters).Kina);
    }

    /// <summary>캐릭터 창고 kinah is the character's own and counts toward the total; a different server is a
    /// different line with its own warehouse.</summary>
    [Fact]
    public void Each_server_gets_its_own_line_and_character_storage_counts()
    {
        AetherPerCharacterStore characters = Characters(("h1", "밀피", Trinity), ("h3", "필러싱", Siel));
        CurrencyStore currencies = Kina(
            ("h1", CurrencyCatalog.Kina, 100),
            ("h1", CurrencyCatalog.CharacterStorageKina, 50),
            ("h3", CurrencyCatalog.Kina, 7));
        currencies.UpsertServer(Siel, CurrencyCatalog.ServerStorageKina, 1_000, At);

        IReadOnlyList<ServerKinaLine> lines = CurrencyRoster.ServerKina(characters, currencies: currencies);

        Assert.Equal([Siel, Trinity], lines.Select(l => l.Server));
        Assert.Equal(1_007, lines[0].Total);
        Assert.Equal(150, lines[1].Total);
        Assert.Null(lines[1].ServerStorage);
    }

    /// <summary>The line of the server being played comes first, whatever its id.</summary>
    [Fact]
    public void The_current_characters_server_comes_first()
    {
        AetherPerCharacterStore characters = Characters(("h1", "밀피", Trinity), ("h3", "필러싱", Siel));
        CurrencyStore currencies = Kina(("h1", CurrencyCatalog.Kina, 1), ("h3", CurrencyCatalog.Kina, 2));

        IReadOnlyList<ServerKinaLine> lines = CurrencyRoster.ServerKina(characters, currencies: currencies, currentHash: "h1");

        Assert.Equal([Trinity, Siel], lines.Select(l => l.Server));
        Assert.True(lines[0].IsCurrentServer);
    }

    /// <summary>A character on the server whose kinah has never been stated adds nothing — and says so, because
    /// the total is then a lower bound. A server with nothing at all on file gets no line rather than a zero.</summary>
    [Fact]
    public void Characters_without_a_record_are_counted_as_unknown_not_as_zero()
    {
        AetherPerCharacterStore characters = Characters(
            ("h1", "밀피", Trinity), ("h2", "콘팡", Trinity), ("h3", "필러싱", Siel));
        CurrencyStore currencies = Kina(("h1", CurrencyCatalog.Kina, 500));

        ServerKinaLine line = Assert.Single(CurrencyRoster.ServerKina(characters, currencies: currencies));

        Assert.Equal(Trinity, line.Server);
        Assert.Equal(500, line.Total);
        Assert.Equal(1, line.CharactersWithoutRecord);
    }

    /// <summary>The warehouse alone is enough for a line — a server whose characters were played before this
    /// feature existed still has a total to show once any of them withdraws.</summary>
    [Fact]
    public void The_warehouse_alone_makes_a_line()
    {
        AetherPerCharacterStore characters = Characters(("h1", "밀피", Trinity));
        var currencies = CurrencyStore.Parse(null);
        currencies.UpsertServer(Trinity, CurrencyCatalog.ServerStorageKina, 325_000_000, At);

        ServerKinaLine line = Assert.Single(CurrencyRoster.ServerKina(characters, currencies: currencies));
        Assert.Equal(325_000_000, line.Total);
        Assert.Empty(line.Characters);
        Assert.Equal(1, line.CharactersWithoutRecord);
    }

    [Fact]
    public void No_store_means_no_lines_and_no_cells()
    {
        AetherPerCharacterStore characters = Characters(("h1", "밀피", Trinity));

        Assert.Empty(CurrencyRoster.ServerKina(characters));
        AetherRosterRow row = Assert.Single(AetherRoster.Build(characters));
        Assert.Empty(row.CurrencyCells);
        Assert.False(row.CurrenciesKnown);
    }

    /// <summary>Each row carries the character-scope balances in catalog order; the 서버 창고 is not a character's
    /// cell (it is the server line's). A balance never stated stays a null count, not a zero.</summary>
    [Fact]
    public void Rows_carry_the_characters_own_balances()
    {
        AetherPerCharacterStore characters = Characters(("h1", "밀피", Trinity));
        CurrencyStore currencies = Kina(("h1", CurrencyCatalog.AbyssPoint, 25_611), ("h1", CurrencyCatalog.TrialMark, 3_000));
        currencies.UpsertServer(Trinity, CurrencyCatalog.ServerStorageKina, 325_000_000, At);

        AetherRosterRow row = Assert.Single(AetherRoster.Build(characters, currencies: currencies));

        Assert.True(row.CurrenciesKnown);
        Assert.DoesNotContain(row.CurrencyCells, c => c.Currency.Scope == CurrencyScope.Server);
        Assert.Equal(25_611, row.CurrencyCells.Single(c => c.Currency.Slug == CurrencyCatalog.AbyssPoint).Count);
        Assert.Equal(3_000, row.CurrencyCells.Single(c => c.Currency.Slug == CurrencyCatalog.TrialMark).Count);
        Assert.Null(row.CurrencyCells.Single(c => c.Currency.Slug == CurrencyCatalog.BoundKina).Count);
    }

    [Theory]
    [InlineData(0L, "0")]
    [InlineData(3_000L, "3,000")]
    [InlineData(25_611L, "25,611")]
    [InlineData(99_999L, "99,999")]
    [InlineData(100_000L, "10만")]
    [InlineData(4_454_882L, "445만")]
    [InlineData(99_999_999L, "9,999만")]
    [InlineData(100_000_000L, "1억")]
    [InlineData(300_000_000L, "3억")]
    [InlineData(325_000_000L, "3억 2,500만")]
    [InlineData(329_454_882L, "3억 2,945만")]
    [InlineData(12_345_678_901L, "123억 4,567만")]
    [InlineData(1_234_567_890_123L, "1조 2,345억")]
    [InlineData(-4_454_882L, "-445만")]
    public void Compact_amounts_read_in_korean_units_and_truncate(long amount, string expected)
    {
        Assert.Equal(expected, CurrencyFormat.Compact(amount));
    }

    [Fact]
    public void Exact_amounts_are_comma_grouped()
    {
        Assert.Equal("329,454,882", CurrencyFormat.Exact(329_454_882));
    }

    /// <summary>The icons ship as embedded resources from <c>dotnet/Assets/Icons</c>; a catalog entry pointing at a
    /// file that is not there renders as an empty square, with nothing failing anywhere.</summary>
    [Fact]
    public void Every_currency_icon_ships()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "dotnet", "Assets", "Icons")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        string icons = Path.Combine(dir!.FullName, "dotnet", "Assets", "Icons");
        foreach (string file in CurrencyCatalog.All.Select(c => c.IconFile).Append(CurrencyCatalog.TotalKinaIcon).Distinct())
        {
            Assert.True(File.Exists(Path.Combine(icons, file)), $"Assets/Icons/{file} 가 없습니다.");
        }
    }
}

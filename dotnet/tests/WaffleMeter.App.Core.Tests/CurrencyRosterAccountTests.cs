using WaffleMeter.App.Core;
using WaffleMeter.Capture;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

/// <summary>
/// Spec for the 총 키나 lines when one server holds characters of more than one game account (user report
/// 2026-11-12: "같은 서버에 계정 두 개면 창고 키나랑 슈고 열쇠가 섞인다"). Each account's line counts its OWN 서버 창고 —
/// the newest reading one of its characters saw — instead of the single server-keyed row that the last account to
/// log in overwrites.
/// </summary>
public sealed class CurrencyRosterAccountTests
{
    private const long At = 1_794_554_197_737;
    private const int Trinity = 2003;

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

    private static CurrencyStore Kina(params (string Hash, long Count)[] rows)
    {
        var store = CurrencyStore.Parse(null);
        foreach ((string hash, long count) in rows)
        {
            store.UpsertCharacter(hash, CurrencyCatalog.Kina, count, At);
        }

        return store;
    }

    private static CurrencyStore Observed(params (string Hash, long Storage, long AtMs)[] rows)
    {
        var store = CurrencyStore.Parse(null);
        foreach ((string hash, long storage, long atMs) in rows)
        {
            store.UpsertCharacter(hash, CurrencyCatalog.ServerStorageKina, storage, atMs);
        }

        return store;
    }

    private static AccountGroupStore Groups(params string[][] lists)
    {
        var store = AccountGroupStore.Empty();
        int n = 0;
        foreach (string[] list in lists)
        {
            int id = n++;
            store.ApplyLobby(list, At + id, () => "g" + id);
        }

        return store;
    }

    /// <summary>The reported case. Account A (a1, a2) keeps 300 in its 서버 창고, account B (b1) an empty one; B
    /// logged in last, so the server row says 0. Split, each account shows its own.</summary>
    [Fact]
    public void Two_accounts_on_one_server_each_count_their_own_warehouse()
    {
        AetherPerCharacterStore characters = Characters(("a1", "가", Trinity), ("a2", "나", Trinity), ("b1", "다", Trinity));
        CurrencyStore currencies = Kina(("a1", 10), ("a2", 20), ("b1", 5));
        currencies.UpsertServer(Trinity, CurrencyCatalog.ServerStorageKina, 0, At + 500); // B wrote last
        CurrencyStore observed = Observed(("a1", 300, At + 100), ("a2", 300, At + 200), ("b1", 0, At + 500));

        IReadOnlyList<ServerKinaLine> lines = CurrencyRoster.ServerKina(
            characters, currencies: currencies, currentHash: "b1",
            groups: Groups(["a1", "a2"], ["b1"]), observed: observed);

        Assert.Equal(2, lines.Count);
        Assert.Equal(("계정 1", "g0", 330L, (long?)300), (lines[0].AccountLabel, lines[0].AccountGroupId, lines[0].Total, lines[0].ServerStorage));
        Assert.Equal(At + 200, lines[0].ServerStorageObservedAtMs); // the newer of a1's and a2's readings
        Assert.Equal(("계정 2", "g1", 5L, (long?)0), (lines[1].AccountLabel, lines[1].AccountGroupId, lines[1].Total, lines[1].ServerStorage));
        Assert.All(lines, l => Assert.True(l.IsCurrentServer));
    }

    /// <summary>The numbers follow the order the accounts were first told apart, not who is playing — they do not
    /// swap as the user switches accounts.</summary>
    [Fact]
    public void Account_numbers_do_not_follow_the_current_character()
    {
        AetherPerCharacterStore characters = Characters(("a1", "가", Trinity), ("b1", "다", Trinity));
        CurrencyStore currencies = Kina(("a1", 1), ("b1", 2));
        AccountGroupStore groups = Groups(["a1"], ["b1"]);

        foreach (string current in new[] { "a1", "b1" })
        {
            IReadOnlyList<ServerKinaLine> lines = CurrencyRoster.ServerKina(
                characters, currencies: currencies, currentHash: current, groups: groups, observed: Observed());

            Assert.Equal(["g0", "g1"], lines.Select(l => l.AccountGroupId));
        }
    }

    /// <summary>One account per server — the ordinary case — keeps the single line exactly as before: no account
    /// label, and the 서버 창고 is the newest of the server row and its characters' readings.</summary>
    [Fact]
    public void One_account_keeps_one_unlabeled_line()
    {
        AetherPerCharacterStore characters = Characters(("a1", "가", Trinity), ("a2", "나", Trinity));
        CurrencyStore currencies = Kina(("a1", 10), ("a2", 20));
        currencies.UpsertServer(Trinity, CurrencyCatalog.ServerStorageKina, 100, At + 10);
        CurrencyStore observed = Observed(("a2", 120, At + 20));

        ServerKinaLine line = Assert.Single(CurrencyRoster.ServerKina(
            characters, currencies: currencies, groups: Groups(["a1", "a2"]), observed: observed));

        Assert.Null(line.AccountLabel);
        Assert.Equal(120, line.ServerStorage);
        Assert.Equal(150, line.Total);
    }

    /// <summary>Without any account knowledge (before the first list, or an older settings file) nothing changes.</summary>
    [Fact]
    public void Without_groups_the_server_row_is_used_as_before()
    {
        AetherPerCharacterStore characters = Characters(("a1", "가", Trinity), ("b1", "다", Trinity));
        CurrencyStore currencies = Kina(("a1", 10), ("b1", 5));
        currencies.UpsertServer(Trinity, CurrencyCatalog.ServerStorageKina, 7, At);

        ServerKinaLine line = Assert.Single(CurrencyRoster.ServerKina(characters, currencies: currencies));

        Assert.Null(line.AccountLabel);
        Assert.Equal(22, line.Total);
    }

    /// <summary>On a server already split between two accounts, characters no list has placed share a "계정 미확인"
    /// line of their own — with no 서버 창고, since whose it would be cannot be told.</summary>
    [Fact]
    public void Characters_no_list_placed_share_an_unplaced_line_without_a_warehouse()
    {
        AetherPerCharacterStore characters = Characters(("a1", "가", Trinity), ("b1", "다", Trinity), ("x1", "라", Trinity));
        CurrencyStore currencies = Kina(("a1", 10), ("b1", 1), ("x1", 4));
        CurrencyStore observed = Observed(("a1", 300, At), ("x1", 999, At + 1));

        IReadOnlyList<ServerKinaLine> lines = CurrencyRoster.ServerKina(
            characters, currencies: currencies, groups: Groups(["a1"], ["b1"]), observed: observed);

        Assert.Equal(3, lines.Count);
        Assert.Equal(310, lines[0].Total);
        Assert.False(lines[0].IsUnplacedAccount);
        Assert.Equal("계정 미확인", lines[2].AccountLabel);
        Assert.True(lines[2].IsUnplacedAccount);
        Assert.Null(lines[2].ServerStorage);
        Assert.Equal(4, lines[2].Total);
        Assert.True(lines[2].IsLowerBound);
    }

    /// <summary>Adversarial review 2026-11-13: a one-account user with a stale entry on the server (a deleted alt, a
    /// renamed character's old name) must keep the ordinary single line — a stale entry and a second account not yet
    /// seen cannot be told apart, so one account plus unplaced characters does not split.</summary>
    [Fact]
    public void One_account_with_a_stale_character_keeps_one_line()
    {
        AetherPerCharacterStore characters = Characters(("a1", "가", Trinity), ("old", "옛이름", Trinity));
        CurrencyStore currencies = Kina(("a1", 10), ("old", 4));
        currencies.UpsertServer(Trinity, CurrencyCatalog.ServerStorageKina, 300, At);

        ServerKinaLine line = Assert.Single(CurrencyRoster.ServerKina(
            characters, currencies: currencies, groups: Groups(["a1"]), observed: Observed()));

        Assert.Null(line.AccountLabel);
        Assert.Equal(314, line.Total);
    }

    /// <summary>Accounts on DIFFERENT servers never split anything: each server is one account.</summary>
    [Fact]
    public void Accounts_on_different_servers_stay_one_line_each()
    {
        AetherPerCharacterStore characters = Characters(("a1", "가", Trinity), ("b1", "다", 1001));
        CurrencyStore currencies = Kina(("a1", 1), ("b1", 2));

        IReadOnlyList<ServerKinaLine> lines = CurrencyRoster.ServerKina(
            characters, currencies: currencies, groups: Groups(["a1"], ["b1"]), observed: Observed());

        Assert.Equal(2, lines.Count);
        Assert.All(lines, l => Assert.Null(l.AccountLabel));
    }

    /// <summary>Adversarial review 2026-11-13: a snapshot filed by the 30 s fallback, before its naming packet, may be
    /// the NEXT account's warehouse under the previous account's character — so the ledger says it is unconfirmed and
    /// the app learns nothing account-scoped from it. Once the naming packet has come, it is confirmed.</summary>
    [Fact]
    public void A_fallback_filing_is_not_identity_confirmed()
    {
        var fallback = new CurrencyLedger();
        fallback.ApplySnapshot([new CurrencyItem(1, CurrencyItemParser.KinaId, 0, 2)], exact: true, At);
        Assert.True(fallback.TryTakeFiling(At - 60_000, At + WeeklyContentOwnership.SettleMs, out _, out bool early));
        Assert.False(early);

        var named = new CurrencyLedger();
        named.ApplySnapshot([new CurrencyItem(1, CurrencyItemParser.KinaId, 0, 2)], exact: true, At);
        Assert.True(named.TryTakeFiling(At + 7_000, At + 7_000, out _, out bool confirmed));
        Assert.True(confirmed);
    }

    /// <summary>The 서버 창고 half of a filing goes under the character that saw it; character balances do not.</summary>
    [Fact]
    public void Observed_store_files_only_the_account_scope_balance()
    {
        var observed = CurrencyStore.Parse(null);
        CurrencyBalance[] balances =
        [
            new(CurrencyItemParser.KinaId, 2, 325_000_000, At), // container 2 = 서버 창고
            new(CurrencyItemParser.KinaId, 1, 4_454_882, At),   // the character's own
        ];

        Assert.True(observed.FileObservedAccountScope("a1", balances));

        Assert.Equal(325_000_000, observed.Character("a1", CurrencyCatalog.ServerStorageKina)!.Value.Count);
        Assert.Null(observed.Character("a1", CurrencyCatalog.Kina));
        Assert.False(observed.FileObservedAccountScope("a1", balances)); // nothing new
    }
}

using WaffleMeter.App.Core;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

/// <summary>
/// Spec for <see cref="AccountGroupStore"/> (<c>content.accountGroups</c>): which characters share a game account,
/// learned from the character-select list. The newest list wins and lists never merge groups; the user's own
/// placement wins over any list.
/// </summary>
public sealed class AccountGroupStoreTests
{
    private const long At = 1_794_554_197_737;

    private static Func<string> Ids(params string[] ids)
    {
        var queue = new Queue<string>(ids);
        return () => queue.Dequeue();
    }

    /// <summary>The owner's 2026-11-13 capture in miniature: two accounts on one server log in one after the
    /// other, each list naming only its own characters — two groups, nobody shared.</summary>
    [Fact]
    public void Two_accounts_lists_make_two_groups()
    {
        var store = AccountGroupStore.Empty();

        Assert.True(store.ApplyLobby(["a1", "a2", "a3"], At, Ids("ga")));
        Assert.True(store.ApplyLobby(["b1", "b2"], At + 1, Ids("gb")));

        Assert.Equal("ga", store.GroupOf("a2")!.Value.GroupId);
        Assert.Equal("gb", store.GroupOf("b1")!.Value.GroupId);
        Assert.Equal(AccountGroupSource.Lobby, store.GroupOf("b1")!.Value.Source);
        Assert.True(store.CreatedAt("ga") < store.CreatedAt("gb"));
    }

    /// <summary>The same account's list again keeps its group (no new id) and only re-stamps its members — AtMs is
    /// what the cap evicts by, so an account still logging in never loses characters to it. The same list at the same
    /// moment changes nothing.</summary>
    [Fact]
    public void The_same_list_again_keeps_the_group_and_restamps_it()
    {
        var store = AccountGroupStore.Empty();
        store.ApplyLobby(["a1", "a2"], At, Ids("ga"));

        Assert.False(store.ApplyLobby(["a2", "a1"], At, () => throw new InvalidOperationException("no new group")));
        Assert.True(store.ApplyLobby(["a2", "a1"], At + 10, () => throw new InvalidOperationException("no new group")));
        Assert.Equal(new AccountMembership("ga", AccountGroupSource.Lobby, At + 10), store.GroupOf("a1"));
        Assert.Equal(At, store.CreatedAt("ga"));
    }

    /// <summary>At the cap, the account that logged in long ago loses characters, not the one still in use.</summary>
    [Fact]
    public void The_cap_evicts_the_account_not_seen_for_longest()
    {
        var store = AccountGroupStore.Empty();
        string[] main = Enumerable.Range(0, 40).Select(i => "m" + i).ToArray();
        string[] old = Enumerable.Range(0, 40).Select(i => "o" + i).ToArray();
        string[] alt = Enumerable.Range(0, 20).Select(i => "n" + i).ToArray();
        store.ApplyLobby(main, At, Ids("gm"));
        store.ApplyLobby(old, At + 1, Ids("go"));
        store.ApplyLobby(main, At + 2, Ids("unused")); // main logs in again

        store.ApplyLobby(alt, At + 3, Ids("gn"));      // 100 members > 96

        Assert.All(main, h => Assert.Equal("gm", store.GroupOf(h)!.Value.GroupId));
        Assert.Equal(36, old.Count(h => store.GroupOf(h) != null));
    }

    /// <summary>A list is the account's WHOLE roster: a character it no longer names (renamed — the owner's 10-07
    /// list had 하** where 11-13 has 뿌* — deleted, transferred) leaves the group; a new one joins it.</summary>
    [Fact]
    public void A_character_the_list_no_longer_names_leaves_and_a_new_one_joins()
    {
        var store = AccountGroupStore.Empty();
        store.ApplyLobby(["a1", "old-name", "a3"], At, Ids("ga"));

        Assert.True(store.ApplyLobby(["a1", "new-name", "a3"], At + 1, Ids("unused")));

        Assert.Null(store.GroupOf("old-name"));
        Assert.Equal("ga", store.GroupOf("new-name")!.Value.GroupId);
    }

    /// <summary>A name deleted on account A and re-created on account B (same server, so the same identity hash)
    /// moves to B alone. Accumulated "seen together" edges would have joined the two accounts for good.</summary>
    [Fact]
    public void A_name_recreated_on_the_other_account_moves_alone_and_never_merges_the_accounts()
    {
        var store = AccountGroupStore.Empty();
        store.ApplyLobby(["a1", "a2", "shared"], At, Ids("ga"));
        store.ApplyLobby(["b1", "b2"], At + 1, Ids("gb"));

        store.ApplyLobby(["b1", "b2", "shared"], At + 2, Ids("unused"));

        Assert.Equal("gb", store.GroupOf("shared")!.Value.GroupId);
        Assert.Equal("ga", store.GroupOf("a1")!.Value.GroupId);
        Assert.Equal("ga", store.GroupOf("a2")!.Value.GroupId);
    }

    /// <summary>A list whose characters sit in two groups goes to the one holding most of them; the other group
    /// keeps whatever the list does not name.</summary>
    [Fact]
    public void A_list_spanning_two_groups_goes_to_the_one_holding_most_of_it()
    {
        var store = AccountGroupStore.Empty();
        store.ApplyLobby(["x1", "x2", "x3"], At, Ids("gx"));
        store.ApplyLobby(["y1", "y2"], At + 1, Ids("gy"));

        store.ApplyLobby(["x1", "x2", "y1"], At + 2, Ids("unused"));

        Assert.Equal("gx", store.GroupOf("y1")!.Value.GroupId);
        Assert.Equal("gy", store.GroupOf("y2")!.Value.GroupId);
        Assert.Null(store.GroupOf("x3")); // gx's list no longer names it
    }

    /// <summary>The user's placement stands against any list until handed back; handing it back leaves the character
    /// for the next list.</summary>
    [Fact]
    public void A_manual_placement_wins_until_cleared()
    {
        var store = AccountGroupStore.Empty();
        store.ApplyLobby(["a1", "a2"], At, Ids("ga"));
        Assert.True(store.AssignManual("a2", groupId: null, At + 1, Ids("gm")));

        store.ApplyLobby(["a1", "a2"], At + 2, Ids("unused"));
        Assert.Equal(new AccountMembership("gm", AccountGroupSource.Manual, At + 1), store.GroupOf("a2"));

        Assert.True(store.ClearManual("a2"));
        Assert.Null(store.GroupOf("a2"));
        Assert.Equal(0, store.CreatedAt("gm")); // the emptied group is gone

        store.ApplyLobby(["a1", "a2"], At + 3, Ids("unused"));
        Assert.Equal("ga", store.GroupOf("a2")!.Value.GroupId);
    }

    /// <summary>Second review 2026-11-13: one character the user (mis)placed in account A must not steer account B's
    /// next list into A and let go of all of A's characters. Only list placements vote.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void A_manual_placement_does_not_steer_another_accounts_list(int bCount)
    {
        string[] bList = Enumerable.Range(1, bCount).Select(i => "b" + i).ToArray();
        var store = AccountGroupStore.Empty();
        store.ApplyLobby(["a1", "a2"], At, Ids("ga"));
        store.ApplyLobby(bList, At + 1, Ids("gb"));
        store.AssignManual("b1", "ga", At + 2, Ids("unused")); // the user put b1 in account A

        store.ApplyLobby(bList, At + 3, Ids("gnew"));

        Assert.Equal("ga", store.GroupOf("a1")!.Value.GroupId);
        Assert.Equal("ga", store.GroupOf("a2")!.Value.GroupId);
        Assert.Equal(AccountGroupSource.Manual, store.GroupOf("b1")!.Value.Source);
    }

    /// <summary>With no list votes at all, an account the user built takes the list's other characters — the
    /// "새 계정으로 분리" its siblings then join — and nobody is let go.</summary>
    [Fact]
    public void A_user_built_account_takes_its_siblings_when_no_list_has_spoken()
    {
        var store = AccountGroupStore.Empty();
        store.AssignManual("b1", groupId: null, At, Ids("gm"));

        store.ApplyLobby(["b1", "b2"], At + 1, Ids("unused"));

        Assert.Equal(new AccountMembership("gm", AccountGroupSource.Lobby, At + 1), store.GroupOf("b2"));
        Assert.Equal(AccountGroupSource.Manual, store.GroupOf("b1")!.Value.Source);
    }

    [Fact]
    public void Manual_assignment_into_an_unknown_group_is_refused()
    {
        var store = AccountGroupStore.Empty();
        Assert.False(store.AssignManual("a1", "nope", At, Ids("unused")));
        Assert.False(store.ClearManual("a1"));
    }

    [Fact]
    public void Forgetting_a_character_drops_it_and_any_group_it_leaves_empty()
    {
        var store = AccountGroupStore.Empty();
        store.ApplyLobby(["a1"], At, Ids("ga"));
        store.ApplyLobby(["b1", "b2"], At + 1, Ids("gb"));

        Assert.True(store.RemoveAll(["a1", "b1"]));

        Assert.Null(store.GroupOf("a1"));
        Assert.Equal(0, store.CreatedAt("ga"));
        Assert.Equal("gb", store.GroupOf("b2")!.Value.GroupId);
    }

    [Fact]
    public void Round_trips_and_is_stable()
    {
        var store = AccountGroupStore.Empty();
        store.ApplyLobby(["a2", "a1"], At, Ids("ga"));
        store.ApplyLobby(["b1"], At + 1, Ids("gb"));
        store.AssignManual("b1", "ga", At + 2, Ids("unused"));

        string blob = store.Serialize();
        AccountGroupStore back = AccountGroupStore.Parse(blob);

        Assert.Equal(blob, back.Serialize());
        Assert.Equal(new AccountMembership("ga", AccountGroupSource.Manual, At + 2), back.GroupOf("b1"));
        Assert.Equal(At, back.CreatedAt("ga"));
    }

    /// <summary>Garbage is skipped, never thrown; a membership whose group record is missing still counts.</summary>
    [Fact]
    public void Parse_skips_garbage_and_recovers_a_missing_group_record()
    {
        AccountGroupStore store = AccountGroupStore.Parse($"m,h1,g1,l,{At};m,h2,,l,1;m,h3,g1,x,1;g,g2;zzz;;m,h4,g1,u,nope");

        Assert.Equal("g1", store.GroupOf("h1")!.Value.GroupId);
        Assert.Null(store.GroupOf("h2"));
        Assert.Null(store.GroupOf("h3"));
        Assert.Null(store.GroupOf("h4"));
        Assert.Equal(At, store.CreatedAt("g1"));
    }
}

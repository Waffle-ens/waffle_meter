using WaffleMeter.App.Core;
using WaffleMeter.Stats;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

/// <summary>
/// Spec for <see cref="LobbyRosterGate"/>: a character-select list is applied only once the character picked from
/// it has entered the world — an identity established after the list arrived — and only if that character is in it.
/// </summary>
public sealed class LobbyRosterGateTests
{
    private const long ListAt = 1_794_554_197_737;
    private const int Server = 2003;

    private static readonly string[] Names = ["가나다", "라마", "Abc1"];

    private static string Hash(string name) => StatsIdentity.CharacterIdentityHash(Server, name)!;

    /// <summary>The owner's 2026-11-13 login: the list landed ~20 s before the naming packet. Until then the
    /// identity on hand is the previous character's (or none) — wait.</summary>
    [Theory]
    [InlineData(0L)]
    [InlineData(ListAt - 60_000)]
    public void Waits_for_an_identity_established_after_the_list(long identityAt)
    {
        LobbyRosterDecision d = LobbyRosterGate.Evaluate(Names, ListAt, identityAt, Hash("라마"), Server, ListAt + 5_000);

        Assert.Equal(LobbyRosterVerdict.Wait, d.Verdict);
    }

    [Fact]
    public void Applies_when_the_character_that_entered_is_in_the_list()
    {
        LobbyRosterDecision d = LobbyRosterGate.Evaluate(Names, ListAt, ListAt + 19_000, Hash("라마"), Server, ListAt + 19_000);

        Assert.Equal(LobbyRosterVerdict.Apply, d.Verdict);
        Assert.Equal(Names.Select(Hash), d.IdentityHashes);
    }

    /// <summary>Nickname case does not matter — the identity hash lower-cases it, the list is hashed the same way.</summary>
    [Fact]
    public void Latin_names_match_regardless_of_case()
    {
        LobbyRosterDecision d = LobbyRosterGate.Evaluate(["ABC1"], ListAt, ListAt + 1, Hash("abc1"), Server, ListAt + 1);

        Assert.Equal(LobbyRosterVerdict.Apply, d.Verdict);
    }

    /// <summary>A list that does not contain the character that entered is not ours (a misread frame, another
    /// message on a moved opcode) and is never applied.</summary>
    [Fact]
    public void Drops_a_list_without_the_character_that_entered()
    {
        LobbyRosterDecision d = LobbyRosterGate.Evaluate(Names, ListAt, ListAt + 1, Hash("다른사람"), Server, ListAt + 1);

        Assert.Equal(LobbyRosterVerdict.Drop, d.Verdict);
        Assert.Empty(d.IdentityHashes);
    }

    /// <summary>The same nickname on another server is another character — the list is hashed with the server of
    /// the character that entered.</summary>
    [Fact]
    public void The_list_is_hashed_with_the_entering_characters_server()
    {
        string otherServerHash = StatsIdentity.CharacterIdentityHash(1001, "라마")!;

        LobbyRosterDecision d = LobbyRosterGate.Evaluate(Names, ListAt, ListAt + 1, otherServerHash, 1001, ListAt + 1);

        Assert.Equal(LobbyRosterVerdict.Apply, d.Verdict);
        Assert.Contains(otherServerHash, d.IdentityHashes);
        Assert.DoesNotContain(Hash("라마"), d.IdentityHashes);
    }

    [Fact]
    public void A_list_nobody_entered_from_expires()
    {
        LobbyRosterDecision d = LobbyRosterGate.Evaluate(
            Names, ListAt, identityAtMs: 0, currentHash: null, currentServer: 0, ListAt + LobbyRosterGate.ExpireMs);

        Assert.Equal(LobbyRosterVerdict.Drop, d.Verdict);
    }
}

using WaffleMeter.Stats;
using Xunit;

namespace WaffleMeter.Stats.Tests;

public sealed class StatsIdentityTests
{
    [Fact]
    public void Sha256_matches_known_vector()
    {
        Assert.Equal(
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            StatsIdentity.Sha256("abc"));
    }

    [Fact]
    public void Identity_hash_is_64_lowercase_hex_and_deterministic()
    {
        string? a = StatsIdentity.CharacterIdentityHash(3, "Hero");
        string? b = StatsIdentity.CharacterIdentityHash(3, "Hero");

        Assert.NotNull(a);
        Assert.Equal(a, b);
        Assert.Equal(64, a!.Length);
        Assert.Matches("^[0-9a-f]{64}$", a);
    }

    [Fact]
    public void Identity_hash_is_case_insensitive_and_trims()
    {
        string? lower = StatsIdentity.CharacterIdentityHash(3, "hero");
        Assert.Equal(lower, StatsIdentity.CharacterIdentityHash(3, "HERO"));
        Assert.Equal(lower, StatsIdentity.CharacterIdentityHash(3, "  Hero  "));
    }

    [Fact]
    public void Identity_hash_distinguishes_server_and_name()
    {
        Assert.NotEqual(
            StatsIdentity.CharacterIdentityHash(3, "Hero"),
            StatsIdentity.CharacterIdentityHash(4, "Hero"));
        Assert.NotEqual(
            StatsIdentity.CharacterIdentityHash(3, "Hero"),
            StatsIdentity.CharacterIdentityHash(3, "Villain"));
    }

    [Theory]
    [InlineData(0, "Hero")]
    [InlineData(-1, "Hero")]
    [InlineData(3, null)]
    [InlineData(3, "")]
    [InlineData(3, "   ")]
    public void Identity_hash_guards_invalid_inputs(int server, string? nickname)
    {
        Assert.Null(StatsIdentity.CharacterIdentityHash(server, nickname));
    }
}

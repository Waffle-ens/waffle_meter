using WaffleMeter.App.Core;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

/// <summary>Parity spec for <see cref="ColorString"/> vs the React color-utils.ts.</summary>
public class ColorStringTests
{
    [Theory]
    [InlineData("#15c98f", 0x15, 0xC9, 0x8F, 255)]
    [InlineData("#15C98F", 0x15, 0xC9, 0x8F, 255)]   // case-insensitive
    [InlineData("#ff000080", 0xFF, 0x00, 0x00, 0x80)] // 8-digit = alpha
    [InlineData("#abc", 0xAA, 0xBB, 0xCC, 255)]       // 3-digit expand
    [InlineData("#abcd", 0xAA, 0xBB, 0xCC, 0xDD)]     // 4-digit expand
    [InlineData("rgb(255, 0, 0)", 255, 0, 0, 255)]
    [InlineData("rgba(20, 40, 60, 0.5)", 20, 40, 60, 128)] // 0.5*255 -> 128 (round)
    [InlineData("  #FFFFFF  ", 255, 255, 255, 255)]   // trimmed
    public void Parses_hex_and_rgb_forms(string input, int r, int g, int b, int a)
    {
        Assert.True(ColorString.TryParse(input, out ColorRgba c));
        Assert.Equal(((byte)r, (byte)g, (byte)b, (byte)a), (c.R, c.G, c.B, c.A));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("nonsense")]
    [InlineData("#12")]        // invalid length
    [InlineData("#xyzxyz")]    // non-hex
    public void Rejects_invalid(string? input)
    {
        Assert.False(ColorString.TryParse(input, out _));
    }

    [Fact]
    public void Serializes_hex_uppercase_when_opaque_and_preferred()
    {
        Assert.Equal("#15C98F", ColorString.Serialize(0x15, 0xC9, 0x8F, 1.0, preferHex: true));
    }

    [Fact]
    public void Serializes_rgba_when_translucent_even_if_hex_preferred()
    {
        Assert.Equal("rgba(255, 0, 0, 0.5)", ColorString.Serialize(255, 0, 0, 0.5, preferHex: true));
    }

    [Fact]
    public void Serializes_rgba_with_alpha_one_when_rgba_preferred()
    {
        Assert.Equal("rgba(21, 201, 143, 1)", ColorString.Serialize(0x15, 0xC9, 0x8F, 1.0, preferHex: false));
    }

    [Fact]
    public void Alpha_is_rounded_to_three_decimals()
    {
        // 0.1236 -> 0.124 (unambiguous round-up); slider alpha (v/100) needs no rounding.
        Assert.Equal("rgba(0, 0, 0, 0.124)", ColorString.Serialize(0, 0, 0, 0.1236, preferHex: true));
        Assert.Equal("rgba(0, 0, 0, 0.37)", ColorString.Serialize(0, 0, 0, 0.37, preferHex: true));
    }
}

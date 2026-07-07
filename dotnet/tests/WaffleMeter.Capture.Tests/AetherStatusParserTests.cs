using WaffleMeter.Capture;
using Xunit;

namespace WaffleMeter.Capture.Tests;

/// <summary>
/// Golden spec for <see cref="AetherStatusParser"/> against real captured 0x610C aether packets (assembled
/// bodies, marker-based layout). bodyStart = 3 for these (1-byte length var-int + 2 opcode bytes).
/// </summary>
public class AetherStatusParserTests
{
    private const int BodyStart = 3;

    [Fact]
    public void Total_only_marker_reads_the_total()
    {
        // 0F 0C 61 | 00 [08 01 87 93 03] 8A 0F 03  → TotalMarker, total var-int 8A 0F = 1930
        byte[] p = { 0x0F, 0x0C, 0x61, 0x00, 0x08, 0x01, 0x87, 0x93, 0x03, 0x8A, 0x0F, 0x03 };
        AetherParse a = AetherStatusParser.TryParse(p, BodyStart);

        Assert.True(a.Ok);
        Assert.False(a.Split);
        Assert.Equal(1930, a.Total);
    }

    [Fact]
    public void Split_marker_reads_base_and_bonus()
    {
        // 15 0C 61 01 [0C 01 87 93 03] B3 03 87 06 01 A0 00 00 00
        //   SplitMarker, base var-int B3 03 = 435, bonus var-int 87 06 = 775 → total 1210
        byte[] p = { 0x15, 0x0C, 0x61, 0x01, 0x0C, 0x01, 0x87, 0x93, 0x03, 0xB3, 0x03, 0x87, 0x06, 0x01, 0xA0, 0x00, 0x00, 0x00 };
        AetherParse a = AetherStatusParser.TryParse(p, BodyStart);

        Assert.True(a.Ok);
        Assert.True(a.Split);
        Assert.Equal(435, a.Base);
        Assert.Equal(775, a.Bonus);
        Assert.Equal(1210, a.Total);
    }

    [Fact]
    public void Total_only_with_a_trailing_delta_still_reads_the_total()
    {
        // 13 0C 61 01 [08 01 87 93 03] B2 0F 01 28 00 00 00  → total var-int B2 0F = 1970
        byte[] p = { 0x13, 0x0C, 0x61, 0x01, 0x08, 0x01, 0x87, 0x93, 0x03, 0xB2, 0x0F, 0x01, 0x28, 0x00, 0x00, 0x00 };
        AetherParse a = AetherStatusParser.TryParse(p, BodyStart);

        Assert.True(a.Ok);
        Assert.False(a.Split);
        Assert.Equal(1970, a.Total);
    }

    [Fact]
    public void No_marker_yields_no_parse()
    {
        // an unrelated 0x610C variant (04-family counter) carries no aether marker
        byte[] p = { 0x0E, 0x0C, 0x61, 0x00, 0x04, 0x04, 0x00, 0x00, 0x00, 0x0A, 0x03 };
        Assert.False(AetherStatusParser.TryParse(p, BodyStart).Ok);
    }
}

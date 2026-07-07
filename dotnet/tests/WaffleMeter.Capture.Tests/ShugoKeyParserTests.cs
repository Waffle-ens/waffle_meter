using WaffleMeter.Capture;
using Xunit;

namespace WaffleMeter.Capture.Tests;

/// <summary>
/// Golden spec for <see cref="ShugoKeyParser"/>. The shugo-festa key rides the same 0x610B/0x610C status
/// family as aether; the record is identical apart from the per-stat KEY byte — aether is key 0x01, the
/// shugo key is key 0x03. bodyStart = 3 (1-byte length var-int + 2 opcode bytes), matching the aether tests.
/// </summary>
public class ShugoKeyParserTests
{
    private const int BodyStart = 3;

    [Fact]
    public void Split_marker_reads_base_and_bonus()
    {
        // 0E 0C 61 00 [0C 03 87 93 03] 07 03  → SplitMarker (key 03), base 7, bonus 3 → total 10
        byte[] p = { 0x0E, 0x0C, 0x61, 0x00, 0x0C, 0x03, 0x87, 0x93, 0x03, 0x07, 0x03 };
        ShugoKeyParse s = ShugoKeyParser.TryParse(p, BodyStart);

        Assert.True(s.Ok);
        Assert.True(s.Split);
        Assert.Equal(7, s.Base);
        Assert.Equal(3, s.Bonus);
        Assert.Equal(10, s.Total);
    }

    [Fact]
    public void Total_only_marker_reads_the_total()
    {
        // 0D 0C 61 00 [08 03 87 93 03] 0C  → TotalMarker (key 03), total 12
        byte[] p = { 0x0D, 0x0C, 0x61, 0x00, 0x08, 0x03, 0x87, 0x93, 0x03, 0x0C };
        ShugoKeyParse s = ShugoKeyParser.TryParse(p, BodyStart);

        Assert.True(s.Ok);
        Assert.False(s.Split);
        Assert.Equal(12, s.Total);
    }

    [Fact]
    public void Aether_key1_packet_is_not_read_as_a_shugo_key()
    {
        // aether split record (key 01) must not be picked up by the shugo (key 03) scan
        byte[] p = { 0x15, 0x0C, 0x61, 0x01, 0x0C, 0x01, 0x87, 0x93, 0x03, 0xB3, 0x03, 0x87, 0x06, 0x01, 0xA0, 0x00, 0x00, 0x00 };
        Assert.False(ShugoKeyParser.TryParse(p, BodyStart).Ok);
    }

    [Fact]
    public void Shugo_key3_packet_is_not_read_as_aether()
    {
        // conversely, a shugo record (key 03) must not be picked up by the aether (key 01) scan
        byte[] p = { 0x0E, 0x0C, 0x61, 0x00, 0x0C, 0x03, 0x87, 0x93, 0x03, 0x07, 0x03 };
        Assert.False(AetherStatusParser.TryParse(p, BodyStart).Ok);
    }

    [Fact]
    public void Base_above_the_stack_cap_is_rejected()
    {
        // base 20 (0x14) exceeds the 14-key cap → no parse (guards against a coincidental marker match)
        byte[] p = { 0x0E, 0x0C, 0x61, 0x00, 0x0C, 0x03, 0x87, 0x93, 0x03, 0x14, 0x03 };
        Assert.False(ShugoKeyParser.TryParse(p, BodyStart).Ok);
    }

    [Fact]
    public void No_marker_yields_no_parse()
    {
        byte[] p = { 0x0E, 0x0C, 0x61, 0x00, 0x04, 0x04, 0x00, 0x00, 0x00, 0x0A, 0x03 };
        Assert.False(ShugoKeyParser.TryParse(p, BodyStart).Ok);
    }
}

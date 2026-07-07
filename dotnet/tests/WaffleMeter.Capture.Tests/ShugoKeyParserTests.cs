using WaffleMeter.Capture;
using Xunit;

namespace WaffleMeter.Capture.Tests;

/// <summary>
/// Golden spec for <see cref="ShugoKeyParser"/> against a real captured 0x610C shugo-key packet. The shugo
/// key rides the same 0x610B/0x610C family as aether but uses a "header" field layout (04 01 00 00 00 &lt;base&gt;)
/// in its own update packet, disjoint from aether's 87 93 03 record group. bodyStart = 3 (1-byte length
/// var-int + 2 opcode bytes), matching the aether tests.
/// </summary>
public class ShugoKeyParserTests
{
    private const int BodyStart = 3;

    [Fact]
    public void Header_form_reads_the_base_key_count()
    {
        // Real capture (2026-07-07): 0E 0C 61 | 00 [04 01 00 00 00 07] 03 8F 0A  → base 7 (the 8→7 change)
        byte[] p = { 0x0E, 0x0C, 0x61, 0x00, 0x04, 0x01, 0x00, 0x00, 0x00, 0x07, 0x03, 0x8F, 0x0A };
        ShugoKeyParse s = ShugoKeyParser.TryParse(p, BodyStart);

        Assert.True(s.Ok);
        Assert.Equal(7, s.Base);
        Assert.Equal(0, s.Bonus);
        Assert.Equal(7, s.Total);
    }

    [Fact]
    public void Header_form_reads_base_and_bonus_when_the_bonus_field_follows()
    {
        // 04 01 00 00 00 05 (base 5) immediately followed by 04 03 00 00 00 02 (bonus 2) → total 7
        byte[] p = { 0x10, 0x0C, 0x61, 0x00, 0x04, 0x01, 0x00, 0x00, 0x00, 0x05, 0x04, 0x03, 0x00, 0x00, 0x00, 0x02 };
        ShugoKeyParse s = ShugoKeyParser.TryParse(p, BodyStart);

        Assert.True(s.Ok);
        Assert.Equal(5, s.Base);
        Assert.Equal(2, s.Bonus);
        Assert.Equal(7, s.Total);
    }

    [Fact]
    public void Compact_form_reads_base_and_bonus_with_the_fixed_tail()
    {
        // 0C 01 00 00 00 [base 08][bonus 00] then tail 03 03 00 00 00 00
        byte[] p = { 0x0F, 0x0C, 0x61, 0x00, 0x0C, 0x01, 0x00, 0x00, 0x00, 0x08, 0x00, 0x03, 0x03, 0x00, 0x00, 0x00, 0x00 };
        ShugoKeyParse s = ShugoKeyParser.TryParse(p, BodyStart);

        Assert.True(s.Ok);
        Assert.Equal(8, s.Base);
        Assert.Equal(8, s.Total);
    }

    [Fact]
    public void Aether_broadcast_group_is_not_read_as_a_shugo_key()
    {
        // aether split record (0C 01 87 93 03, key 1) has no 04 01 00 00 00 header → no shugo parse
        byte[] p = { 0x15, 0x0C, 0x61, 0x01, 0x0C, 0x01, 0x87, 0x93, 0x03, 0xB3, 0x03, 0x87, 0x06, 0x01, 0xA0, 0x00, 0x00, 0x00 };
        Assert.False(ShugoKeyParser.TryParse(p, BodyStart).Ok);
    }

    [Fact]
    public void Base_above_the_stack_cap_is_rejected()
    {
        // base 20 (0x14) exceeds the 14-key cap → no parse (guards against a coincidental header match)
        byte[] p = { 0x0E, 0x0C, 0x61, 0x00, 0x04, 0x01, 0x00, 0x00, 0x00, 0x14, 0x03, 0x8F, 0x0A };
        Assert.False(ShugoKeyParser.TryParse(p, BodyStart).Ok);
    }

    [Fact]
    public void No_marker_yields_no_parse()
    {
        byte[] p = { 0x0E, 0x0C, 0x61, 0x00, 0x08, 0x08, 0x87, 0x93, 0x03, 0x1C, 0x03 };
        Assert.False(ShugoKeyParser.TryParse(p, BodyStart).Ok);
    }
}

using WaffleMeter.Capture;
using Xunit;

namespace WaffleMeter.Capture.Tests;

/// <summary>
/// Golden spec for <see cref="ShugoKeyParser"/> against real captured 0x610C shugo-key packets. The shugo key
/// rides the same 0x610B/0x610C family — and the same
/// <c>&lt;fieldMask&gt; &lt;resourceKey&gt; &lt;groupId(3)&gt; &lt;values&gt;</c> record layout — as aether, but
/// under group id <c>00 00 00</c> key <c>0x01</c> instead of aether's <c>87 93 03</c>, so the two never collide.
/// bodyStart = 3 (1-byte length var-int + 2 opcode bytes), matching the aether tests.
/// </summary>
public class ShugoKeyParserTests
{
    private const int BodyStart = 3;

    [Theory]
    // real captures — the only shape the live client was ever seen to send for this resource
    [InlineData(new byte[] { 0x0E, 0x0C, 0x61, 0x00, 0x04, 0x01, 0x00, 0x00, 0x00, 0x07, 0x03, 0x87, 0x0A, 0x0A, 0x57 }, 7)]  // 2026-07-04
    [InlineData(new byte[] { 0x0E, 0x0C, 0x61, 0x00, 0x04, 0x01, 0x00, 0x00, 0x00, 0x08, 0x03, 0xEF, 0x09, 0x0A, 0x57 }, 8)]  // 2026-07-07
    [InlineData(new byte[] { 0x0E, 0x0C, 0x61, 0x00, 0x04, 0x01, 0x00, 0x00, 0x00, 0x0B, 0x03, 0xBD, 0x0A, 0x0A, 0x57 }, 11)] // 2026-07-28
    public void Base_only_record_reads_the_key_count(byte[] packet, int expected)
    {
        ShugoKeyParse s = ShugoKeyParser.TryParse(packet, BodyStart);

        Assert.True(s.Ok);
        Assert.Equal(expected, s.Base);
        Assert.Equal(0, s.Bonus);
        Assert.Equal(expected, s.Total);
    }

    /// <summary>The key-0x03 record is a DIFFERENT resource, not this one's bonus field. Reading it as a bonus
    /// (as this parser did until 2026-07-31) would show an unrelated counter as "(+N)" on the badge. Constructed
    /// — the live client was never seen to send it next to a key count.</summary>
    [Fact]
    public void A_neighbouring_key_3_record_is_not_this_resources_bonus()
    {
        byte[] p = { 0x10, 0x0C, 0x61, 0x00, 0x04, 0x01, 0x00, 0x00, 0x00, 0x05, 0x04, 0x03, 0x00, 0x00, 0x00, 0x02 };
        ShugoKeyParse s = ShugoKeyParser.TryParse(p, BodyStart);

        Assert.True(s.Ok);
        Assert.Equal(5, s.Base);
        Assert.Equal(0, s.Bonus);
    }

    /// <summary>Both pools in one record (mask 0x0C), as aether sends. Constructed — not seen for this resource,
    /// but the layout is shared so it is decoded rather than ignored.</summary>
    [Fact]
    public void Both_pools_record_reads_base_and_bonus()
    {
        byte[] p = { 0x0F, 0x0C, 0x61, 0x00, 0x0C, 0x01, 0x00, 0x00, 0x00, 0x08, 0x02, 0x03, 0x03, 0x00, 0x00, 0x00, 0x00 };
        ShugoKeyParse s = ShugoKeyParser.TryParse(p, BodyStart);

        Assert.True(s.Ok);
        Assert.Equal(8, s.Base);
        Assert.Equal(2, s.Bonus);
        Assert.Equal(10, s.Total);
    }

    /// <summary>Bonus pool alone (mask 0x08) — the base pool is omitted because it is zero, never because it is
    /// unchanged. Constructed; the aether corpus is what establishes this reading.</summary>
    [Fact]
    public void Bonus_only_record_leaves_the_base_at_zero()
    {
        byte[] p = { 0x0E, 0x0C, 0x61, 0x00, 0x08, 0x01, 0x00, 0x00, 0x00, 0x03, 0x03, 0x8F, 0x0A };
        ShugoKeyParse s = ShugoKeyParser.TryParse(p, BodyStart);

        Assert.True(s.Ok);
        Assert.Equal(0, s.Base);
        Assert.Equal(3, s.Bonus);
    }

    [Fact]
    public void Aether_record_is_not_read_as_a_shugo_key()
    {
        // aether rides group 87 93 03, not 00 00 00 → no shugo parse
        byte[] p = { 0x15, 0x0C, 0x61, 0x01, 0x0C, 0x01, 0x87, 0x93, 0x03, 0xB3, 0x03, 0x87, 0x06, 0x01, 0xA0, 0x00, 0x00, 0x00 };
        Assert.False(ShugoKeyParser.TryParse(p, BodyStart).Ok);
    }

    /// <summary>The stack cap is also what keeps a coincidental run of zero bytes from being read as a key
    /// count — this group id is three zeroes, far less distinctive than aether's.</summary>
    [Fact]
    public void A_count_above_the_stack_cap_is_rejected()
    {
        byte[] p = { 0x0E, 0x0C, 0x61, 0x00, 0x04, 0x01, 0x00, 0x00, 0x00, 0x14, 0x03, 0x8F, 0x0A };
        Assert.False(ShugoKeyParser.TryParse(p, BodyStart).Ok);
    }

    [Fact]
    public void A_different_resource_key_is_not_a_shugo_key()
    {
        // group 00 00 00 but key 0x0C (a different id=0 resource, seen constantly in the corpus at value 14)
        byte[] p = { 0x0E, 0x0C, 0x61, 0x00, 0x04, 0x0C, 0x00, 0x00, 0x00, 0x0E, 0x02, 0x06, 0x00, 0x36 };
        Assert.False(ShugoKeyParser.TryParse(p, BodyStart).Ok);
    }

    [Fact]
    public void No_record_yields_no_parse()
    {
        byte[] p = { 0x0E, 0x0C, 0x61, 0x00, 0x08, 0x08, 0x87, 0x93, 0x03, 0x1C, 0x03 };
        Assert.False(ShugoKeyParser.TryParse(p, BodyStart).Ok);
    }
}

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

    /// <summary>Real capture 2026-11-13 (0x610B at world entry): an account that filled the item-charged bonus
    /// pool to its cap. Base 14 + bonus 30 = 44. Until 2026-11-13 the bonus pool was held to the base cap 14,
    /// so this whole record was rejected and the badge never appeared for that account.</summary>
    [Fact]
    public void Bonus_pool_filled_to_its_own_cap_of_30_is_read()
    {
        byte[] p = { 0x0F, 0x0B, 0x61, 0x4F, 0x0C, 0x01, 0x00, 0x00, 0x00, 0x0E, 0x1E, 0x03, 0x03, 0x00, 0x00, 0x00, 0x00 };
        ShugoKeyParse s = ShugoKeyParser.TryParse(p, BodyStart);

        Assert.True(s.Ok);
        Assert.Equal(14, s.Base);
        Assert.Equal(30, s.Bonus);
        Assert.Equal(44, s.Total);
    }

    /// <summary>Each pool keeps its own cap: bonus past 30 is still rejected, and the base pool stays at 14
    /// even though the bonus pool may go higher.</summary>
    [Theory]
    [InlineData(new byte[] { 0x0E, 0x0B, 0x61, 0x4F, 0x0C, 0x01, 0x00, 0x00, 0x00, 0x0E, 0x1F, 0x03, 0x03 })] // bonus 31
    [InlineData(new byte[] { 0x0E, 0x0B, 0x61, 0x4F, 0x08, 0x01, 0x00, 0x00, 0x00, 0x1F, 0x03, 0x03, 0x00 })] // bonus-only 31
    [InlineData(new byte[] { 0x0E, 0x0B, 0x61, 0x4F, 0x0C, 0x01, 0x00, 0x00, 0x00, 0x0F, 0x01, 0x03, 0x03 })] // base 15
    public void A_pool_past_its_own_cap_is_rejected(byte[] packet)
    {
        Assert.False(ShugoKeyParser.TryParse(packet, BodyStart).Ok);
    }

    // ── Walked frames (third review 2026-11-13): the last key spent is a currency-1 record with no fields ──

    /// <summary>A 0x610B snapshot body: <c>[varint count][records]</c>. The neighbours are the shapes seen in the
    /// owner's 2026-11-13 dump: a mask-0x03 record (two u64 fields) and an 오드 record (currency 60000001).</summary>
    private static byte[] Snapshot(params byte[][] records)
    {
        var b = new List<byte> { 0x00, 0x0B, 0x61, (byte)records.Length };
        foreach (byte[] r in records)
        {
            b.AddRange(r);
        }

        return b.ToArray();
    }

    private static readonly byte[] TimeRecord =
        [0x03, 0x03, 0x00, 0x00, 0x00, 0x50, 0x8F, 0xCD, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0xF3, 0x6F, 0x06, 0x00, 0x00, 0x00, 0x00];

    private static readonly byte[] AetherRecord = [0x0C, 0x01, 0x87, 0x93, 0x03, 0x4B, 0xD0, 0x0F];

    [Theory]
    [InlineData(new byte[] { 0x0C, 0x01, 0x00, 0x00, 0x00, 0x0E, 0x1E }, 14, 30)] // 11-13 account B
    [InlineData(new byte[] { 0x04, 0x01, 0x00, 0x00, 0x00, 0x0E }, 14, 0)]        // 11-13 account A
    [InlineData(new byte[] { 0x00, 0x01, 0x00, 0x00, 0x00 }, 0, 0)]               // every key spent: no fields
    public void A_walked_snapshot_reads_the_key_including_zero(byte[] shugo, int expectedBase, int expectedBonus)
    {
        byte[] p = Snapshot(shugo, TimeRecord, AetherRecord);

        ShugoKeyParse s = ShugoKeyParser.TryParse(p, BodyStart, fromSnapshot: true);

        Assert.True(s.Ok);
        Assert.Equal((expectedBase, expectedBonus), (s.Base, s.Bonus));
    }

    /// <summary>A 0x610C change notice spending the last key: <c>[optMask][record][reason]</c>.</summary>
    [Fact]
    public void A_change_notice_spending_the_last_key_reads_zero()
    {
        byte[] p = { 0x00, 0x0C, 0x61, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x02 };

        ShugoKeyParse s = ShugoKeyParser.TryParse(p, BodyStart, fromSnapshot: false);

        Assert.True(s.Ok);
        Assert.Equal(0, s.Total);
    }

    [Fact]
    public void A_walked_frame_about_another_resource_says_nothing()
    {
        Assert.False(ShugoKeyParser.TryParse(Snapshot(TimeRecord, AetherRecord), BodyStart, fromSnapshot: true).Ok);
        byte[] notice = { 0x00, 0x0C, 0x61, 0x00, 0x00, 0x03, 0x00, 0x00, 0x00, 0x03 };
        Assert.False(ShugoKeyParser.TryParse(notice, BodyStart, fromSnapshot: false).Ok);
    }

    /// <summary>A frame that does not end where its records do proves nothing, so a fieldless record in it is never
    /// read as zero — the fallback scan, which cannot invent a zero, decides.</summary>
    [Fact]
    public void A_frame_that_does_not_walk_never_yields_a_zero()
    {
        byte[] p = [.. Snapshot([0x00, 0x01, 0x00, 0x00, 0x00], AetherRecord), 0x7F, 0x7F];

        Assert.False(ShugoKeyParser.TryParse(p, BodyStart, fromSnapshot: true).Ok);
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

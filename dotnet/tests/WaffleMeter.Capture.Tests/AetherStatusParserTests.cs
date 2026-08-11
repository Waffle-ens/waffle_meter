using WaffleMeter.Capture;
using Xunit;

namespace WaffleMeter.Capture.Tests;

/// <summary>
/// Golden spec for <see cref="AetherStatusParser"/> against real captured 0x610C aether packets.
/// bodyStart = 3 for these (1-byte length var-int + 2 opcode bytes).
///
/// <para>Records are <c>&lt;fieldMask&gt; &lt;resourceKey&gt; 87 93 03 &lt;value var-ints&gt;</c>. The mask says
/// which of the two 오드 pools the record carries — 0x04 = 자연회복 only, 0x08 = 추가 only, 0x0C = both — and a
/// pool is omitted exactly when it is zero.</para>
/// </summary>
public class AetherStatusParserTests
{
    private const int BodyStart = 3;

    [Fact]
    public void Both_pools_record_reads_natural_and_bonus()
    {
        // 10 0C 61 00 | [0C] 01 87 93 03 | 5F | 98 02 | 03 08 EC FF 04 03
        //   mask 0x0C = both: 자연회복 var-int 5F = 95, 추가 var-int 98 02 = 280
        byte[] p = { 0x10, 0x0C, 0x61, 0x00, 0x0C, 0x01, 0x87, 0x93, 0x03, 0x5F, 0x98, 0x02, 0x03, 0x08, 0xEC, 0xFF, 0x04, 0x03 };
        AetherParse a = AetherStatusParser.TryParse(p, BodyStart);

        Assert.True(a.Ok);
        Assert.Equal(95, a.Base);
        Assert.Equal(280, a.Bonus);
        Assert.Equal(375, a.Total);
    }

    [Fact]
    public void Both_pools_record_with_a_trailing_change_amount()
    {
        // 15 0C 61 01 | [0C] 01 87 93 03 | B3 03 | 87 06 | 01 A0 00 00 00
        //   자연회복 435, 추가 775; the "01 <u32>" tail is the amount that changed, not a third pool.
        byte[] p = { 0x15, 0x0C, 0x61, 0x01, 0x0C, 0x01, 0x87, 0x93, 0x03, 0xB3, 0x03, 0x87, 0x06, 0x01, 0xA0, 0x00, 0x00, 0x00 };
        AetherParse a = AetherStatusParser.TryParse(p, BodyStart);

        Assert.True(a.Ok);
        Assert.Equal(435, a.Base);
        Assert.Equal(775, a.Bonus);
        Assert.Equal(1210, a.Total);
    }

    /// <summary>The regression this parser was rewritten for (2026-07-30). A 추가-only record is NOT a total:
    /// reading it as one made the data layer back-compute a delta and credit it to 자연회복, so using a
    /// 오드 회복 소모품 grew the number OUTSIDE the parentheses instead of the one inside.</summary>
    [Fact]
    public void Bonus_only_record_is_the_additional_pool_not_a_total()
    {
        // 0F 0C 61 00 | [08] 01 87 93 03 | EB 01 | 03 08 EC FF 04 05   → 추가 235, 자연회복 omitted (= 0)
        byte[] p = { 0x0F, 0x0C, 0x61, 0x00, 0x08, 0x01, 0x87, 0x93, 0x03, 0xEB, 0x01, 0x03, 0x08, 0xEC, 0xFF, 0x04, 0x05 };
        AetherParse a = AetherStatusParser.TryParse(p, BodyStart);

        Assert.True(a.Ok);
        Assert.Equal(0, a.Base);
        Assert.Equal(235, a.Bonus);
        Assert.Equal(235, a.Total);
    }

    /// <summary>A real 오드 회복 소모품 use: a 추가-only record whose trailing change amount is 0x28 = 40.</summary>
    [Fact]
    public void Consumable_grant_lands_entirely_in_the_additional_pool()
    {
        // 13 0C 61 01 | [08] 01 87 93 03 | B2 0F | 01 28 00 00 00   → 추가 1970, changed by +40
        byte[] p = { 0x13, 0x0C, 0x61, 0x01, 0x08, 0x01, 0x87, 0x93, 0x03, 0xB2, 0x0F, 0x01, 0x28, 0x00, 0x00, 0x00 };
        AetherParse a = AetherStatusParser.TryParse(p, BodyStart);

        Assert.True(a.Ok);
        Assert.Equal(0, a.Base);
        Assert.Equal(1970, a.Bonus);
    }

    /// <summary>Mask 0x04 = the 자연회복 pool alone (추가 omitted, i.e. zero). Real record bytes
    /// (<c>04 01 87 93 03 78</c> followed by the neighbouring key-02 and key-03 records) lifted out of a large
    /// status broadcast and given a 0x610C header, since this form only ever appears mid-broadcast.</summary>
    [Fact]
    public void Natural_only_record_is_the_natural_pool()
    {
        byte[] p = { 0x0F, 0x0C, 0x61, 0x00, 0x04, 0x01, 0x87, 0x93, 0x03, 0x78,
                     0x04, 0x02, 0x87, 0x93, 0x03, 0x02, 0x04, 0x03, 0x06, 0x00, 0x11, 0x04 };
        AetherParse a = AetherStatusParser.TryParse(p, BodyStart);

        Assert.True(a.Ok);
        Assert.Equal(120, a.Base);
        Assert.Equal(0, a.Bonus);
    }

    /// <summary>Mask 0x00 = BOTH pools omitted, i.e. a balance of exactly zero — the game leaves out a pool
    /// precisely when it is empty, so an empty mask carries no value fields at all.
    /// <para>Until 2026-08-11 this fell through as a parse failure, which the badge could not tell apart from
    /// "no reading has ever arrived": a character that had spent everything got no footer badge until the next
    /// 자연회복 tick, up to three hours later. Zero is a balance.</para></summary>
    [Fact]
    public void Empty_mask_is_a_balance_of_zero_not_a_failed_parse()
    {
        // 0E 0C 61 00 | [00] 01 87 93 03 | (no value fields) followed by the neighbouring key-02 record
        byte[] p = { 0x0E, 0x0C, 0x61, 0x00, 0x00, 0x01, 0x87, 0x93, 0x03,
                     0x04, 0x02, 0x87, 0x93, 0x03, 0x02 };
        AetherParse a = AetherStatusParser.TryParse(p, BodyStart);

        Assert.True(a.Ok);
        Assert.Equal(0, a.Base);
        Assert.Equal(0, a.Bonus);
        Assert.Equal(0, a.Total);
    }

    /// <summary>Two packets arrived in one segment; only the first 오드 record is read and the parse must not
    /// run on into the following key-02 resource record.</summary>
    [Fact]
    public void Stops_at_the_first_aether_record_in_a_multi_record_segment()
    {
        byte[] p = { 0x11, 0x0C, 0x61, 0x00, 0x0C, 0x01, 0x87, 0x93, 0x03, 0xC1, 0x05, 0xA5, 0x01, 0x03,
                     0x0F, 0x0C, 0x61, 0x00, 0x0C, 0x02, 0x87, 0x93, 0x03, 0x0D, 0x06, 0x03, 0x08, 0xEC, 0xFF, 0x04, 0x04, 0x06, 0x00, 0x36 };
        AetherParse a = AetherStatusParser.TryParse(p, BodyStart);

        Assert.True(a.Ok);
        Assert.Equal(705, a.Base);
        Assert.Equal(165, a.Bonus);
    }

    /// <summary>Another resource rides the same group id under a different key — it must never be read as 오드.</summary>
    [Fact]
    public void A_different_resource_key_is_not_aether()
    {
        // 0E 0C 61 00 | 08 [08] 87 93 03 | 1C 03   → key 0x08, not the 오드 key 0x01
        byte[] p = { 0x0E, 0x0C, 0x61, 0x00, 0x08, 0x08, 0x87, 0x93, 0x03, 0x1C, 0x03 };
        Assert.False(AetherStatusParser.TryParse(p, BodyStart).Ok);
    }

    [Fact]
    public void No_marker_yields_no_parse()
    {
        // an unrelated 0x610C variant (04-family counter) carries no aether record
        byte[] p = { 0x0E, 0x0C, 0x61, 0x00, 0x04, 0x04, 0x00, 0x00, 0x00, 0x0A, 0x03 };
        Assert.False(AetherStatusParser.TryParse(p, BodyStart).Ok);
    }

    [Fact]
    public void An_out_of_range_value_is_rejected()
    {
        // mask 0x08 with a var-int far past any real balance (0xFF 0xFF 0x7F = 2,097,151)
        byte[] p = { 0x0F, 0x0C, 0x61, 0x00, 0x08, 0x01, 0x87, 0x93, 0x03, 0xFF, 0xFF, 0x7F, 0x03 };
        Assert.False(AetherStatusParser.TryParse(p, BodyStart).Ok);
    }
}

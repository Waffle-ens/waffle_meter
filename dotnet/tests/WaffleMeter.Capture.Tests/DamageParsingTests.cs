using WaffleMeter.Capture;
using Xunit;

namespace WaffleMeter.Capture.Tests;

/// <summary>Unit spec for the pure damage helpers ported from Kotlin StreamProcessor.</summary>
public class DamageParsingTests
{
    // --- ParseSpecialDamageFlags ---

    [Fact]
    public void Special_flags_size_8_is_empty()
    {
        Assert.Empty(DamageParsing.ParseSpecialDamageFlags(new byte[8]));
    }

    [Fact]
    public void Special_flags_size_9_is_empty()
    {
        // Neither the ==8 nor the >=10 branch applies.
        Assert.Empty(DamageParsing.ParseSpecialDamageFlags(new byte[9]));
    }

    // NOTE: the 2026-07-01 patch rotated the on-wire flag byte RIGHT by 1 bit, so these inputs are the
    // post-patch RAW bytes; the parser rotates LEFT by 1 before applying the masks. (raw = decoded ror 1.)

    [Fact]
    public void Special_flags_decode_individual_bits_when_size_at_least_10()
    {
        var p = new byte[10];
        p[0] = 0x80; // ror(BACK 0x01) — back-attack; regressed to 0% before the rotate fix
        Assert.Equal(new[] { SpecialDamage.BACK }, DamageParsing.ParseSpecialDamageFlags(p));

        p[0] = 0x02; // ror(PARRY 0x04)
        Assert.Equal(new[] { SpecialDamage.PARRY }, DamageParsing.ParseSpecialDamageFlags(p));

        p[0] = 0x04; // ror(PERFECT 0x08)
        Assert.Equal(new[] { SpecialDamage.PERFECT }, DamageParsing.ParseSpecialDamageFlags(p));

        p[0] = 0x08; // ror(DOUBLE 0x10) — 강타; regressed to 0% before the rotate fix
        Assert.Equal(new[] { SpecialDamage.DOUBLE }, DamageParsing.ParseSpecialDamageFlags(p));
    }

    [Fact]
    public void Special_flags_decode_multiple_bits_in_order_and_ignore_0x80()
    {
        var p = new byte[12];
        // ror(BACK 0x01 | DOUBLE 0x10 | Restoration 0x40 | POWER_SHARD 0x80) = ror(0xD1) = 0xE8.
        // Decodes back to 0xD1; the 0x80 (POWER_SHARD) bit stays ignored.
        p[0] = 0xE8;
        Assert.Equal(
            new[] { SpecialDamage.BACK, SpecialDamage.DOUBLE, SpecialDamage.Restoration },
            DamageParsing.ParseSpecialDamageFlags(p));
    }

    // --- TryParseMultiHit ---

    [Fact]
    public void MultiHit_count_zero_returns_unchanged_offset()
    {
        var data = new byte[] { 0x00, 0x05 };
        MultiHitOutput r = DamageParsing.TryParseMultiHit(data, 0);
        Assert.Equal(0, r.Time);
        Assert.Equal(0, r.NewOffset); // offset unchanged
    }

    [Fact]
    public void MultiHit_three_identical_values_is_accepted()
    {
        // count=3, then three identical damage varints (value 7 each, 1 byte)
        var data = new byte[] { 0x03, 0x07, 0x07, 0x07 };
        MultiHitOutput r = DamageParsing.TryParseMultiHit(data, 0);
        Assert.Equal(3, r.Time);
        Assert.Equal(7, r.Damage);
        Assert.Equal(4, r.NewOffset); // consumed count + 3 varints
    }

    [Fact]
    public void MultiHit_mismatched_value_resets_to_original_offset()
    {
        var data = new byte[] { 0x03, 0x07, 0x07, 0x09 }; // third differs
        MultiHitOutput r = DamageParsing.TryParseMultiHit(data, 0);
        Assert.Equal(0, r.Time);
        Assert.Equal(0, r.NewOffset);
    }

    [Fact]
    public void MultiHit_first_value_zero_is_rejected()
    {
        var data = new byte[] { 0x02, 0x00, 0x00 };
        MultiHitOutput r = DamageParsing.TryParseMultiHit(data, 0);
        Assert.Equal(0, r.Time);
    }

    // --- NormalizeDamageSkillCode ---

    [Fact]
    public void Skill_normalize_returns_first_candidate_present_in_catalog()
    {
        // rawCode 12345: candidates include 12345, 12340, 1234, 1230, 123, 120.
        var catalog = new HashSet<long> { 1230 };
        int r = DamageParsing.NormalizeDamageSkillCode(12345, fallback: -1, code => catalog.Contains(code));
        Assert.Equal(1230, r);
    }

    [Fact]
    public void Skill_normalize_prefers_earlier_candidate()
    {
        var catalog = new HashSet<long> { 12340, 1230 };
        int r = DamageParsing.NormalizeDamageSkillCode(12345, fallback: -1, code => catalog.Contains(code));
        Assert.Equal(12340, r); // 12340 comes before 1230 in candidate order
    }

    [Fact]
    public void Skill_normalize_falls_back_when_no_candidate_matches()
    {
        int r = DamageParsing.NormalizeDamageSkillCode(12345, fallback: 999, _ => false);
        Assert.Equal(999, r);
    }
}

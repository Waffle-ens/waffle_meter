using WaffleMeter.App.Core;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

/// <summary>
/// Spec for decoding a skill's specialization (특화) from its full wire code — the last four decimal
/// digits, ones digit dropped, remaining digits (1..5) name active slots. Values verified against a real
/// 151,937-hit capture (e.g. 13040240 → {2,4}, 13042350 → {2,3,5}, 16040340 → {3,4}).
/// </summary>
public sealed class SkillSpecializationTests
{
    private static int[] Slots(int rawCode)
    {
        bool[]? s = SkillSpecialization.Decode(rawCode);
        Assert.NotNull(s);
        var slots = new List<int>();
        for (int i = 0; i < s!.Length; i++)
        {
            if (s[i])
            {
                slots.Add(i + 1);
            }
        }

        return slots.ToArray();
    }

    [Theory]
    [InlineData(13040240, new[] { 2, 4 })]     // 024 → slots 2,4
    [InlineData(13042350, new[] { 2, 3, 5 })]  // 235 → slots 2,3,5
    [InlineData(16040340, new[] { 3, 4 })]     // 034 → slots 3,4
    [InlineData(13351351, new[] { 1, 3, 5 })]  // 135 (ones digit 1 dropped) → 1,3,5
    [InlineData(11340028, new[] { 2 })]        // 002 → slot 2
    public void Decodes_active_slots_from_the_last_four_digits(int rawCode, int[] expected)
    {
        Assert.Equal(expected, Slots(rawCode));
    }

    [Fact]
    public void No_specialization_when_only_a_charge_step_is_present()
    {
        // 13720005..09: the last four digits are just a ones-digit charge step, no slot digits.
        Assert.Null(SkillSpecialization.Decode(13720005));
        Assert.Null(SkillSpecialization.Decode(13720009));
    }

    [Fact]
    public void Only_player_skills_carry_specialization()
    {
        Assert.Null(SkillSpecialization.Decode(0));           // no code
        Assert.Null(SkillSpecialization.Decode(100014));      // basic attack (below the player-skill band)
        Assert.Null(SkillSpecialization.Decode(3000119));     // theostone orb — would decode to garbage
        Assert.Null(SkillSpecialization.Decode(23010890));    // mob/boss skill (above the band)
    }

    [Fact]
    public void An_out_of_range_slot_digit_yields_no_guess()
    {
        // A digit of 6 or above isn't a valid slot (the game exposes only 5) → treat as undecodable.
        Assert.Null(SkillSpecialization.Decode(13040690)); // 069 → digit 6 invalid
    }

    [Fact]
    public void Floors_a_code_that_carries_trailing_framing_digits()
    {
        // Codes can arrive with extra trailing digits (>8 digits); floor to the 8-digit skill code first.
        Assert.Equal(new[] { 2, 4 }, Slots(130402401)); // → 13040240 → {2,4}
    }
}

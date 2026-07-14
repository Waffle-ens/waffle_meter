namespace WaffleMeter.App.Core;

/// <summary>
/// Decodes a skill's specialization (특화) from the FULL wire skill code. The game does not send
/// specialization in a dedicated field or via the official API — it is baked into the skill code's last
/// four decimal digits. Dropping the ones digit (a charge/variant sub-index), each of the remaining
/// tens/hundreds/thousands digits (constrained to 1..5) names one active specialization slot. So a base
/// skill 13040000 cast as 13040240 has slots {2,4}; cast as 13042350 has {2,3,5}. Verified against a real
/// 151,937-hit capture and matched to the client's own decode.
/// <para>Pure so it is unit-testable and shared by the in-meter detail panel.</para>
/// </summary>
public static class SkillSpecialization
{
    /// <summary>The number of specialization slots the game exposes (the client draws five pips).</summary>
    public const int SlotCount = 5;

    /// <summary>
    /// Which of the five specialization slots this cast had active, as a length-5 bool array (index i =
    /// slot i+1). Returns null when the code carries no decodable specialization: only player skills
    /// (8-digit codes in the 11_000_000..19_999_999 band) do — basic attacks, theostone orbs (3xxxxxx),
    /// mob skills, and DoT-only rows have none, and a malformed suffix (a digit outside 1..5) yields null
    /// rather than a wrong guess.
    /// </summary>
    public static bool[]? Decode(int rawSkillCode)
    {
        if (rawSkillCode <= 0)
        {
            return null;
        }

        // The wire code can exceed 8 digits (trailing framing); floor to the 8-digit skill code first, the
        // same normalization the client applies before splitting off the specialization digits.
        long code = rawSkillCode;
        while (code > 99_999_999)
        {
            code /= 10;
        }

        // Only real player skills carry specialization. Their base (last four digits zeroed) sits in the
        // 11M..19.99M band; anything else (theostone 3xxxxxx, basic attacks, mob skills) has no build.
        long baseCode = code - code % 10_000;
        if (baseCode is < 11_000_000 or > 19_999_999)
        {
            return null;
        }

        int value = (int)(code - baseCode) / 10; // drop the ones digit; the rest are the slot digits
        if (value is < 1 or > 999)
        {
            return null;
        }

        var slots = new bool[SlotCount];
        bool any = false;
        while (value > 0)
        {
            int digit = value % 10;
            if (digit is < 1 or > SlotCount)
            {
                return null; // an out-of-range digit means this isn't a specialization suffix
            }

            slots[digit - 1] = true;
            any = true;
            value /= 10;
        }

        return any ? slots : null;
    }
}

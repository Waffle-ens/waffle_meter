namespace WaffleMeter.Data;

/// <summary>
/// Player job/class. Verbatim port of Kotlin <c>entity/enums/JobClass.kt</c>. Serializes as its
/// Korean <see cref="ClassName"/> (JobClassSerializer) — e.g. "검성".
/// </summary>
public enum JobClass
{
    GLADIATOR,
    TEMPLAR,
    RANGER,
    ASSASSIN,
    SORCERER,
    CLERIC,
    ELEMENTALIST,
    CHANTER,
}

public static class JobClassInfo
{
    public static string ClassName(this JobClass job) => job switch
    {
        JobClass.GLADIATOR => "검성",
        JobClass.TEMPLAR => "수호성",
        JobClass.RANGER => "궁성",
        JobClass.ASSASSIN => "살성",
        JobClass.SORCERER => "마도성",
        JobClass.CLERIC => "치유성",
        JobClass.ELEMENTALIST => "정령성",
        JobClass.CHANTER => "호법성",
        _ => "",
    };

    /// <summary>Kotlin JobClass.convertFromSkill (skill-code range -> job).</summary>
    public static JobClass? ConvertFromSkill(int skillCode) => skillCode switch
    {
        >= 11000000 and <= 11999999 => JobClass.GLADIATOR,
        >= 12000000 and <= 12999999 => JobClass.TEMPLAR,
        >= 13000000 and <= 13999999 => JobClass.ASSASSIN,
        >= 14000000 and <= 14999999 => JobClass.RANGER,
        >= 15000000 and <= 15999999 => JobClass.SORCERER,
        >= 16000000 and <= 16999999 => JobClass.ELEMENTALIST,
        >= 17000000 and <= 17999999 => JobClass.CLERIC,
        >= 18000000 and <= 18999999 => JobClass.CHANTER,
        _ => null,
    };

    /// <summary>Kotlin JobClass.convertFromCode (packet job byte -> job).</summary>
    public static JobClass? ConvertFromCode(int job) => job switch
    {
        13 or 14 or 15 or 16 => JobClass.RANGER,
        33 or 34 or 35 or 36 => JobClass.CHANTER,
        17 or 18 or 19 or 20 => JobClass.ASSASSIN,
        29 or 30 or 31 or 32 => JobClass.CLERIC,
        21 or 22 or 23 or 24 => JobClass.ELEMENTALIST,
        25 or 26 or 27 or 28 => JobClass.SORCERER,
        5 or 6 or 7 or 8 => JobClass.GLADIATOR,
        9 or 10 or 11 or 12 => JobClass.TEMPLAR,
        _ => null,
    };

    /// <summary>Kotlin JobClass.basicSkillCode (job -> base skill code; used for buff-source job match).</summary>
    public static int BasicSkillCode(this JobClass job) => job switch
    {
        JobClass.GLADIATOR => 11020000,
        JobClass.TEMPLAR => 12010000,
        JobClass.RANGER => 14020000,
        JobClass.ASSASSIN => 13010000,
        JobClass.SORCERER => 15210000,
        JobClass.CLERIC => 17010000,
        JobClass.ELEMENTALIST => 16010000,
        JobClass.CHANTER => 18010000,
        _ => 0,
    };
}

namespace WaffleMeter.Capture;

/// <summary>The four knobs the party sets before entering 시련: 바크론의 공중섬. Each is 1~4 and the
/// displayed difficulty is their SUM, so the level runs 4~16.</summary>
public enum TrialAffixGroup
{
    /// <summary>제한 시간 — 1800/1200/900/600s. Carried by the instance phase window, not by a buff.</summary>
    Timelimit = 0,

    /// <summary>부활 제한 — 3/2/1/0 revives. No observed carrier yet.</summary>
    Rebirthlimit = 1,

    /// <summary>보스 강화 — the only knob that touches boss stats: max HP x1.0/1.3/1.7/2.2 plus damage
    /// amplification. This is the axis that actually distorts a DPS percentile.</summary>
    BossBuff = 2,

    /// <summary>바크론 패턴 강화 — extra/upgraded boss mechanics.</summary>
    BakronSkillUpgrade = 3,
}

/// <summary>One observed affix setting.</summary>
public readonly record struct TrialAffix(TrialAffixGroup Group, int Level);

/// <summary>
/// Maps the 시련 difficulty affixes to the wire.
/// <para>Two of the four groups are implemented as ordinary abnormal (buff) codes and are broadcast on the
/// dungeon's mobs, so the chosen level can be read straight off a buff-apply packet — the code identifies the
/// level 1:1, no value decoding needed. Each group has a hidden first-stage code that casts a system skill and
/// a visible second-stage code that the skill applies; both are listed because both appear on the wire.</para>
/// <para>The remaining two groups (제한 시간 / 부활 제한) are not abnormals — they are instance settings, so
/// they can never appear here. 제한 시간 is recoverable from the instance's phase window; 부활 제한 has no
/// known carrier.</para>
/// </summary>
public static class TrialAffixCatalog
{
    /// <summary>Total difficulty is the sum of the four groups, each 1..4.</summary>
    public const int MinLevel = 4;
    public const int MaxLevel = 16;

    /// <summary>How many groups make up the total.</summary>
    public const int GroupCount = 4;

    /// <summary>Seconds of dungeon time per 제한 시간 level (index 0 = level 1).</summary>
    private static readonly int[] TimelimitSeconds = [1800, 1200, 900, 600];

    private static readonly Dictionary<int, TrialAffix> ByCode = Build();

    private static Dictionary<int, TrialAffix> Build()
    {
        var map = new Dictionary<int, TrialAffix>();
        // 보스 강화 N단계 — hidden caster 19993x01, visible abnormal 19993x11.
        Add(map, TrialAffixGroup.BossBuff, [19993401, 19993501, 19993601, 19993701]);
        Add(map, TrialAffixGroup.BossBuff, [19993411, 19993511, 19993611, 19993711]);
        // 바크론 패턴 강화 N단계 — hidden caster 198063x1, visible abnormal 198063x2.
        Add(map, TrialAffixGroup.BakronSkillUpgrade, [19806301, 19806311, 19806321, 19806331]);
        Add(map, TrialAffixGroup.BakronSkillUpgrade, [19806302, 19806312, 19806322, 19806332]);
        return map;
    }

    private static void Add(Dictionary<int, TrialAffix> map, TrialAffixGroup group, int[] codes)
    {
        for (int i = 0; i < codes.Length; i++)
        {
            map[codes[i]] = new TrialAffix(group, i + 1);
        }
    }

    /// <summary>True when this abnormal code is one of the trial's difficulty affixes.</summary>
    public static bool TryResolve(int skillCode, out TrialAffix affix) => ByCode.TryGetValue(skillCode, out affix);

    /// <summary>Whether any trial affix uses this code — the cheap pre-check the buff parser runs before its
    /// own drop rules, since these codes sit below the job-buff band and carry an indefinite duration and so
    /// would be discarded twice over.</summary>
    public static bool IsAffixCode(int skillCode) => ByCode.ContainsKey(skillCode);

    /// <summary>The 제한 시간 level a dungeon time budget implies, or 0 when it matches no level. The window
    /// is exact (it comes from the instance, not from a timer the client runs), so this is a lookup rather
    /// than a nearest-match.</summary>
    public static int TimelimitLevelForSeconds(long seconds)
    {
        for (int i = 0; i < TimelimitSeconds.Length; i++)
        {
            if (TimelimitSeconds[i] == seconds)
            {
                return i + 1;
            }
        }

        return 0;
    }
}

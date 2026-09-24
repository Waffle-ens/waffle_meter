namespace WaffleMeter.Capture;

/// <summary>The four knobs the party sets before entering 시련: 바크론의 공중섬. Each is 1~4 and the
/// displayed difficulty is their SUM, so the level runs 4~16.</summary>
public enum TrialAffixGroup
{
    /// <summary>제한 시간 — 1800/1200/900/600s. Carried by the instance phase window, not by a buff.</summary>
    Timelimit = 0,

    /// <summary>부활 제한 — 3/2/1/0 revives. Carried by the room's <c>_affix_list</c> (0x9702 tail), which is
    /// where all four knobs turn out to live; confirmed by driving it 1→2→3→4 while the other three held.</summary>
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
/// <para>The remaining two groups (제한 시간 / 부활 제한) are not abnormals, so they can never appear in the
/// code map here. 제한 시간 is also recoverable from the instance's phase window.</para>
/// <para><b>All four, however, ride the room itself.</b> The 0x9702 room snapshot ends with
/// <c>_affix_list</c> — see <see cref="TryDecodeAffixQuad"/>. That path is the primary one now: it carries
/// every knob, it arrives before the instance does, and it is what pins the level to a number instead of a
/// range. The abnormal codes below stay as an independent second source for cross-checking.</para>
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

    private const int BakronTrial = 0;
    private const int FireTempleTrial = 1;

    /// <summary>
    /// <c>DungeonTrialAffix.dat</c> 의 ID → (어느 시련, 축 슬롯, 레벨). 방 스냅샷의 <c>_affix_list</c> 가 싣는 값이 이 ID 다.
    /// <para>🔴 <b>"축*10+레벨" 은 공식이 아니다.</b> 바크론의 ID(1~4/11~14/21~24/31~34)가 우연히 그 모양이었을
    /// 뿐이고, 불의 신전(클라 112)은 37~52 에 축 순서도 섞여 붙었다. 공식으로 풀면 제한 시간(47~49)이 "축 4"가
    /// 되어 방이 통째로 버려진다 — v3.2.5 의 불의 신전 시련 런이 전부 시련 블록 없이 올라간 이유다.
    /// 새 시련이 오면 .dat 을 보고 여기 행을 더한다.</para>
    /// </summary>
    private static readonly Dictionary<int, (int Trial, TrialAffixGroup Group, int Level)> AffixById = BuildAffixIds();

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

    private static Dictionary<int, (int, TrialAffixGroup, int)> BuildAffixIds()
    {
        var map = new Dictionary<int, (int, TrialAffixGroup, int)>();
        // 바크론의 공중섬 — Timelimit / Rebirthlimit / BossBuff / BakronSkillUpgrade, 각 1~4.
        AddIds(map, BakronTrial, TrialAffixGroup.Timelimit, [1, 2, 3, 4]);
        AddIds(map, BakronTrial, TrialAffixGroup.Rebirthlimit, [11, 12, 13, 14]);
        AddIds(map, BakronTrial, TrialAffixGroup.BossBuff, [21, 22, 23, 24]);
        AddIds(map, BakronTrial, TrialAffixGroup.BakronSkillUpgrade, [31, 32, 33, 34]);
        // 불의 신전 — Timelimit_2 1~3 / Rebirthlimit_2 1~3 / BossBuff_2 1~8 / Pc_debuff_1 1~2.
        // 네 번째 축은 슬롯 3(skillUpgrade)을 재사용한다(웹과 합의 — 축 정체는 맵 id 로 가른다).
        AddIds(map, FireTempleTrial, TrialAffixGroup.Timelimit, [47, 48, 49]);
        AddIds(map, FireTempleTrial, TrialAffixGroup.Rebirthlimit, [50, 51, 52]);
        AddIds(map, FireTempleTrial, TrialAffixGroup.BossBuff, [37, 38, 39, 40, 41, 42, 43, 44]);
        AddIds(map, FireTempleTrial, TrialAffixGroup.BakronSkillUpgrade, [45, 46]);
        return map;
    }

    private static void AddIds(
        Dictionary<int, (int, TrialAffixGroup, int)> map, int trial, TrialAffixGroup group, int[] ids)
    {
        for (int i = 0; i < ids.Length; i++)
        {
            map[ids[i]] = (trial, group, i + 1);
        }
    }

    /// <summary>True when this abnormal code is one of the trial's difficulty affixes.</summary>
    public static bool TryResolve(int skillCode, out TrialAffix affix) => ByCode.TryGetValue(skillCode, out affix);

    /// <summary>Whether any trial affix uses this code — the cheap pre-check the buff parser runs before its
    /// own drop rules, since these codes sit below the job-buff band and carry an indefinite duration and so
    /// would be discarded twice over.</summary>
    public static bool IsAffixCode(int skillCode) => ByCode.ContainsKey(skillCode);

    /// <summary>
    /// Decode the room's <c>_affix_list</c> — four <c>i32</c>, each a <c>DungeonTrialAffix.dat</c> ID that
    /// <see cref="AffixById"/> resolves to (trial, axis, level). Levels come out in <see cref="TrialAffixGroup"/>
    /// order; their ceilings are per-dungeon, so range-checking them is the data layer's job.
    /// <para><b>fail-closed.</b> Every ID must be known, all four must belong to the same trial, and every axis
    /// must appear exactly once, or the whole quad is thrown away. A wrong stage label is worse than no label —
    /// it files the run under a difficulty it was not played at, and a tier percentile is what consumes it.
    /// Measured against 505 non-trial room snapshots across nine dungeon ids plus three coincidental outbound
    /// matches: all rejected, zero false positives.</para>
    /// </summary>
    public static bool TryDecodeAffixQuad(ReadOnlySpan<int> raw, out int[] levels)
    {
        levels = new int[GroupCount];
        if (raw.Length != GroupCount)
        {
            return false;
        }

        int seen = 0;
        int trial = -1;
        foreach (int value in raw)
        {
            if (!AffixById.TryGetValue(value, out (int Trial, TrialAffixGroup Group, int Level) affix))
            {
                return false; // 표에 없는 ID = 어픽스 배열이 아니거나 아직 모르는 시련
            }

            if (trial >= 0 && affix.Trial != trial)
            {
                return false; // 한 방이 두 시련의 어픽스를 함께 실을 수는 없다
            }

            trial = affix.Trial;
            int axis = (int)affix.Group;
            if ((seen & (1 << axis)) != 0)
            {
                return false; // 같은 축이 두 번 = 어픽스 배열이 아니다
            }

            seen |= 1 << axis;
            levels[axis] = affix.Level;
        }

        return seen == (1 << GroupCount) - 1;
    }

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

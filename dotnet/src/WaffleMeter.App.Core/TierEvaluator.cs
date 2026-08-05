using WaffleMeter.Data;

namespace WaffleMeter.App.Core;

/// <summary>What the tier layer knows about one visible row.
/// <para><paramref name="TierRank"/> 1..8. When <paramref name="IsCareer"/> is true it is the character's
/// server-computed career tier; otherwise it is derived from THIS fight's percentile and must be worded as
/// 이번 전투 등급, never as a standing.</para>
/// <para><paramref name="BattleTopPercent"/> is this fight's locally computed percentile, null when the cohort
/// shipped no distribution row (표본 부족) — the caller renders nothing rather than guessing.</para></summary>
public readonly record struct RowTier(
    int TierRank,
    double? BattleTopPercent,
    string? DungeonLabel = null,
    bool IsCareer = false);

/// <summary>
/// Turns a live/finished report into per-row tier state, entirely locally.
/// <para>The live percentile for EVERY row — self and party members alike — is computed here from the
/// downloaded distribution artifact: that row's dps against its own class cohort. It costs no request and
/// discloses nothing beyond the combat packets the row already renders. Only the career tier (a standing over
/// weeks) comes from the server, and only for characters that consented.</para>
/// </summary>
public static class TierEvaluator
{
    /// <summary>The four support classes whose presence defines a party's synergy, and their bit values.
    /// Must stay byte-identical to the web's <c>SYNERGY_BITS</c> or the cohort key silently diverges.</summary>
    private static readonly (JobClass Job, int Bit)[] SynergyBits =
    [
        (JobClass.TEMPLAR, 1),   // 수호성
        (JobClass.GLADIATOR, 2), // 검성
        (JobClass.CHANTER, 4),   // 호법성
        (JobClass.CLERIC, 8),    // 치유성
    ];

    /// <summary>
    /// Build the uid → tier map for a report.
    /// </summary>
    /// <param name="careerTiers">Server-supplied career tiers keyed by identity hash (self from the upload
    /// response, others from a consent-gated batch lookup). Rows missing here still get a 이번 전투 등급.</param>
    /// <param name="identityHashOf">Resolves a row's identity hash; returns null when the character is not
    /// identified yet (a bare mid-join actor), in which case only the local percentile applies.</param>
    /// <param name="rankThisFight">False when this fight must not produce a 이번 전투 상위 %, even though the
    /// artifact has a row for its boss. The trial is the only encounter that needs it: all of its difficulties
    /// share one set of boss mobCodes, so the artifact's mob map cannot tell them apart and would hand a
    /// level-4 run the level-16 distribution. The career tier still shows — that is the character's standing,
    /// not this fight's.</param>
    public static Dictionary<int, RowTier> Evaluate(
        DpsReport report,
        TierArtifact? artifact,
        IReadOnlyDictionary<string, int>? careerTiers = null,
        Func<User, string?>? identityHashOf = null,
        bool rankThisFight = true)
    {
        var result = new Dictionary<int, RowTier>();
        if (artifact == null || report.Target is not MobInfo target)
        {
            return result;
        }

        long durationMs = report.BattleEnd - report.BattleStart;
        if (durationMs < TierLadder.MinBattleDurationMs)
        {
            return result;
        }

        if (artifact.Placement(target.Mob.Code) is not TierMobPlacement placement)
        {
            return result; // fail-closed: an unmapped boss gets no tier at all
        }

        string? dungeonLabel = DungeonLabel(artifact, placement);
        int partySize = report.Contributors.Count;
        int partyMode = partySize is 8 or 10 ? 10 : 5;
        bool trusted = IsSynergyTrusted(report, partySize);

        foreach (User user in report.Contributors)
        {
            if (!report.Information.TryGetValue(user.Id, out DpsInformation? info) || info.Dps <= 0)
            {
                continue;
            }

            string? job = user.Job is JobClass jc ? jc.ClassName() : null;
            int synergyCount = SynergyCountFor(report, user, partySize, trusted);

            TierCohort? cohort = rankThisFight
                ? TierLadder.CohortFor(
                    artifact, target.Mob.Code, job, user.Power, durationMs, synergyCount, partyMode, trusted)
                : null;

            double? battlePercent = cohort is TierCohort c
                ? TierLadder.Evaluate(artifact, c, info.Dps)?.TopPercent
                : null;

            string? hash = identityHashOf?.Invoke(user);
            bool hasCareer = hash != null && careerTiers != null && careerTiers.TryGetValue(hash, out int careerRank);
            int rank = hasCareer
                ? careerTiers![hash!]
                : battlePercent is double bp ? TierLadder.TierRankOf(bp) : 0;

            if (rank <= 0)
            {
                continue; // neither a standing nor a measurable fight — render nothing
            }

            result[user.Id] = new RowTier(rank, battlePercent, dungeonLabel, hasCareer);
        }

        return result;
    }

    /// <summary>"무스펠의 성배 · 어려움", or just the dungeon when the variant label is unknown.</summary>
    private static string? DungeonLabel(TierArtifact artifact, TierMobPlacement placement)
    {
        string? dungeon = artifact.DungeonName(placement.DungeonOrd);
        if (dungeon == null)
        {
            return null;
        }

        string? variant = artifact.VariantLabel(placement.DungeonOrd, placement.VariantOrd);
        return variant == null ? dungeon : $"{dungeon} · {variant}";
    }

    /// <summary>
    /// Can this report's synergy be trusted to describe what each player ACTUALLY received?
    /// <para>For 5-man content the whole party is one synergy group, so yes. For an 8/10-man raid it is only
    /// true when the roster resolved completely — every participant holds a distinct slot covering 1..N. Without
    /// that the mask would describe the whole raid, claiming synergies from the other sub-party that the player
    /// never received, which is exactly why the server excludes those rows from its synergy-bucketed rungs.</para>
    /// </summary>
    private static bool IsSynergyTrusted(DpsReport report, int partySize)
    {
        if (partySize is not (8 or 10))
        {
            return true;
        }

        var slots = new HashSet<int>();
        foreach (User user in report.Contributors)
        {
            if (!report.PartySlots.TryGetValue(user.Id, out int slot) || slot < 1 || slot > partySize)
            {
                return false;
            }

            if (!slots.Add(slot))
            {
                return false; // duplicate slot — the roster is not coherent
            }
        }

        return slots.Count == partySize;
    }

    /// <summary>Distinct synergy classes in the player's own group, capped at 3 (the server caps the same way,
    /// so a 4-synergy party folds into the 3 bucket rather than creating a rare cell). The mask includes the
    /// player's own class — a 치유성 counts their own 축복 like everyone else's.</summary>
    private static int SynergyCountFor(DpsReport report, User user, int partySize, bool trusted)
    {
        int group = SubGroupOf(report, user, partySize, trusted);
        int mask = 0;
        foreach (User other in report.Contributors)
        {
            if (other.Job is not JobClass job)
            {
                continue;
            }

            if (group > 0 && SubGroupOf(report, other, partySize, trusted) != group)
            {
                continue;
            }

            foreach ((JobClass synergyJob, int bit) in SynergyBits)
            {
                if (job == synergyJob)
                {
                    mask |= bit;
                }
            }
        }

        return Math.Min(System.Numerics.BitOperations.PopCount((uint)mask), 3);
    }

    /// <summary>1 or 2 for a trusted raid (slots split in half: 10-man 1~5 / 6~10, 8-man 1~4 / 5~8),
    /// 0 when the whole party is one group.</summary>
    private static int SubGroupOf(DpsReport report, User user, int partySize, bool trusted)
    {
        if (!trusted || partySize is not (8 or 10))
        {
            return 0;
        }

        return report.PartySlots.TryGetValue(user.Id, out int slot) && slot > partySize / 2 ? 2 : 1;
    }
}

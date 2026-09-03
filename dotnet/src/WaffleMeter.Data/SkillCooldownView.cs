namespace WaffleMeter.Data;

/// <summary>One row of the skill-cooldown overlay: a skill the local player has actually used (or that the
/// server has reported a cooldown for) this session, plus where its cooldown stands right now.
/// </summary>
/// <param name="GroupId">The shared-cooldown group — the row's identity. Two skills that share a cooldown are
/// one row.</param>
/// <param name="DisplayCode">The last wire code seen for this group, used to pick the icon.</param>
/// <param name="Name">Display name from the catalog.</param>
/// <param name="RemainingMs">Time left, 0 when ready.</param>
/// <param name="TotalMs">The full cooldown this character actually has (from the cast frame), i.e. the ring's
/// denominator. 0 when only a correction has been seen and the total is still unknown.</param>
/// <param name="IsReady">Whether the skill can be recast now.</param>
/// <param name="Job">Job band (11–19), for grouping.</param>
/// <param name="Order">Stable position inside the job.</param>
public readonly record struct SkillCooldownView(
    int GroupId,
    int DisplayCode,
    string Name,
    long RemainingMs,
    long TotalMs,
    bool IsReady,
    int Job,
    int Order);

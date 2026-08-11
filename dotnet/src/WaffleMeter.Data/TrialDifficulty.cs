using WaffleMeter.Capture;

namespace WaffleMeter.Data;

/// <summary>
/// What is known about the current 시련 run's difficulty. The party sets four knobs (each 1~4) before
/// entering and the game shows their SUM, so the level runs 4~16 — and every level shares one map and one set
/// of boss codes, which is why it has to be read off the wire at all.
/// <para>Three of the four are observable today, so the total is usually a narrow RANGE rather than a number.
/// Reporting a range beats reporting a guess: pooling a 4 with a 16 distorts a percentile badly (the boss has
/// 2.2x the HP), but so would filing a run under the wrong level.</para>
/// </summary>
public readonly record struct TrialDifficulty(int? Timelimit, int? Rebirthlimit, int? BossBuff, int? SkillUpgrade)
{
    private int[] Levels => [Timelimit ?? 0, Rebirthlimit ?? 0, BossBuff ?? 0, SkillUpgrade ?? 0];

    /// <summary>How many of the four knobs are known.</summary>
    public int KnownCount => Levels.Count(l => l > 0);

    /// <summary>True once anything at all has been observed — i.e. this IS a trial run.</summary>
    public bool IsTrial => KnownCount > 0;

    /// <summary>The displayed level, or null while any knob is still unknown.</summary>
    public int? Level => KnownCount == TrialAffixCatalog.GroupCount ? Levels.Sum() : null;

    /// <summary>Lowest displayed level consistent with what is known (unknown knobs assumed 1).</summary>
    public int LevelMin => Levels.Sum(l => l > 0 ? l : 1);

    /// <summary>Highest displayed level consistent with what is known (unknown knobs assumed 4).</summary>
    public int LevelMax => Levels.Sum(l => l > 0 ? l : 4);

    /// <summary>"시련 16단계" when the level is pinned, "시련 13~16단계" while it isn't, "" when this is not
    /// a trial run at all.</summary>
    public string Label =>
        !IsTrial ? string.Empty
        : Level is { } exact ? $"시련 {exact}단계"
        : $"시련 {LevelMin}~{LevelMax}단계";

    /// <summary>
    /// The top difficulty — the only one this fight gets ranked at.
    /// <para>보스 강화 4 raises the boss's max HP 120%, its damage amplification 50%, its combat speed 40%
    /// and its groggy gauge 50%; 바크론 패턴 강화 4 adds 가시 속박, more 덩굴, and 탄환초 소환. Both change
    /// how much damage a run can put out, so a percentile that mixed them with lower settings would not be
    /// measuring anything. 시간 제한 and 부활 제한 leave the boss alone, but at this setting all three
    /// readable knobs are 4 anyway.</para>
    /// </summary>
    public bool IsTopDifficulty =>
        Timelimit == 4 && BossBuff == 4 && SkillUpgrade == 4;
}

/// <summary>
/// Collects the difficulty knobs observed for the current instance. Pure and lock-guarded so the packet
/// thread can write while the UI reads.
/// <para>Reset on entering a new instance: the knobs are chosen per run, so carrying them across would file
/// the next run under the previous one's difficulty.</para>
/// </summary>
public sealed class TrialDifficultyTracker
{
    /// <summary>The trial's map. The affix abnormals only ever appear here, but the phase window arrives for
    /// every dungeon, so that path has to be scoped explicitly.</summary>
    public const int TrialMapId = 600074;

    /// <summary>The instance phase whose window IS the 제한 시간 setting. Phases 3/4 are short transitions
    /// (measured ~10 s) and must not be mistaken for it.</summary>
    private const int MainPhase = 2;

    private readonly object _gate = new();
    private readonly int?[] _levels = new int?[TrialAffixCatalog.GroupCount];
    private long _runStartMs;

    public void Observe(TrialAffixGroup group, int level)
    {
        if (level < 1 || level > 4)
        {
            return;
        }

        lock (_gate)
        {
            _levels[(int)group] = level;
        }
    }

    /// <summary>Feed an instance phase window. Only the trial's main phase says anything about difficulty;
    /// everything else is ignored rather than guessed at.
    /// <para>A main phase with a new start time IS a new run, and the knobs are chosen per run — so this is
    /// also where the previous run's settings are cleared. Without it, re-entering at a different difficulty
    /// would file the second run under the first one's level.</para></summary>
    public void ObservePhaseWindow(int mapId, int phase, long startMs, long windowMs)
    {
        if (mapId <= 0)
        {
            return;
        }

        if (mapId != TrialMapId)
        {
            // A phase window for ANOTHER map is proof the trial is over, and it is the only such proof the
            // meter gets — there is no "you left the instance" packet. Until 2026-08-11 this method returned
            // here without clearing, so the knobs outlived the run: one 시련 at the start of a session relabelled
            // every dungeon after it, and 돌아온 추방자 가르가움 — a 초월 2단계 boss — rendered as
            // "(시련 13~16단계)" for the rest of the evening.
            //
            // Deliberately NOT symmetrical: entering the trial's map must not clear, because this window is not
            // ordered against the affix broadcasts and clearing on arrival would discard settings that got here
            // first. Leaving has no such hazard — everything held is the old run's by definition.
            Reset();
            return;
        }

        if (phase != MainPhase)
        {
            return;
        }

        lock (_gate)
        {
            // Only a CHANGE marks a new run. The first window must not clear anything: it is not ordered
            // against the affix broadcasts, so clearing on it would discard settings that arrived first.
            if (_runStartMs != 0 && startMs != _runStartMs)
            {
                Array.Clear(_levels);
            }

            _runStartMs = startMs;
        }

        int level = TrialAffixCatalog.TimelimitLevelForSeconds(windowMs / 1000);
        if (level > 0)
        {
            Observe(TrialAffixGroup.Timelimit, level);
        }
    }

    public TrialDifficulty Current
    {
        get
        {
            lock (_gate)
            {
                return new TrialDifficulty(
                    Timelimit: _levels[(int)TrialAffixGroup.Timelimit],
                    Rebirthlimit: _levels[(int)TrialAffixGroup.Rebirthlimit],
                    BossBuff: _levels[(int)TrialAffixGroup.BossBuff],
                    SkillUpgrade: _levels[(int)TrialAffixGroup.BakronSkillUpgrade]);
            }
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            Array.Clear(_levels);
            _runStartMs = 0;
        }
    }
}

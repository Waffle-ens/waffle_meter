using WaffleMeter.Capture;
using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>
/// Spec for the one encounter whose mobCode does not imply its difficulty.
/// <para>Every other boss code belongs to exactly one difficulty, so the tier artifact's mob map places a
/// fight correctly on its own. 시련: 바크론의 공중섬 shares one set of codes across all of its settings, so
/// the map would hand a level-4 run the level-16 distribution — and at the top setting the boss carries 2.2x
/// the max HP. Only the top difficulty gets ranked.</para>
/// </summary>
public sealed class TrialTopDifficultyTests
{
    private static EncounterCatalog Shipped()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "Assets", "json", "encounters.json");
            if (File.Exists(candidate))
            {
                return EncounterCatalog.Load(candidate);
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Assets/json/encounters.json not found above " + AppContext.BaseDirectory);
    }

    /// <summary>The trial's three boss codes, and only those, leave the difficulty undetermined.</summary>
    [Theory]
    [InlineData(2300580, true)]   // 티에 (시련)
    [InlineData(2300581, true)]   // 타몬 (시련)
    [InlineData(2300582, true)]   // 바크론 (시련)
    [InlineData(2300812, false)]  // 바크론 (보통) — its own code, its own difficulty
    [InlineData(2310812, false)]  // 바크론 (탐험)
    [InlineData(2320812, false)]  // 바크론 (어려움)
    [InlineData(2301723, false)]  // 타락한 데바의 성 (어려움)
    [InlineData(2600068, false)]  // 정령왕 아그로 — a field boss, not catalogued at all
    public void Only_the_trial_leaves_its_difficulty_undetermined(int mobCode, bool isTrial)
    {
        Assert.Equal(isTrial, Shipped().IsTrialEncounter(mobCode));
    }

    [Fact]
    public void Top_difficulty_needs_every_readable_knob_at_four()
    {
        var tracker = new TrialDifficultyTracker();
        tracker.Observe(TrialAffixGroup.BossBuff, 4);
        tracker.Observe(TrialAffixGroup.BakronSkillUpgrade, 4);
        Assert.False(tracker.Current.IsTopDifficulty);   // 시간 제한 still unknown

        tracker.Observe(TrialAffixGroup.Timelimit, 4);
        Assert.True(tracker.Current.IsTopDifficulty);
    }

    /// <summary>부활 제한 has no known carrier, so it stays null — and must not be required, or the top
    /// difficulty would never be recognised at all.</summary>
    [Fact]
    public void The_unreadable_knob_is_not_required()
    {
        var tracker = new TrialDifficultyTracker();
        tracker.Observe(TrialAffixGroup.Timelimit, 4);
        tracker.Observe(TrialAffixGroup.BossBuff, 4);
        tracker.Observe(TrialAffixGroup.BakronSkillUpgrade, 4);

        Assert.Null(tracker.Current.Rebirthlimit);
        Assert.True(tracker.Current.IsTopDifficulty);
        Assert.Equal("시련 13~16단계", tracker.Current.Label);
    }

    [Theory]
    [InlineData(4, 4, 3)]   // 패턴 강화가 낮다 — 기믹이 달라 딜 로스가 다르다
    [InlineData(4, 1, 4)]   // 보스 강화가 낮다 — 최대 체력이 1.0배
    [InlineData(1, 4, 4)]   // 시간 제한이 낮다
    public void Anything_below_the_top_is_not_ranked(int timelimit, int bossBuff, int skillUpgrade)
    {
        var tracker = new TrialDifficultyTracker();
        tracker.Observe(TrialAffixGroup.Timelimit, timelimit);
        tracker.Observe(TrialAffixGroup.BossBuff, bossBuff);
        tracker.Observe(TrialAffixGroup.BakronSkillUpgrade, skillUpgrade);

        Assert.False(tracker.Current.IsTopDifficulty);
    }

    /// <summary>A run whose affixes were never observed must not be ranked either — "unknown" is not "top".</summary>
    [Fact]
    public void An_unobserved_run_is_not_top_difficulty()
    {
        Assert.False(new TrialDifficultyTracker().Current.IsTopDifficulty);
    }

    /// <summary>Today the top difficulty is exactly the 13~16 label, so the two agree. They stop agreeing once
    /// 부활 제한 becomes readable — level 13~16 will then admit combinations with a lower 보스 강화 — which is
    /// why the gate is written on the knobs and not on the level.</summary>
    [Fact]
    public void Top_difficulty_coincides_with_the_thirteen_to_sixteen_label_today()
    {
        for (int t = 1; t <= 4; t++)
        {
            for (int b = 1; b <= 4; b++)
            {
                for (int s = 1; s <= 4; s++)
                {
                    var tracker = new TrialDifficultyTracker();
                    tracker.Observe(TrialAffixGroup.Timelimit, t);
                    tracker.Observe(TrialAffixGroup.BossBuff, b);
                    tracker.Observe(TrialAffixGroup.BakronSkillUpgrade, s);

                    TrialDifficulty d = tracker.Current;
                    Assert.Equal(d.LevelMin == 13, d.IsTopDifficulty);
                }
            }
        }
    }
}

using WaffleMeter.Capture;
using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>
/// Spec for reading 시련: 바크론의 공중섬's difficulty off the wire. Every level 4~16 shares one map and one
/// set of boss mobCodes, so this is the only thing separating a level-4 run from a level-16 one — and pooling
/// them distorts a percentile badly (the boss carries 2.2x the HP at the top setting).
/// </summary>
public sealed class TrialDifficultyTests
{
    [Fact]
    public void An_affix_code_names_its_group_and_level_one_to_one()
    {
        Assert.True(TrialAffixCatalog.TryResolve(19993401, out TrialAffix a));
        Assert.Equal(new TrialAffix(TrialAffixGroup.BossBuff, 1), a);

        Assert.True(TrialAffixCatalog.TryResolve(19993701, out TrialAffix b));
        Assert.Equal(new TrialAffix(TrialAffixGroup.BossBuff, 4), b);

        Assert.True(TrialAffixCatalog.TryResolve(19806331, out TrialAffix c));
        Assert.Equal(new TrialAffix(TrialAffixGroup.BakronSkillUpgrade, 4), c);

        // The visible second-stage codes resolve to the same setting as their hidden casters.
        Assert.True(TrialAffixCatalog.TryResolve(19993711, out TrialAffix d));
        Assert.Equal(new TrialAffix(TrialAffixGroup.BossBuff, 4), d);
    }

    [Fact]
    public void An_ordinary_job_buff_is_not_an_affix()
    {
        Assert.False(TrialAffixCatalog.IsAffixCode(19130000)); // 폭주
        Assert.False(TrialAffixCatalog.IsAffixCode(11020001));
        Assert.False(TrialAffixCatalog.TryResolve(19993402, out _)); // neighbouring, unallocated
    }

    [Fact]
    public void Timelimit_seconds_map_to_their_level()
    {
        Assert.Equal(1, TrialAffixCatalog.TimelimitLevelForSeconds(1800));
        Assert.Equal(2, TrialAffixCatalog.TimelimitLevelForSeconds(1200));
        Assert.Equal(3, TrialAffixCatalog.TimelimitLevelForSeconds(900));
        Assert.Equal(4, TrialAffixCatalog.TimelimitLevelForSeconds(600));
        Assert.Equal(0, TrialAffixCatalog.TimelimitLevelForSeconds(7200)); // another dungeon's budget
        Assert.Equal(0, TrialAffixCatalog.TimelimitLevelForSeconds(10));   // a phase-3/4 transition window
    }

    /// <summary>Until every knob is known the total is a range, not a number. Saying "13~16단계" is useful;
    /// saying "13단계" because the last knob happened to default low is not.</summary>
    [Fact]
    public void The_total_is_a_range_while_a_knob_is_unknown()
    {
        var tracker = new TrialDifficultyTracker();
        tracker.Observe(TrialAffixGroup.BossBuff, 4);
        tracker.Observe(TrialAffixGroup.BakronSkillUpgrade, 4);
        tracker.ObservePhaseWindow(TrialDifficultyTracker.TrialMapId, 2, startMs: 1000, windowMs: 600_000);

        TrialDifficulty d = tracker.Current;
        Assert.True(d.IsTrial);
        Assert.Equal(3, d.KnownCount);
        Assert.Null(d.Level);          // 부활 제한 has no known carrier
        Assert.Equal(13, d.LevelMin);  // 4+4+4 + 1
        Assert.Equal(16, d.LevelMax);  // 4+4+4 + 4
        Assert.Equal("시련 13~16단계", d.Label);
    }

    [Fact]
    public void The_total_is_exact_once_every_knob_is_known()
    {
        var tracker = new TrialDifficultyTracker();
        tracker.Observe(TrialAffixGroup.BossBuff, 4);
        tracker.Observe(TrialAffixGroup.BakronSkillUpgrade, 4);
        tracker.Observe(TrialAffixGroup.Timelimit, 4);
        tracker.Observe(TrialAffixGroup.Rebirthlimit, 4);

        TrialDifficulty d = tracker.Current;
        Assert.Equal(16, d.Level);
        Assert.Equal(16, d.LevelMin);
        Assert.Equal(16, d.LevelMax);
        Assert.Equal("시련 16단계", d.Label);
    }

    [Fact]
    public void Nothing_observed_means_this_is_not_a_trial_run()
    {
        TrialDifficulty d = new TrialDifficultyTracker().Current;

        Assert.False(d.IsTrial);
        Assert.Equal(string.Empty, d.Label);
    }

    /// <summary>The knobs are chosen per run. Re-entering at a different difficulty must not inherit the
    /// previous run's settings — a new main-phase window is what marks the boundary.</summary>
    [Fact]
    public void A_new_run_clears_the_previous_runs_settings()
    {
        var tracker = new TrialDifficultyTracker();
        tracker.ObservePhaseWindow(TrialDifficultyTracker.TrialMapId, 2, startMs: 1000, windowMs: 600_000);
        tracker.Observe(TrialAffixGroup.BossBuff, 4);
        Assert.Equal(4, tracker.Current.BossBuff);

        // Second entry, easier settings.
        tracker.ObservePhaseWindow(TrialDifficultyTracker.TrialMapId, 2, startMs: 999_000, windowMs: 1_800_000);

        Assert.Null(tracker.Current.BossBuff);          // the old run's boss setting is gone
        Assert.Equal(1, tracker.Current.Timelimit);     // the new run's is in
    }

    /// <summary>The phase window is not ordered against the affix broadcasts, so the FIRST window must not
    /// clear settings that happened to arrive before it.</summary>
    [Fact]
    public void The_first_window_keeps_affixes_that_arrived_before_it()
    {
        var tracker = new TrialDifficultyTracker();
        tracker.Observe(TrialAffixGroup.BossBuff, 4);
        tracker.ObservePhaseWindow(TrialDifficultyTracker.TrialMapId, 2, startMs: 1000, windowMs: 600_000);

        Assert.Equal(4, tracker.Current.BossBuff);
        Assert.Equal(4, tracker.Current.Timelimit);
    }

    [Fact]
    public void The_same_window_repeating_is_not_a_new_run()
    {
        var tracker = new TrialDifficultyTracker();
        tracker.ObservePhaseWindow(TrialDifficultyTracker.TrialMapId, 2, startMs: 1000, windowMs: 600_000);
        tracker.Observe(TrialAffixGroup.BossBuff, 3);
        tracker.ObservePhaseWindow(TrialDifficultyTracker.TrialMapId, 2, startMs: 1000, windowMs: 600_000);

        Assert.Equal(3, tracker.Current.BossBuff);
    }

    /// <summary>Only the trial's MAIN phase is its time budget. Phases 3/4 are ~10 s transitions, and every
    /// other dungeon sends windows too — treating either as a difficulty would invent a level.</summary>
    [Theory]
    [InlineData(600074, 3, 600_000)]   // trial, but a transition phase
    [InlineData(620021, 2, 600_000)]   // another dungeon's main phase
    public void Only_the_trials_main_phase_sets_the_time_limit(int mapId, int phase, long windowMs)
    {
        var tracker = new TrialDifficultyTracker();
        tracker.ObservePhaseWindow(mapId, phase, startMs: 1000, windowMs: windowMs);

        Assert.False(tracker.Current.IsTrial);
    }

    /// <summary>A phase window for another map is the only proof the meter gets that the trial is over — there
    /// is no "you left the instance" packet. Without this the knobs outlived the run and one 시련 at the start
    /// of a session relabelled every dungeon after it (돌아온 추방자 가르가움, a 초월 2단계 boss, rendered as
    /// "(시련 13~16단계)" all evening).</summary>
    [Fact]
    public void Entering_another_dungeon_ends_the_trial_run()
    {
        var tracker = new TrialDifficultyTracker();
        tracker.ObservePhaseWindow(TrialDifficultyTracker.TrialMapId, 2, startMs: 1000, windowMs: 600_000);
        tracker.Observe(TrialAffixGroup.BossBuff, 4);
        tracker.Observe(TrialAffixGroup.BakronSkillUpgrade, 4);
        Assert.True(tracker.Current.IsTrial);

        // 심연의 뿔 암굴 2단계 — any phase of it, including a transition.
        tracker.ObservePhaseWindow(610031, 2, startMs: 900_000, windowMs: 1_800_000);

        Assert.False(tracker.Current.IsTrial);
        Assert.Equal(string.Empty, tracker.Current.Label);
    }

    [Fact]
    public void A_transition_phase_of_another_dungeon_also_ends_the_run()
    {
        var tracker = new TrialDifficultyTracker();
        tracker.Observe(TrialAffixGroup.BossBuff, 4);
        tracker.ObservePhaseWindow(610031, 3, startMs: 900_000, windowMs: 10_000);

        Assert.False(tracker.Current.IsTrial);
    }

    /// <summary>Re-entering the trial must not clear on arrival: this window is not ordered against the affix
    /// broadcasts, so clearing here would discard settings that got in first. Only LEAVING clears.</summary>
    [Fact]
    public void Coming_back_to_the_trial_keeps_affixes_that_arrived_first()
    {
        var tracker = new TrialDifficultyTracker();
        tracker.ObservePhaseWindow(610031, 2, startMs: 1000, windowMs: 1_800_000); // elsewhere first
        tracker.Observe(TrialAffixGroup.BossBuff, 4);                              // affix beats the window
        tracker.ObservePhaseWindow(TrialDifficultyTracker.TrialMapId, 2, startMs: 900_000, windowMs: 600_000);

        Assert.Equal(4, tracker.Current.BossBuff);
        Assert.Equal(4, tracker.Current.Timelimit);
    }

    [Fact]
    public void A_zero_map_id_is_ignored_rather_than_treated_as_leaving()
    {
        var tracker = new TrialDifficultyTracker();
        tracker.Observe(TrialAffixGroup.BossBuff, 4);
        tracker.ObservePhaseWindow(0, 2, startMs: 1000, windowMs: 600_000);

        Assert.Equal(4, tracker.Current.BossBuff);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(-1)]
    public void An_out_of_range_level_is_ignored(int level)
    {
        var tracker = new TrialDifficultyTracker();
        tracker.Observe(TrialAffixGroup.BossBuff, level);

        Assert.False(tracker.Current.IsTrial);
    }

    [Fact]
    public void DataManager_routes_the_parser_callbacks_into_the_tracker()
    {
        var dm = new DataManager();
        dm.SaveTrialAffix(TrialAffixGroup.BossBuff, 4, arrivedAt: 0);
        dm.SaveInstancePhaseWindow(TrialDifficultyTracker.TrialMapId, 2, startMs: 1000, windowMs: 900_000);

        TrialDifficulty d = dm.TrialDifficulty.Current;
        Assert.Equal(4, d.BossBuff);
        Assert.Equal(3, d.Timelimit);
    }
}

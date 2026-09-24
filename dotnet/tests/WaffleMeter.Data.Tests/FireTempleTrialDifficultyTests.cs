using WaffleMeter.Capture;
using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>
/// Spec for the SECOND 시련, 불의 신전 (client 112, 2026-09-23). It shows the same 4~16 as 바크론 but splits
/// the range differently — 3/3/8/2 instead of 4/4/4/4 — so every place that hard-coded "4" had to learn that
/// a ceiling is per-dungeon. These tests pin the new dungeon's behaviour AND that 바크론's did not move.
/// </summary>
public sealed class FireTempleTrialDifficultyTests
{
    private const int FireTemple = TrialDungeonAxes.FireTempleMapId;   // 600025
    private const int Bakron = TrialDungeonAxes.BakronIslandMapId;     // 600074

    private static int[] Knobs(int timelimit, int rebirth, int bossBuff, int fourth) =>
        [timelimit, rebirth, bossBuff, fourth];

    [Fact]
    public void Fire_temple_is_map_600025_not_600024()
    {
        // 탐험/보통/어려움 are 600021/22/23, so 600024 is the tempting guess — Map.dat says otherwise.
        Assert.Equal(600025, TrialDungeonAxes.FireTempleMapId);
        Assert.True(TrialDungeonAxes.IsTrialMap(FireTemple));
        Assert.True(TrialDungeonAxes.IsTrialMap(Bakron));
        Assert.False(TrialDungeonAxes.IsTrialMap(600024));
        Assert.False(TrialDungeonAxes.IsTrialMap(600022));
    }

    [Fact]
    public void Each_dungeon_keeps_its_own_ceilings()
    {
        Assert.Equal([4, 4, 4, 4], TrialDungeonAxes.For(Bakron));
        Assert.Equal([3, 3, 8, 2], TrialDungeonAxes.For(FireTemple));
    }

    /// <summary>
    /// The regression this whole change exists for: the room snapshot used to be clamped to <c>&lt;= 4</c>,
    /// so 불의 신전's 보스 강화 5~8 was dropped on the floor. That is precisely the top of the range, i.e.
    /// the setting the owner asked to report — it would have gone missing while 4 and below sailed through.
    /// </summary>
    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void Boss_buff_above_four_survives_in_fire_temple(int bossBuff)
    {
        TrialDifficultyTracker t = new();
        t.ObserveRoomAffixes(FireTemple, roomKey: 4242, Knobs(3, 3, bossBuff, 2));

        Assert.Equal(bossBuff, t.Current.BossBuff);
    }

    [Fact]
    public void Sixteen_in_fire_temple_is_three_three_eight_two()
    {
        TrialDifficultyTracker t = new();
        t.ObserveRoomAffixes(FireTemple, roomKey: 1, Knobs(3, 3, 8, 2));

        TrialDifficulty d = t.Current;
        Assert.Equal(16, d.Level);
        Assert.Equal("시련 16단계", d.Label);
        Assert.True(d.IsTopDifficulty);
        Assert.True(d.IsTop16Difficulty);
    }

    /// <summary>부활 제한 tops out at 3 here. Measured against 바크론's 4 it would never be "top" — which is
    /// exactly how the owner's "16단계만" filter would have silently excluded every 불의 신전 run.</summary>
    [Fact]
    public void Rebirthlimit_three_is_top_in_fire_temple_but_not_in_bakron()
    {
        Assert.True(new TrialDifficulty(3, 3, 8, 2, FireTemple).IsTop16Difficulty);
        Assert.False(new TrialDifficulty(4, 3, 4, 4, Bakron).IsTop16Difficulty);
    }

    [Fact]
    public void One_short_of_the_ceiling_is_not_top()
    {
        Assert.False(new TrialDifficulty(3, 3, 7, 2, FireTemple).IsTop16Difficulty);
        Assert.False(new TrialDifficulty(2, 3, 8, 2, FireTemple).IsTop16Difficulty);
        Assert.False(new TrialDifficulty(3, 3, 8, 1, FireTemple).IsTop16Difficulty);
    }

    /// <summary>An unread axis widens the range by that axis's own headroom, so the same "one knob missing"
    /// is far coarser here (보스 강화 alone spans 7) than in 바크론 (3).</summary>
    [Fact]
    public void Unknown_axes_are_bounded_by_their_own_ceiling()
    {
        TrialDifficulty fire = new(3, 3, null, 2, FireTemple);
        Assert.Null(fire.Level);
        Assert.Equal(9, fire.LevelMin);    // 3+3+1+2
        Assert.Equal(16, fire.LevelMax);   // 3+3+8+2

        TrialDifficulty bakron = new(4, 4, null, 4, Bakron);
        Assert.Equal(13, bakron.LevelMin);
        Assert.Equal(16, bakron.LevelMax);
    }

    /// <summary>The web's schema rejects the WHOLE report when levelMax &gt; 16. Widening an unread 불의 신전 axis
    /// by 바크론's +3 would turn a 16단계 run with 부활 제한 unread into 14~17 — a permanent loss, not a
    /// coarse label.</summary>
    [Theory]
    [InlineData(0, 14)]   // 제한 시간 unread: 1+3+8+2
    [InlineData(1, 14)]   // 부활 제한 unread
    [InlineData(2, 9)]    // 보스 강화 unread: 3+3+1+2
    [InlineData(3, 15)]   // Pc_debuff_1 unread
    public void A_fire_temple_sixteen_with_one_axis_unread_never_ranges_past_sixteen(int unread, int min)
    {
        int?[] k = [3, 3, 8, 2];
        k[unread] = null;
        TrialDifficulty fire = new(k[0], k[1], k[2], k[3], FireTemple);

        Assert.Equal(min, fire.LevelMin);
        Assert.Equal(16, fire.LevelMax);
    }

    [Fact]
    public void Bakron_rejects_a_boss_buff_that_only_fire_temple_allows()
    {
        TrialDifficultyTracker t = new();
        t.ObserveRoomAffixes(Bakron, roomKey: 7, Knobs(4, 4, 8, 4));

        // 8 is out of range for 바크론 — the axis stays unread rather than recording a level that
        // dungeon cannot produce.
        Assert.Null(t.Current.BossBuff);
        Assert.Equal(4, t.Current.Timelimit);
    }

    [Fact]
    public void Switching_dungeon_drops_the_previous_runs_knobs()
    {
        TrialDifficultyTracker t = new();
        t.ObserveRoomAffixes(Bakron, roomKey: 100, Knobs(4, 4, 4, 4));
        Assert.Equal(16, t.Current.Level);

        // Same roomKey, different dungeon: the held knobs were measured on another ruler.
        t.ObserveRoomAffixes(FireTemple, roomKey: 100, Knobs(3, 3, 8, 2));

        TrialDifficulty d = t.Current;
        Assert.Equal(FireTemple, d.MapId);
        Assert.Equal(16, d.Level);
        Assert.True(d.IsTop16Difficulty);
    }

    [Fact]
    public void A_non_trial_map_still_clears_everything()
    {
        TrialDifficultyTracker t = new();
        t.ObserveRoomAffixes(FireTemple, roomKey: 5, Knobs(3, 3, 8, 2));
        Assert.True(t.Current.IsTrial);

        t.ObserveRoomAffixes(600022, roomKey: 6, Knobs(1, 1, 1, 1));   // 불의 신전 보통 — not a trial
        Assert.False(t.Current.IsTrial);
    }

    /// <summary>The seconds→level table was measured on 바크론 and cannot describe an axis that stops at 3.
    /// Leaving it unread keeps the room snapshot as the single source for this dungeon.</summary>
    [Fact]
    public void Phase_window_does_not_guess_fire_temples_timelimit()
    {
        TrialDifficultyTracker t = new();
        t.ObservePhaseWindow(FireTemple, phase: 2, startMs: 1_000, windowMs: 600_000);

        Assert.Null(t.Current.Timelimit);

        TrialDifficultyTracker b = new();
        b.ObservePhaseWindow(Bakron, phase: 2, startMs: 1_000, windowMs: 600_000);
        Assert.Equal(4, b.Current.Timelimit);
    }
}

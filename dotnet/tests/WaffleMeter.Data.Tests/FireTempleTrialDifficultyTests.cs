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

    /// <summary>클라 125(2026-11-11): 불의 신전 보스 강화가 8→16, 합이 4~24 가 됐다. 16단계 게이트의 축
    /// (<see cref="TrialDungeonAxes.For"/>)은 그대로 두고 현재 상한만 따로 올렸다 — 바크론은 둘이 같다.</summary>
    [Fact]
    public void Fire_temples_current_ceiling_grew_to_sixteen_while_the_sixteen_gate_stayed()
    {
        Assert.Equal([3, 3, 16, 2], TrialDungeonAxes.CeilingFor(FireTemple));
        Assert.Equal([3, 3, 8, 2], TrialDungeonAxes.For(FireTemple));
        Assert.Equal(TrialDungeonAxes.For(Bakron), TrialDungeonAxes.CeilingFor(Bakron));
        Assert.Equal(16, TrialDungeonAxes.Max(FireTemple, (int)TrialAffixGroup.BossBuff));
        Assert.Equal(4, TrialDungeonAxes.Max(Bakron, (int)TrialAffixGroup.BossBuff));
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
    [InlineData(9)]     // 클라 125 신설 구간 — 상한이 8에 머물면 17단계 이상만 통째로 빠진다
    [InlineData(12)]
    [InlineData(16)]
    public void Boss_buff_above_four_survives_in_fire_temple(int bossBuff)
    {
        TrialDifficultyTracker t = new();
        t.ObserveRoomAffixes(FireTemple, roomKey: 4242, Knobs(3, 3, bossBuff, 2));

        Assert.Equal(bossBuff, t.Current.BossBuff);
    }

    [Fact]
    public void Boss_buff_past_sixteen_is_still_out_of_range()
    {
        TrialDifficultyTracker t = new();
        t.ObserveRoomAffixes(FireTemple, roomKey: 4242, Knobs(3, 3, 17, 2));

        Assert.Null(t.Current.BossBuff);
        Assert.Equal(3, t.Current.Timelimit);
    }

    [Fact]
    public void Twenty_four_in_fire_temple_is_three_three_sixteen_two()
    {
        TrialDifficultyTracker t = new();
        t.ObserveRoomAffixes(FireTemple, roomKey: 1, Knobs(3, 3, 16, 2));

        TrialDifficulty d = t.Current;
        Assert.Equal(24, d.Level);
        Assert.Equal("시련 24단계", d.Label);
        // 16단계 판정은 3/3/8/2 그대로다. 24단계가 "최고 16단계"로 섞여 들어가면 안 된다.
        Assert.False(d.IsTopDifficulty);
        Assert.False(d.IsTop16Difficulty);
    }

    /// <summary>합이 16이어도 3/3/8/2 가 아니면 16단계 게이트에 들지 않는다 — 클라 125 부터는 보스 강화를 더
    /// 올리고 다른 축을 내려 같은 합을 만들 수 있다(예: 1/3/10/2).</summary>
    [Fact]
    public void A_sum_of_sixteen_reached_another_way_is_not_the_sixteen_gate()
    {
        TrialDifficulty d = new(1, 3, 10, 2, FireTemple);

        Assert.Equal(16, d.Level);
        Assert.False(d.IsTop16Difficulty);
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
    /// is far coarser here (보스 강화 alone spans 15 since client 125) than in 바크론 (3).</summary>
    [Fact]
    public void Unknown_axes_are_bounded_by_their_own_ceiling()
    {
        TrialDifficulty fire = new(3, 3, null, 2, FireTemple);
        Assert.Null(fire.Level);
        Assert.Equal(9, fire.LevelMin);    // 3+3+1+2
        Assert.Equal(24, fire.LevelMax);   // 3+3+16+2 — 클라 125 부터 보스 강화 상한이 16

        TrialDifficulty bakron = new(4, 4, null, 4, Bakron);
        Assert.Equal(13, bakron.LevelMin);
        Assert.Equal(16, bakron.LevelMax);
    }

    /// <summary>The web's schema rejects the WHOLE report when levelMax is past the dungeon's top. Widening an
    /// unread 불의 신전 axis by 바크론's +3 would turn a 16단계 run with 부활 제한 unread into 14~17 — a
    /// permanent loss, not a coarse label.
    /// <para>보스 강화 is the one axis whose headroom moved in client 125: unread, it now leaves the run open
    /// up to 24 (3+3+16+2). The web raises its bound to 24 for the same patch — this meter must not ship
    /// ahead of that.</para></summary>
    [Theory]
    [InlineData(0, 14, 16)]   // 제한 시간 unread: 1+3+8+2
    [InlineData(1, 14, 16)]   // 부활 제한 unread
    [InlineData(2, 9, 24)]    // 보스 강화 unread: 3+3+1+2 … 3+3+16+2
    [InlineData(3, 15, 16)]   // Pc_debuff_1 unread
    public void A_fire_temple_sixteen_with_one_axis_unread_ranges_only_by_that_axis(int unread, int min, int max)
    {
        int?[] k = [3, 3, 8, 2];
        k[unread] = null;
        TrialDifficulty fire = new(k[0], k[1], k[2], k[3], FireTemple);

        Assert.Equal(min, fire.LevelMin);
        Assert.Equal(max, fire.LevelMax);
    }

    [Fact]
    public void No_fire_temple_range_ever_passes_twenty_four()
    {
        Assert.Equal(24, new TrialDifficulty(null, null, null, 2, FireTemple).LevelMax);
        Assert.Equal(24, new TrialDifficulty(3, 3, 16, null, FireTemple).LevelMax);
        Assert.Equal(16, new TrialDifficulty(null, null, null, 4, Bakron).LevelMax);
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

using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>
/// 1인 모드 판정의 수명. 입장 버프는 무기한이고 해제 패킷이 없어서, "언제 끝났나"는 인스턴스 phase 창이
/// 새 입장을 알릴 때만 알 수 있다. 틀릴 땐 끄는 쪽으로 — 파티 전투를 1인 모드로 찍으면 등급이 사라진다.
/// </summary>
public sealed class SoloModeTrackerTests
{
    private const int Uruguru = 600192;  // 광신의 우루구구 보통 — 1인 모드 대상
    private const int Krao = 600002;     // 1인 모드 대상 아님
    private const long T0 = 1_764_000_000_000;

    [Fact]
    public void Nothing_seen_applies_to_nothing() =>
        Assert.False(new SoloModeTracker().AppliesTo(Uruguru));

    [Fact]
    public void A_buff_before_the_phase_window_belongs_to_that_entry()
    {
        var t = new SoloModeTracker();
        t.ObserveBuff(T0);
        t.ObservePhaseWindow(Uruguru, 1, T0 + 2_000, T0 + 2_000);

        Assert.True(t.AppliesTo(Uruguru));
    }

    [Fact]
    public void A_buff_after_the_phase_window_also_counts()
    {
        var t = new SoloModeTracker();
        t.ObservePhaseWindow(Uruguru, 1, T0, T0);
        t.ObserveBuff(T0 + 500);

        Assert.True(t.AppliesTo(Uruguru));
    }

    /// <summary>런 진행(phase 1→2→3)은 새 입장이 아니다 — 보스는 입장 몇 분 뒤에 잡는다.</summary>
    [Fact]
    public void Phase_progress_inside_the_run_keeps_the_flag()
    {
        var t = new SoloModeTracker();
        t.ObservePhaseWindow(Uruguru, 1, T0, T0);
        t.ObserveBuff(T0 + 300);
        t.ObservePhaseWindow(Uruguru, 2, T0 + 30_000, T0 + 30_000);
        t.ObservePhaseWindow(Uruguru, 3, T0 + 600_000, T0 + 600_000);

        Assert.True(t.AppliesTo(Uruguru));
    }

    /// <summary>1인 모드를 마치고 마을로 나와 같은 던전에 파티로 다시 들어가면 맵이 같다. phase 가 처음으로
    /// 돌아갔는데 그 근처에 버프가 없으면 파티 판이다.</summary>
    [Fact]
    public void Re_entering_the_same_dungeon_without_the_buff_clears_it()
    {
        var t = new SoloModeTracker();
        t.ObservePhaseWindow(Uruguru, 1, T0, T0);
        t.ObserveBuff(T0 + 300);
        t.ObservePhaseWindow(Uruguru, 2, T0 + 30_000, T0 + 30_000);

        t.ObservePhaseWindow(Uruguru, 1, T0 + 1_800_000, T0 + 1_800_000);

        Assert.False(t.AppliesTo(Uruguru));
    }

    /// <summary>1인 모드로 다시 들어가면 버프가 또 걸려 다시 켜진다.</summary>
    [Fact]
    public void Re_entering_in_solo_mode_turns_it_back_on()
    {
        var t = new SoloModeTracker();
        t.ObservePhaseWindow(Uruguru, 1, T0, T0);
        t.ObserveBuff(T0 + 300);
        t.ObservePhaseWindow(Uruguru, 1, T0 + 1_800_000, T0 + 1_800_000);
        t.ObserveBuff(T0 + 1_800_400);

        Assert.True(t.AppliesTo(Uruguru));
    }

    [Fact]
    public void Moving_to_another_dungeon_clears_it()
    {
        var t = new SoloModeTracker();
        t.ObservePhaseWindow(Uruguru, 1, T0, T0);
        t.ObserveBuff(T0 + 300);
        t.ObservePhaseWindow(600022, 1, T0 + 900_000, T0 + 900_000);

        Assert.False(t.AppliesTo(Uruguru));
        Assert.False(t.AppliesTo(600022));
    }

    /// <summary>필드·마을엔 phase 창이 없어 추적기가 안 지워진다. 그래서 전투에 붙일 땐 던전을 대조한다 —
    /// 1인 모드 대상이 아닌 던전, 그리고 지금 인스턴스가 아닌 던전에는 붙지 않는다.</summary>
    [Fact]
    public void It_applies_only_to_the_current_eligible_dungeon()
    {
        var t = new SoloModeTracker();
        t.ObservePhaseWindow(Uruguru, 1, T0, T0);
        t.ObserveBuff(T0 + 300);

        Assert.True(t.AppliesTo(Uruguru));
        Assert.False(t.AppliesTo(Krao));
        Assert.False(t.AppliesTo(600022)); // 대상이지만 지금 인스턴스가 아니다
    }

    [Fact]
    public void Reset_clears_everything()
    {
        var t = new SoloModeTracker();
        t.ObserveBuff(T0);
        t.Reset();

        Assert.False(t.Active);
        Assert.False(t.AppliesTo(Uruguru));
    }

    [Fact]
    public void All_fifteen_eligible_dungeons_are_listed() =>
        Assert.Equal(15, SoloModeTracker.EligibleDungeonIds.Count);
}

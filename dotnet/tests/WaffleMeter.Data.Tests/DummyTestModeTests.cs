using WaffleMeter.Capture;
using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>
/// 허수아비 (training-dummy) test mode. A dummy hit is metered as a live battle ONLY while the mode is on; the
/// run is hard-cut at the chosen duration (later hits ignored, result frozen); a dummy run never lands in saved
/// battle history; and the dummy DPS reset clears just the live report while re-arming the cut. Also locks in
/// that <see cref="ReferenceJson.LoadMobs"/> reads the "isDummy" flag — without which the whole gate is dead
/// (every runtime Mob.IsDummy would be false).
/// </summary>
public sealed class DummyTestModeTests
{
    private const int DummyInstance = 200;
    private const int DummyCode = 2300229; // a shipped 훈련용 허수아비 code
    private const int BossInstance = 100;
    private const int BossCode = 2301008;
    private const int Dealer = 5001;

    private static (DataManager Dm, DpsCalculator Calc, long[] Clock) Setup()
    {
        long[] now = { 1_000_000 };
        var dm = new DataManager { Clock = () => now[0] };
        dm.LoadMobs(new Dictionary<int, Mob>
        {
            [DummyCode] = new Mob(DummyCode, "훈련용 허수아비", Boss: false, IsDummy: true),
            [BossCode] = new Mob(BossCode, "보스", Boss: true),
        });
        dm.SaveMobId(DummyInstance, DummyCode);
        dm.SaveMobId(BossInstance, BossCode);
        dm.SaveNickname(Dealer, "딜러", isExecutor: true, server: 2003, jobByte: 32);
        return (dm, new DpsCalculator(dm), now);
    }

    private static void HitDummy(DataManager dm, long timestamp, int damage) =>
        dm.SaveDamage(
            new ParsedDamagePacket { ActorId = Dealer, TargetId = DummyInstance, Damage = damage, Timestamp = timestamp },
            dm.CurrentEpoch());

    [Fact]
    public void Mode_off_a_dummy_hit_registers_no_combat()
    {
        (DataManager dm, DpsCalculator calc, long[] clock) = Setup();
        dm.DummyTestMode = false;

        HitDummy(dm, clock[0] + 100, 5000);
        DpsReport report = calc.GetDps();

        Assert.True(dm.CurrentTarget() <= 0); // no live target (never started)
        Assert.Empty(report.Information);      // no DPS rows
    }

    [Fact]
    public void Mode_on_a_dummy_hit_is_metered_as_a_live_battle()
    {
        (DataManager dm, DpsCalculator calc, long[] clock) = Setup();
        dm.DummyTestMode = true;

        HitDummy(dm, clock[0] + 100, 5000);
        HitDummy(dm, clock[0] + 1_100, 5000);
        DpsReport report = calc.GetDps();

        Assert.Equal(DummyInstance, dm.CurrentTarget());
        Assert.Equal(10_000.0, report.Information[Dealer].Amount, 3);
    }

    [Fact]
    public void Duration_hard_cut_on_hit_stops_counting_and_ignores_later_hits()
    {
        (DataManager dm, DpsCalculator calc, long[] clock) = Setup();
        dm.DummyTestMode = true;
        dm.DummyDurationSec = 30;

        HitDummy(dm, clock[0], 4000); // opens the window at t=start
        calc.GetDps();
        clock[0] += 31_000;           // past the 30s cut
        HitDummy(dm, clock[0], 999_999); // this hit is past the cut → dropped, and it ends the run
        DpsReport afterCut = calc.GetDps();

        Assert.Equal(-1, dm.CurrentTarget());                        // battle ended by the cut
        Assert.Equal(4000.0, afterCut.Information[Dealer].Amount, 3); // the post-cut hit was NOT counted

        HitDummy(dm, clock[0] + 1_000, 888_888); // still ignored until a reset
        DpsReport still = calc.GetDps();
        Assert.Equal(-1, dm.CurrentTarget());
        Assert.Equal(4000.0, still.Information[Dealer].Amount, 3);
    }

    [Fact]
    public void Duration_hard_cut_fires_from_the_periodic_tick_without_a_hit()
    {
        (DataManager dm, DpsCalculator calc, long[] clock) = Setup();
        dm.DummyTestMode = true;
        dm.DummyDurationSec = 30;

        HitDummy(dm, clock[0], 4000);
        calc.GetDps();
        Assert.Equal(DummyInstance, dm.CurrentTarget());

        clock[0] += 31_000; // no further hits — the tick must still cut
        calc.GetDps();
        Assert.Equal(-1, dm.CurrentTarget());
    }

    [Fact]
    public void A_dummy_run_is_never_saved_to_battle_history()
    {
        (DataManager dm, DpsCalculator calc, long[] clock) = Setup();
        dm.DummyTestMode = true;
        dm.DummyDurationSec = 30;

        HitDummy(dm, clock[0], 4000);
        calc.GetDps();
        clock[0] += 31_000;
        calc.GetDps(); // cut → end transition

        Assert.Null(dm.BattleLog(0)); // nothing saved
    }

    [Fact]
    public void A_boss_battle_is_still_saved_to_history()
    {
        (DataManager dm, DpsCalculator calc, long[] clock) = Setup();
        dm.MobHp(BossInstance, 5000);
        dm.StartBattle(BossInstance);
        dm.SaveDamage(
            new ParsedDamagePacket { ActorId = Dealer, TargetId = BossInstance, Damage = 3000, Timestamp = clock[0] + 500 },
            dm.CurrentEpoch());
        calc.GetDps();
        clock[0] += 2_000;
        dm.EndBattle(BossInstance);
        calc.GetDps(); // end transition → saved

        Assert.NotNull(dm.BattleLog(0)); // the dummy save-skip must not affect bosses
    }

    [Fact]
    public void Reset_clears_the_live_dummy_report_and_re_arms_the_cut()
    {
        (DataManager dm, DpsCalculator calc, long[] clock) = Setup();
        dm.DummyTestMode = true;
        dm.DummyDurationSec = 30;

        HitDummy(dm, clock[0], 4000);
        calc.GetDps();
        clock[0] += 31_000;
        calc.GetDps(); // cut (cutoff latched, report frozen at 4000)

        calc.ResetDummyBattle(); // 허수아비 DPS 초기화
        Assert.Empty(calc.GetDps().Information); // live report cleared

        HitDummy(dm, clock[0] + 100, 7000); // a fresh hit opens a new window (the cut was re-armed)
        DpsReport retest = calc.GetDps();
        Assert.Equal(DummyInstance, dm.CurrentTarget());
        Assert.Equal(7000.0, retest.Information[Dealer].Amount, 3);
    }

    [Fact]
    public void Mode_off_mid_run_ends_the_dummy_battle_on_the_next_tick()
    {
        (DataManager dm, DpsCalculator calc, long[] clock) = Setup();
        dm.DummyTestMode = true;
        HitDummy(dm, clock[0], 4000);
        calc.GetDps();
        Assert.Equal(DummyInstance, dm.CurrentTarget());

        dm.DummyTestMode = false;
        calc.GetDps(); // TickDummyBattle ends the run
        Assert.Equal(-1, dm.CurrentTarget());
    }

    [Fact]
    public void LoadMobs_reads_the_isDummy_flag()
    {
        string path = Path.Combine(Path.GetTempPath(), "wm_mobs_" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(
            path,
            """[{"code":2300229,"name":"훈련용 허수아비","boss":false,"isDummy":true},{"code":2301008,"name":"보스","boss":true}]""");
        try
        {
            Dictionary<int, Mob> mobs = ReferenceJson.LoadMobs(path);
            Assert.True(mobs[2300229].IsDummy);  // the flag round-trips
            Assert.False(mobs[2301008].IsDummy); // absent flag defaults false
        }
        finally
        {
            File.Delete(path);
        }
    }
}

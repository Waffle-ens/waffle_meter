using WaffleMeter.Capture;
using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>
/// Covers the MeterEngine shutdown drain: quitting the app right after a wipe (or mid-battle, or after a
/// lost end toggle) must not lose the pending battle — <see cref="DpsCalculator.ResetDataStorage"/> is
/// called as the consumer thread exits and saves the battle iff it is non-empty and unsaved, which is what
/// fires OnBattleLogged and lets the position replay persist. A battle that already went through the
/// normal end-transition save must NOT be saved twice.
/// </summary>
public sealed class DpsCalculatorShutdownSaveTests
{
    private const int Instance = 100;
    private const int BossCode = 2301008;
    private const int Dealer = 5001;

    private static (DataManager Dm, DpsCalculator Calc, long[] Clock) Boss()
    {
        long[] now = { 1_000_000 };
        var dm = new DataManager { Clock = () => now[0] };
        dm.LoadMobs(new Dictionary<int, Mob> { [BossCode] = new Mob(BossCode, "보스", Boss: true) });
        dm.SaveMobId(Instance, BossCode);
        dm.SaveNickname(Dealer, "딜러", isExecutor: true, server: 2003, jobByte: 32);
        return (dm, new DpsCalculator(dm), now);
    }

    private static void Hit(DataManager dm, long timestamp, int damage) =>
        dm.SaveDamage(
            new ParsedDamagePacket { ActorId = Dealer, TargetId = Instance, Damage = damage, Timestamp = timestamp },
            dm.CurrentEpoch());

    [Fact]
    public void Shutdown_drain_saves_the_pending_unsaved_battle()
    {
        (DataManager dm, DpsCalculator calc, long[] clock) = Boss();
        var logged = new List<DpsLog>();
        calc.OnBattleLogged = logged.Add;

        // A battle is in progress (damage flowing, no end toggle yet) when the user quits.
        dm.MobHp(Instance, 5000);
        dm.StartBattle(Instance);
        Hit(dm, clock[0] + 1_000, 3000);
        Hit(dm, clock[0] + 2_000, 3000);
        calc.GetDps(); // a live tick mid-battle

        calc.ResetDataStorage(); // the shutdown drain (MeterEngine.ConsumeLoop exit)

        DpsLog log = Assert.Single(logged); // saved -> OnBattleLogged fired -> replay recording built
        Assert.Equal(6000.0, log.Report.Information[Dealer].Amount, 3);
    }

    [Fact]
    public void Shutdown_drain_does_not_double_save_an_ended_battle()
    {
        (DataManager dm, DpsCalculator calc, long[] clock) = Boss();
        var logged = new List<DpsLog>();
        calc.OnBattleLogged = logged.Add;

        dm.MobHp(Instance, 5000);
        dm.StartBattle(Instance);
        Hit(dm, clock[0] + 1_000, 3000);
        calc.GetDps();
        clock[0] += 3_000;
        dm.EndBattle(Instance);
        calc.GetDps(); // end transition: the battle is saved on the normal path

        Assert.Single(logged);

        calc.ResetDataStorage(); // then the user quits

        Assert.Single(logged); // no duplicate save on shutdown
    }
}

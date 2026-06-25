using WaffleMeter.Capture;
using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>
/// Locks the finished-battle identity freeze. Once a battle ends, the standby/idle report's contributor
/// identity (nickname/server/job/power) is frozen at battle-end — so a later entity-id REUSE (the game
/// reissues a retired uid to a DIFFERENT nearby player, which DataManager.SaveNickname applies to the live
/// repository User IN PLACE) does NOT repaint the still-displayed finished-battle row. This reproduces the
/// 8-player-raid 수컷→하또사 bug in miniature: the live repo is reused (intentional) but the displayed
/// finished battle stays correct.
/// </summary>
public sealed class DpsCalculatorFinishedBattleIdentityFreezeTests
{
    private const int Instance = 100;
    private const int BossCode = 2301008;
    private const int Dealer = 5001;

    [Fact]
    public void Standby_report_identity_is_frozen_against_later_uid_reuse()
    {
        long[] now = { 1_000_000 };
        var dm = new DataManager { Clock = () => now[0] };
        dm.LoadMobs(new Dictionary<int, Mob> { [BossCode] = new Mob(BossCode, "보스", Boss: true) });
        dm.SaveMobId(Instance, BossCode);
        // a non-executor party member (own:false), mirroring the reused uid 9804 in the real raid bug
        dm.SaveNickname(Dealer, "수컷", isExecutor: false, server: 1009, jobByte: 34);
        var calc = new DpsCalculator(dm);

        // ---- fight the boss ----
        dm.MobHp(Instance, 5000);
        dm.StartBattle(Instance);
        dm.SaveDamage(new ParsedDamagePacket { ActorId = Dealer, TargetId = Instance, Damage = 3000, Timestamp = now[0] + 1_000 }, dm.CurrentEpoch());
        dm.SaveDamage(new ParsedDamagePacket { ActorId = Dealer, TargetId = Instance, Damage = 2000, Timestamp = now[0] + 2_000 }, dm.CurrentEpoch());
        calc.GetDps(); // a live tick mid-fight

        // ---- the battle ends -> standby report is built + frozen ----
        now[0] += 3_000;
        dm.EndBattle(Instance);
        DpsReport ended = calc.GetDps(); // end transition (RefreshRecentReportFromCache deep-copies contributors)
        User endedContrib = Assert.Single(ended.Contributors, u => u.Id == Dealer);
        Assert.Equal("수컷", endedContrib.Nickname);
        Assert.Equal(1009, endedContrib.Server);
        Assert.Equal(JobClassInfo.ConvertFromCode(34), endedContrib.Job);

        // ---- minutes later the game REUSES entity id `Dealer` for a DIFFERENT nearby player ----
        now[0] = 2_000_000;
        dm.SaveNickname(Dealer, "하또사", isExecutor: false, server: 2003, jobByte: 28);

        // the LIVE repository User is reused (intentional — entity-id reuse, see SaveNickname)...
        Assert.Equal("하또사", dm.User(Dealer)!.Nickname);
        Assert.Equal(2003, dm.User(Dealer)!.Server);

        // ...but the STANDBY report the overlay is still displaying stays FROZEN to the real contributor.
        DpsReport standby = calc.GetDps(); // still CurrentTarget == -1 -> returns the frozen _recentData
        User stillShown = Assert.Single(standby.Contributors, u => u.Id == Dealer);
        Assert.Equal("수컷", stillShown.Nickname); // pre-fix this was repainted to "하또사"
        Assert.Equal(1009, stillShown.Server);
        Assert.Equal(JobClassInfo.ConvertFromCode(34), stillShown.Job);
    }
}

using WaffleMeter.Capture;
using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>
/// Covers the soft reset behind the "초기화 누르면 인식된 캐릭터까지 날아간다" fix: DataManager.ResetBattleRecords (and
/// DpsCalculator.ResetKeepingCharacters) clear the battle ledger (saved history + in-flight packets + lifecycle
/// state) but PRESERVE recognized users/executor, the mob-instance map, and the party roster — so combat info
/// still appears on the next pull inside a dungeon with no zone reload.
/// </summary>
public sealed class DataManagerSoftResetTests
{
    private static DpsLog SaveOneBattle(DataManager dm)
    {
        var report = new DpsReport { BattleStart = 1, BattleEnd = 2 };
        report.Contributors.Add(new User(1, "플러시", 2003));
        report.Information[1] = new DpsInformation(100, 50, 100, 10);
        return dm.SaveBattleLog(
            report,
            new Dictionary<int, Dictionary<string, AnalyzedSkill>>(),
            new Dictionary<int, List<OperatingData>>(),
            new List<OperatingData>());
    }

    [Fact]
    public void SoftReset_keeps_users_and_executor_but_clears_history()
    {
        var dm = new DataManager { Clock = () => 1_000_000 };
        dm.SaveNickname(1, "플러시", isExecutor: true, server: 2003, jobByte: 32);
        dm.SaveNickname(2, "Wildz", isExecutor: false, server: 1014, jobByte: 0);
        dm.SaveUserPower(1, 3000);
        SaveOneBattle(dm);
        Assert.Single(dm.RecentBattleList());

        dm.ResetBattleRecords();

        Assert.NotNull(dm.User(1));
        Assert.NotNull(dm.User(2));
        Assert.Equal(1, dm.ExecutorId());
        Assert.Equal("플러시", dm.User(1)!.Nickname);
        Assert.Equal(3000, dm.User(1)!.Power);
        Assert.Empty(dm.RecentBattleList());     // saved history cleared
        Assert.True(dm.CurrentTarget() <= 0);    // no active battle
    }

    [Fact]
    public void SoftReset_keeps_a_provisional_EnsureUser()
    {
        var dm = new DataManager();
        User provisional = dm.EnsureUser(13601);

        dm.ResetBattleRecords();

        Assert.Same(provisional, dm.User(13601)); // bare provisional survives, ready to be enriched in place
    }

    [Fact]
    public void SoftReset_clears_party_roster_but_keeps_mob_instance_map()
    {
        var dm = new DataManager { Clock = () => 1_000_000 };
        dm.SaveNickname(1, "플러시", isExecutor: true, server: 2003, jobByte: 0);
        dm.SavePartyRoster(new List<(string, int, int)> { ("플러시", 2003, 1) });
        dm.SaveMobId(100, 2301008);

        dm.ResetBattleRecords();

        Assert.Empty(dm.PartyRoster(300_000));         // roster cleared — a stale party must not preview on reset
        Assert.NotNull(dm.User(1));                    // ...but the recognized user itself survives
        Assert.Equal(2301008, dm.GetMobId(100));       // spawned-mob map survives (0x3640 won't re-fire in-dungeon)
    }

    [Fact]
    public void SoftReset_bumps_epoch_so_pre_reset_damage_is_rejected()
    {
        var dm = new DataManager();
        long oldEpoch = dm.CurrentEpoch();

        dm.ResetBattleRecords();

        Assert.NotEqual(oldEpoch, dm.CurrentEpoch());
        dm.SaveDamage(new ParsedDamagePacket { ActorId = 1, TargetId = 100, Damage = 500, Timestamp = 1000 }, oldEpoch);
        Assert.Null(dm.BattleData(100)); // a stale-epoch packet is dropped, not saved
    }

    [Fact]
    public void SoftReset_clears_recently_ended_so_an_immediate_repull_is_not_suppressed()
    {
        var dm = new DataManager { Clock = () => 1_000_000 };
        dm.SaveMobId(100, 2301008);
        dm.MobHp(100, 5000);
        dm.StartBattle(100);
        dm.MobHp(100, 0);
        dm.EndBattle(100);

        dm.ResetBattleRecords();

        dm.StartBattle(100); // would be swallowed by the corpse-guard if recently-ended weren't cleared
        Assert.Equal(100, dm.CurrentTarget());
    }

    [Fact]
    public void HardReset_wipes_users_unlike_the_soft_reset()
    {
        // Contrast: HardReset (the true full wipe, kept for completeness) still clears recognized users, whereas
        // the soft reset above preserves them. Keeps the full-wipe path covered now that the button uses soft reset.
        var dm = new DataManager { Clock = () => 1_000_000 };
        dm.SaveNickname(1, "플러시", isExecutor: true, server: 2003, jobByte: 0);
        SaveOneBattle(dm);

        dm.HardReset();

        Assert.Null(dm.User(1));             // recognized users wiped (soft reset would keep them)
        Assert.Empty(dm.RecentBattleList());
    }

    [Fact]
    public void Calculator_ResetKeepingCharacters_clears_live_report_but_users_survive()
    {
        var dm = new DataManager { Clock = () => 1_000_000 };
        var calc = new DpsCalculator(dm);
        dm.SaveNickname(1, "플러시", isExecutor: true, server: 2003, jobByte: 32);
        SaveOneBattle(dm);

        calc.ResetKeepingCharacters();

        Assert.True(calc.GetRecentData().IsEmpty()); // live/recent report wiped
        Assert.Empty(dm.RecentBattleList());         // history wiped
        Assert.NotNull(dm.User(1));                  // recognized character survives
        Assert.Equal(1, dm.ExecutorId());
    }
}

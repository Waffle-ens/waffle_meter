using WaffleMeter.App.Core;
using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

/// <summary>
/// 소환수가 죽어도 그 소환수가 건 버프는 주인의 버프다. 사망으로 주인 해석이 끊기면 상세창의 '내 버프'가 '남이 준
/// 버프'로 옮겨 간다(2026-10-27 카이라 재생에서 8행).
/// </summary>
public sealed class SummonDeathDetailTests
{
    private const int BossInstance = 100, BossCode = 2301008, Owner = 5001, Summon = 6001, BuffCode = 16110000;

    private static (DataManager Dm, DpsCalculator Calc, long T0) Setup()
    {
        long[] now = { 1_000_000 };
        var dm = new DataManager { Clock = () => now[0] };
        dm.LoadMobs(new Dictionary<int, Mob> { [BossCode] = new Mob(BossCode, "보스", Boss: true) });
        dm.SaveMobId(BossInstance, BossCode);
        dm.SaveNickname(Owner, "딜러", isExecutor: true, server: 2003, jobByte: 21);
        return (dm, new DpsCalculator(dm), now[0]);
    }

    private static DetailModel Detail(DpsCalculator calc)
    {
        var buffs = new List<OperatingData>
        {
            new OperatingData(BuffCode, "소환수 버프", "", "", 80.0, Summon, BuffCode, 16, 0),
        };
        return DetailModel.Compute(
            new Dictionary<string, AnalyzedSkill>(), buffs, [], Owner, JobClass.ELEMENTALIST, 0, 10_000,
            null, null, null, new Dictionary<int, string> { [Owner] = "딜러" }, calc.ResolveSummonOwner);
    }

    [Fact]
    public void A_living_summons_buff_is_in_my_buffs()
    {
        (DataManager dm, DpsCalculator calc, _) = Setup();
        dm.SaveSummon(Summon, Owner);

        DetailBuffSection section = Assert.Single(Detail(calc).Buffs);
        Assert.Equal("내 버프", section.Label);
    }

    [Fact]
    public void A_dead_summons_buff_stays_in_my_buffs()
    {
        (DataManager dm, DpsCalculator calc, long t0) = Setup();
        dm.SaveSummon(Summon, Owner);
        dm.SaveEntityDeath(Summon, t0 + 600);

        DetailBuffSection section = Assert.Single(Detail(calc).Buffs);
        Assert.Equal("내 버프", section.Label);
        Assert.False(section.FromOtherPlayers);
        Assert.Null(section.Rows[0].CasterName);
    }
}

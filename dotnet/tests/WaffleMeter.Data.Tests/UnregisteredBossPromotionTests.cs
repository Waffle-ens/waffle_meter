using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>
/// 스폰(0x3641)이 통째로 유실돼 mobCode가 등록되지 않은 던전 보스를 HP 휴리스틱으로 되살리는 안전 게이트
/// (<see cref="DataManager.TryPromoteUnregisteredBoss"/>). "1·2번째 네임드는 되는데 3번째는 집계 안 됨"의
/// 완전유실 갈래. 게이트: ① HP ≥ 던전 보스 임계 ② 미등록 ③ 진행 중인 전투 없음 ④ 교전 토글(0x8D21)을 쏜 적
/// 있음. ④가 기믹 오브젝트/주변 엔티티를 걸러내는 핵심 안전선, ①이 플레이어/잡몹을 걸러낸다.
/// </summary>
public sealed class UnregisteredBossPromotionTests
{
    [Fact]
    public void An_unregistered_entity_with_an_engage_toggle_and_boss_hp_is_promoted_and_aggregates()
    {
        long now = 1_000_000;
        var dm = new DataManager { Clock = () => now };
        dm.RememberUnresolvedBattleStart(500); // 0x8D21 toggle=1 (mobCode 미등록)
        dm.TryPromoteUnregisteredBoss(500, 50_000_000);

        Assert.Equal(500, dm.CurrentTarget()); // 전투가 열렸다 → 이제 DPS가 집계된다
        Assert.Equal(DataManager.UnknownBossMobCode, dm.GetMobId(500));
        Assert.Equal("미상 보스", dm.Mob(DataManager.UnknownBossMobCode)?.Name);
        Assert.True(dm.Mob(DataManager.UnknownBossMobCode)?.Boss);
    }

    [Fact]
    public void Without_an_engage_toggle_a_high_hp_entity_is_not_promoted()
    {
        var dm = new DataManager { Clock = () => 1_000_000 };
        dm.TryPromoteUnregisteredBoss(500, 50_000_000); // 교전 토글 없음(기믹/주변 엔티티)

        Assert.True(dm.CurrentTarget() <= 0);
        Assert.Null(dm.GetMobId(500));
    }

    [Fact]
    public void Hp_below_the_boss_threshold_is_not_promoted()
    {
        var dm = new DataManager { Clock = () => 1_000_000 };
        dm.RememberUnresolvedBattleStart(500);
        dm.TryPromoteUnregisteredBoss(500, 5_000_000); // 잡몹/플레이어급 HP

        Assert.True(dm.CurrentTarget() <= 0);
        Assert.Null(dm.GetMobId(500));
    }

    [Fact]
    public void An_already_registered_entity_is_never_relabelled_as_the_unknown_boss()
    {
        var dm = new DataManager { Clock = () => 1_000_000 };
        dm.SaveMobId(500, 2300154); // 이미 등록된 진짜 보스
        dm.RememberUnresolvedBattleStart(500);
        dm.TryPromoteUnregisteredBoss(500, 50_000_000);

        Assert.Equal(2300154, dm.GetMobId(500)); // 미상 보스로 덮지 않는다
    }

    [Fact]
    public void A_promotion_never_interrupts_a_live_battle()
    {
        var dm = new DataManager { Clock = () => 1_000_000 };
        dm.RememberUnresolvedBattleStart(400);
        dm.TryPromoteUnregisteredBoss(400, 50_000_000); // 400이 라이브가 됨
        Assert.Equal(400, dm.CurrentTarget());

        dm.RememberUnresolvedBattleStart(500);
        dm.TryPromoteUnregisteredBoss(500, 50_000_000); // 라이브 전투 중 → 무시

        Assert.Equal(400, dm.CurrentTarget());
        Assert.Null(dm.GetMobId(500));
    }
}

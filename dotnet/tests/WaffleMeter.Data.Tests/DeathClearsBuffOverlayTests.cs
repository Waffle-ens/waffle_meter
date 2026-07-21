using WaffleMeter.Capture;
using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>
/// 본인 사망(0x8D04) 시 버프 오버레이 스토어를 비운다 — 부활하면 게임에서 모든 버프가 날아간 상태이기 때문.
/// 몹·파티원의 사망은 무시해야 하고, 스킬 쿨다운은 사망으로 초기화되지 않으므로 함께 지우면 안 된다.
/// </summary>
public sealed class DeathClearsBuffOverlayTests
{
    private const int Me = 700;
    private const int Ally = 701;
    private const int JobBuff = 117800071; // 검성 '노련한 반격' 런타임 코드
    private const int JobBuffBase = 11780000;

    private static DataManager WithSelfAndBuff(long t0)
    {
        var dm = new DataManager { Clock = () => t0 };
        dm.SaveNickname(Me, "본인", isExecutor: true, server: 3, jobByte: 0);
        dm.SaveUseBuff(Me, JobBuff, t0, t0 + 30_000, 30_000, actorId: Me);
        Assert.NotEmpty(dm.ActiveOwnerBuffs(t0 + 1_000));
        return dm;
    }

    [Fact]
    public void Own_death_clears_every_buff_slot()
    {
        long t0 = 1_000_000;
        DataManager dm = WithSelfAndBuff(t0);

        dm.SaveEntityDeath(Me, t0 + 5_000);

        Assert.Empty(dm.ActiveOwnerBuffs(t0 + 6_000));
    }

    [Fact]
    public void A_party_members_death_leaves_my_buffs_alone()
    {
        long t0 = 1_000_000;
        DataManager dm = WithSelfAndBuff(t0);

        dm.SaveEntityDeath(Ally, t0 + 5_000);

        Assert.NotEmpty(dm.ActiveOwnerBuffs(t0 + 6_000));
    }

    [Fact]
    public void Death_does_not_wipe_skill_cooldowns()
    {
        // 사망이 스킬 쿨다운을 초기화하지는 않는다 — 함께 지우면 다음 0x3847 스냅샷까지 회색 표시가 틀린다.
        long t0 = 1_000_000;
        DataManager dm = WithSelfAndBuff(t0);
        dm.SaveCooldown(JobBuffBase, 20_000, t0, actorId: 0);

        dm.SaveEntityDeath(Me, t0 + 1_000);

        // 버프는 사라졌지만 쿨다운 항목은 남아, 같은 버프가 다시 켜지면 회색 처리가 이어진다.
        Assert.Empty(dm.ActiveOwnerBuffs(t0 + 2_000));
        dm.SaveUseBuff(Me, JobBuff, t0 + 2_000, t0 + 32_000, 30_000, actorId: Me);
        Assert.True(Assert.Single(dm.ActiveOwnerBuffs(t0 + 3_000)).OnCooldown);
    }

    [Fact]
    public void Clear_revision_advances_only_on_own_death()
    {
        long t0 = 1_000_000;
        DataManager dm = WithSelfAndBuff(t0);
        long before = dm.OwnerBuffClearRevision;

        dm.SaveEntityDeath(Ally, t0 + 1_000);
        Assert.Equal(before, dm.OwnerBuffClearRevision);

        dm.SaveEntityDeath(Me, t0 + 2_000);
        Assert.NotEqual(before, dm.OwnerBuffClearRevision); // 오버레이 틱이 종료 음성을 건너뛰는 신호
    }

    [Fact]
    public void Death_before_self_is_known_is_a_no_op()
    {
        long t0 = 1_000_000;
        var dm = new DataManager { Clock = () => t0 };

        dm.SaveEntityDeath(12345, t0); // executor 미확정 — 아무 일도 일어나면 안 된다

        Assert.Equal(0, dm.OwnerBuffClearRevision);
    }
}

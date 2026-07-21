using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>
/// 회생의 계약의 (B) 긴급 회복 프록 추적. 이 효과는 버프로 방송되지 않아 actor == target 인 0x3804
/// 프레임으로만 오고, 1분 재발동 제한을 알려주는 서버 신호가 없어 락아웃을 우리가 센다. (A) 5초
/// 상태이상-저항 스택은 종전대로 자기 슬롯에 그대로 두고, 회복 쿨다운은 별도 슬롯(base + 7)에 띄운다 —
/// 한 슬롯을 공유하면 _ownerBuffs가 last-write-wins라 (A)가 60초를 5초로 잘라먹기 때문이다.
/// </summary>
public sealed class RevivalHealProcTests
{
    private const int GungseongHeal = 14790007;    // 궁성 회복 프록 (원본 코드)
    private const int GungseongBase = 14790000;    // 그 직업의 회생의 계약 버프 base = (A) 슬롯
    private const int GungseongCooldown = 14790007; // (B) 회복 쿨다운 슬롯 = base + 7
    private const int GungseongResist = 147900081; // (A) 5초 상태이상 저항 스택

    private static DataManager WithExecutor(int uid)
    {
        var dm = new DataManager();
        dm.SaveNickname(uid, "궁성", isExecutor: true, server: 3, jobByte: 0);
        return dm;
    }

    [Fact]
    public void Heal_proc_opens_a_60s_cooldown_slot_of_its_own()
    {
        long t0 = 1_000_000;
        DataManager dm = WithExecutor(7);

        dm.SaveRevivalHeal(7, GungseongHeal, amount: 20_000, arrivedAt: t0);

        OwnerBuffView slot = Assert.Single(dm.ActiveOwnerBuffs(t0 + 30_000));
        Assert.Equal(GungseongCooldown, slot.Code);
        Assert.Equal(t0 + 60_000, slot.EndMs);
        Assert.False(slot.Indefinite);
        Assert.Equal("회계·회복", slot.Name);

        Assert.Empty(dm.ActiveOwnerBuffs(t0 + 60_001)); // 재발동 가능해지면 슬롯이 사라진다
    }

    [Fact]
    public void The_resist_stack_and_the_heal_cooldown_are_two_independent_slots()
    {
        long t0 = 1_000_000;
        DataManager dm = WithExecutor(7);
        dm.SaveRevivalHeal(7, GungseongHeal, 20_000, t0);

        // (A)는 종전대로 자기 슬롯에 뜨고, (B)의 60초 카운트다운을 건드리지 않는다.
        dm.SaveUseBuff(7, GungseongResist, t0 + 1_000, t0 + 6_000, 5_000, actorId: 7);

        IReadOnlyList<OwnerBuffView> both = dm.ActiveOwnerBuffs(t0 + 3_000);
        Assert.Equal(2, both.Count);
        Assert.Equal(t0 + 6_000, Assert.Single(both, b => b.Code == GungseongBase).EndMs);
        Assert.Equal(t0 + 60_000, Assert.Single(both, b => b.Code == GungseongCooldown).EndMs);

        // (A)가 만료된 뒤에도 회복 쿨다운은 계속 남는다.
        OwnerBuffView left = Assert.Single(dm.ActiveOwnerBuffs(t0 + 30_000));
        Assert.Equal(GungseongCooldown, left.Code);
    }

    [Fact]
    public void Another_players_heal_is_counted_but_never_shown_on_the_self_overlay()
    {
        long t0 = 1_000_000;
        DataManager dm = WithExecutor(7);

        dm.SaveRevivalHeal(9, 13790007, 15_000, t0); // 파티원(살성)

        Assert.Equal(1, dm.RevivalHealSummary(9, t0 - 1, t0 + 1).Count);
        Assert.Empty(dm.ActiveOwnerBuffs(t0 + 10_000));
    }

    [Fact]
    public void Summary_counts_only_procs_inside_the_battle_window()
    {
        long t0 = 1_000_000;
        DataManager dm = WithExecutor(7);

        dm.SaveRevivalHeal(7, GungseongHeal, 20_000, t0);
        dm.SaveRevivalHeal(7, GungseongHeal, 20_000, t0 + 60_500);
        dm.SaveRevivalHeal(7, GungseongHeal, 20_000, t0 + 500_000); // 창 밖

        (int count, int code, string name) = dm.RevivalHealSummary(7, t0, t0 + 120_000);

        Assert.Equal(2, count);
        // (A)도 "회생의 계약" 이름으로 가동률 행을 차지하므로 발동 횟수 행은 별도 이름/코드로 구분한다.
        Assert.Equal(GungseongCooldown, code);
        Assert.Equal("회계·회복", name);

        Assert.Equal(0, dm.RevivalHealSummary(999, t0, t0 + 120_000).Count); // 다른 uid
    }
}

using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>
/// 치유성 '대지의 징벌'(17400000)은 대상 몹에게 디버프 '대지의 징벌'을, 본인+파티원에게는 이름이 다른 버프
/// '대지의 축복'을 건다. 두 효과를 같은 base로 접으면 오버레이·음성·picker가 인게임과 다른 이름과 아이콘을
/// 쓰므로, 축복 쪽 abnormal 코드만 표시용 base(17400058)로 분리한다.
/// </summary>
public sealed class EarthBlessingSplitTests
{
    private const int Me = 800;
    private const int BlessingRuntime = 174000571; // 본인+파티원에게 붙는 '대지의 축복'
    private const int BlessingBase = 17400058;
    private const int PunishRuntime = 174000001;   // 몹에게 붙는 '대지의 징벌'
    private const int PunishBase = 17400000;

    [Fact]
    public void Blessing_and_punishment_no_longer_share_a_display_base()
    {
        Assert.Equal(BlessingBase, DataManager.BuffDisplayBase(BlessingRuntime));
        Assert.Equal(BlessingBase, DataManager.BuffDisplayBase(174000271));
        Assert.Equal(BlessingBase, DataManager.BuffDisplayBase(174000371));

        Assert.Equal(PunishBase, DataManager.BuffDisplayBase(PunishRuntime));
        // 집계/통계가 쓰는 BuffBaseCode는 종전 그대로여야 한다(웹 페이로드 불변).
        Assert.Equal(PunishBase, DataManager.BuffBaseCode(BlessingRuntime));
    }

    [Fact]
    public void Other_job_buffs_are_untouched_by_the_override()
    {
        Assert.Equal(17410000, DataManager.BuffDisplayBase(174100511)); // 보호의 빛
        Assert.Equal(18250000, DataManager.BuffDisplayBase(182500511)); // 질풍의 권능
        Assert.Equal(11780000, DataManager.BuffDisplayBase(117800071)); // 노련한 반격
    }

    [Fact]
    public void The_overlay_slot_carries_the_in_game_name()
    {
        long t0 = 1_000_000;
        var dm = new DataManager { Clock = () => t0 };
        dm.LoadBuffNames(new[]
        {
            (PunishBase, "대지의 징벌", "치유성"),
            (BlessingBase, "대지의 축복", "치유성"),
        });
        dm.SaveNickname(Me, "치유성", isExecutor: true, server: 3, jobByte: 0);

        dm.SaveUseBuff(Me, BlessingRuntime, t0, t0 + 20_000, 20_000, actorId: Me);

        OwnerBuffView slot = Assert.Single(dm.ActiveOwnerBuffs(t0 + 1_000));
        Assert.Equal(BlessingBase, slot.Code);   // 아이콘 조회 키 = 축복 아이콘
        Assert.Equal("대지의 축복", slot.Name);  // 오버레이 라벨 + TTS 문구
    }
}

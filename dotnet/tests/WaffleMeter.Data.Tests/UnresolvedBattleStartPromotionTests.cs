using WaffleMeter.Capture;
using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>
/// 소급 승격: 시작 토글이 왔지만 그 엔티티의 mobCode가 아직 없어 거부됐을 때, 스폰이 늦게 도착하면 그 전투를
/// 되살린다. 보스 스폰(0x3641)은 교전당 1회뿐이고 전투 중 재방송이 없어서, 그냥 버리면 그 판은 끝까지 안 열린다.
/// <para>동시에 이 경로가 기존 전투 인식을 망가뜨리지 않아야 한다 — 잡몹/허수아비로는 절대 열리면 안 되고
/// (잡몹마다 전투가 열리면 전투창이 절단·분할된다), 진행 중인 전투를 가로채도 안 된다.</para>
/// </summary>
public sealed class UnresolvedBattleStartPromotionTests
{
    private const int Instance = 100;
    private const int OtherInstance = 200;
    private const int BossCode = 2301008;
    private const int TrashCode = 2100001;
    private const int DummyCode = 2400032;

    private static (DataManager Dm, long[] Clock) Setup()
    {
        long[] now = { 1_000_000 };
        var dm = new DataManager { Clock = () => now[0] };
        dm.LoadMobs(new Dictionary<int, Mob>
        {
            [BossCode] = new Mob(BossCode, "보스", Boss: true),
            [TrashCode] = new Mob(TrashCode, "잡몹", Boss: false),
            [DummyCode] = new Mob(DummyCode, "훈련용 허수아비", Boss: false, IsDummy: true),
        });
        return (dm, now);
    }

    [Fact]
    public void Late_spawn_revives_the_rejected_start_and_backdates_it_to_the_toggle()
    {
        (DataManager dm, long[] now) = Setup();
        long toggledAt = now[0];

        dm.RememberUnresolvedBattleStart(Instance); // 파서가 mob_code_missing으로 거부한 시작 토글
        Assert.True(dm.CurrentTarget() <= 0);       // 아직 전투 없음

        now[0] += 5_000;                 // 스폰이 5초 늦게 도착
        dm.SaveMobId(Instance, BossCode);

        Assert.Equal(Instance, dm.CurrentTarget());
        // 지금(1,005,000)이 아니라 토글 시각으로 스탬프 — 안 그러면 그 5초간의 딜이 창 밖으로 떨어진다.
        Assert.Equal(toggledAt, dm.CurrentBattleStart());
    }

    [Fact]
    public void A_trash_mob_spawn_never_opens_a_battle()
    {
        (DataManager dm, long[] now) = Setup();
        dm.RememberUnresolvedBattleStart(Instance);

        now[0] += 1_000;
        dm.SaveMobId(Instance, TrashCode);

        Assert.True(dm.CurrentTarget() <= 0);
    }

    [Fact]
    public void A_dummy_spawn_never_opens_a_battle()
    {
        (DataManager dm, long[] now) = Setup();
        dm.RememberUnresolvedBattleStart(Instance);

        now[0] += 1_000;
        dm.SaveMobId(Instance, DummyCode);

        Assert.True(dm.CurrentTarget() <= 0);
    }

    [Fact]
    public void A_spawn_that_arrives_too_late_is_not_promoted()
    {
        (DataManager dm, long[] now) = Setup();
        dm.RememberUnresolvedBattleStart(Instance);

        now[0] += 60_001; // PendingStartTtlMs(60s) 초과 — 그 전투는 이미 지난 일이다
        dm.SaveMobId(Instance, BossCode);

        Assert.True(dm.CurrentTarget() <= 0);
    }

    [Fact]
    public void Promotion_never_hijacks_a_live_battle()
    {
        (DataManager dm, long[] now) = Setup();
        dm.SaveMobId(OtherInstance, BossCode);
        dm.StartBattle(OtherInstance);
        Assert.Equal(OtherInstance, dm.CurrentTarget());

        dm.RememberUnresolvedBattleStart(Instance);
        now[0] += 2_000;
        dm.SaveMobId(Instance, BossCode);

        Assert.Equal(OtherInstance, dm.CurrentTarget()); // 진행 중인 전투가 그대로 유지된다
    }

    [Fact]
    public void A_pending_start_is_consumed_so_a_later_respawn_cannot_refire_it()
    {
        (DataManager dm, long[] now) = Setup();
        dm.RememberUnresolvedBattleStart(Instance);

        now[0] += 1_000;
        dm.SaveMobId(Instance, TrashCode); // 잡몹이라 승격 안 됨 — 그래도 항목은 소비된다
        Assert.True(dm.CurrentTarget() <= 0);

        now[0] += 1_000;
        dm.SaveMobId(Instance, BossCode); // 같은 엔티티가 보스로 다시 등록돼도 되살아나지 않는다
        Assert.True(dm.CurrentTarget() <= 0);
    }

    [Fact]
    public void The_normal_path_still_stamps_the_current_time()
    {
        // 회귀 가드: 스폰이 정상적으로 먼저 온 통상 경로는 종전 그대로 "지금"으로 시작을 찍는다.
        (DataManager dm, long[] now) = Setup();
        dm.SaveMobId(Instance, BossCode);

        now[0] += 3_000;
        dm.StartBattle(Instance);

        Assert.Equal(Instance, dm.CurrentTarget());
        Assert.Equal(now[0], dm.CurrentBattleStart());
    }
}

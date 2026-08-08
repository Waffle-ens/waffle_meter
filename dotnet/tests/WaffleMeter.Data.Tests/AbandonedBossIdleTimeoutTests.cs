using WaffleMeter.Capture;
using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>
/// 죽지도 종료 토글도 없이 조용해진 보스를 유휴 타임아웃으로 닫는다.
/// <para>배경(2026-08-08 제보 로그): 전투 종료 경로가 <c>0x8D21 toggle==0</c>/사망 하나뿐이라, 버스(캐리)에서
/// 기사가 보스를 끌고 가거나 승객이 AoI를 벗어나 서버가 그 엔티티 갱신을 끊으면 리포트가 무기한 고착됐다.
/// 실측: 나사라크가 HP 43%로 남은 채 41.2초에 끊겨 104초 고착. 게다가 primary-lock이 "현 타깃 HP>0"을 계속
/// 만족해 <b>다음 보스까지</b> 막았다.</para>
/// <para>⚠️ 반대 방향 회귀가 더 위험하다: 임계가 짧으면 살아있는 전투를 반으로 갈라 저장/업로드를 오염시킨다
/// (191M 사건). 코퍼스 270전투의 전투 중 정상 최대 공백은 27.8초(칼드릭스 페이즈)였고, 그 케이스가 절대
/// 끊기지 않아야 한다.</para>
/// </summary>
public sealed class AbandonedBossIdleTimeoutTests
{
    private const int Boss = 100;
    private const int NextBoss = 200;
    private const int BossCode = 2311402;   // 나사라크
    private const int NextCode = 2311403;   // 나트하라

    private static (DataManager Dm, long[] Clock) Setup()
    {
        long[] now = { 1_000_000 };
        var dm = new DataManager { Clock = () => now[0] };
        dm.LoadMobs(new Dictionary<int, Mob>
        {
            [BossCode] = new Mob(BossCode, "나사라크", Boss: true),
            [NextCode] = new Mob(NextCode, "나트하라", Boss: true),
        });
        return (dm, now);
    }

    /// <summary>보스를 교전시키고 HP를 한 번 보고해 전투를 '살아있는' 상태로 만든다.</summary>
    private static void Engage(DataManager dm, int instance, int code, int hp)
    {
        dm.SaveMobId(instance, code);
        dm.StartBattle(instance);
        dm.MobHp(instance, hp);
    }

    [Fact]
    public void A_boss_that_goes_silent_without_dying_ends_after_the_idle_timeout()
    {
        (DataManager dm, long[] now) = Setup();
        Engage(dm, Boss, BossCode, hp: 14_404_354); // 43% 생존 — 사망 신호는 영영 안 온다
        Assert.Equal(Boss, dm.CurrentTarget());

        now[0] += 59_000;
        dm.TickBossBattleIdle();
        Assert.Equal(Boss, dm.CurrentTarget()); // 아직 임계 이내 — 살아 있어야 한다

        now[0] += 2_000;
        dm.TickBossBattleIdle();
        Assert.True(dm.CurrentTarget() <= 0);   // 닫혔다
        Assert.NotEqual(0L, dm.CurrentBattleEnd());
    }

    [Fact]
    public void The_end_is_stamped_at_the_last_activity_not_at_the_timeout()
    {
        // 종료를 now로 찍으면 아무 일도 없던 유휴 구간이 전투 길이에 들어가 DPS가 희석된다.
        (DataManager dm, long[] now) = Setup();
        Engage(dm, Boss, BossCode, hp: 5_000_000);

        now[0] += 20_000;
        dm.MobHp(Boss, 4_000_000);   // 마지막 활동
        long lastActivity = now[0];

        now[0] += 120_000;           // 한참 뒤에야 틱이 돌아도
        dm.TickBossBattleIdle();

        Assert.Equal(lastActivity, dm.CurrentBattleEnd());
    }

    [Fact]
    public void A_27_second_phase_lull_never_ends_a_live_fight()
    {
        // 실측 최댓값(칼드릭스 27.8초)보다 넉넉히 위. 이 테스트가 깨지면 임계를 줄인 것이고, 살아있는 전투가
        // 반토막 나 통계가 오염된다.
        (DataManager dm, long[] now) = Setup();
        Engage(dm, Boss, BossCode, hp: 9_000_000);

        for (int i = 0; i < 6; i++)
        {
            now[0] += 27_800;          // 페이즈 전환마다 27.8초씩 조용
            dm.TickBossBattleIdle();
            Assert.Equal(Boss, dm.CurrentTarget());
            dm.MobHp(Boss, 9_000_000 - (i * 1_000_000)); // 페이즈가 끝나면 HP가 다시 온다
            dm.TickBossBattleIdle();
            Assert.Equal(Boss, dm.CurrentTarget());
        }
    }

    [Fact]
    public void Hp_reports_alone_keep_a_zero_dps_phase_alive()
    {
        // 파티가 딜을 멈춘 페이즈에도 HP 보고는 계속 온다 — 데미지만 신호로 삼으면 정상 페이즈를 유휴로 오판한다.
        (DataManager dm, long[] now) = Setup();
        Engage(dm, Boss, BossCode, hp: 9_000_000);

        for (int i = 0; i < 10; i++)
        {
            now[0] += 30_000;
            dm.MobHp(Boss, 9_000_000); // 딜은 0, HP는 그대로 보고만 됨
            dm.TickBossBattleIdle();
        }

        Assert.Equal(Boss, dm.CurrentTarget());
    }

    [Fact]
    public void Primary_lock_yields_to_the_next_boss_once_the_current_one_is_stale()
    {
        // 이 가드에 신선도 항이 없으면 조용해진 보스가 HP>0으로 남아 다음 보스를 영구히 막는다.
        (DataManager dm, long[] now) = Setup();
        Engage(dm, Boss, BossCode, hp: 14_404_354);

        now[0] += 10_000;
        dm.SaveMobId(NextBoss, NextCode);
        dm.StartBattle(NextBoss);
        Assert.Equal(Boss, dm.CurrentTarget()); // 아직 신선하다 — 살아있는 보스는 보호돼야 한다

        now[0] += 61_000;                        // 무소식이 임계를 넘으면
        dm.StartBattle(NextBoss);
        Assert.Equal(NextBoss, dm.CurrentTarget()); // 양보한다
    }

    [Fact]
    public void A_live_boss_still_blocks_a_gimmick_stomp()
    {
        // primary-lock의 원래 목적(바크론 기믹이 살아있는 보스 전투를 stomp)이 유지되는지.
        (DataManager dm, long[] now) = Setup();
        Engage(dm, Boss, BossCode, hp: 9_000_000);

        for (int i = 0; i < 5; i++)
        {
            now[0] += 5_000;
            dm.MobHp(Boss, 9_000_000 - i);  // 전투가 활발히 진행 중
            dm.SaveMobId(NextBoss, NextCode);
            dm.StartBattle(NextBoss);
            Assert.Equal(Boss, dm.CurrentTarget());
        }
    }

    [Fact]
    public void An_idle_tick_does_nothing_when_there_is_no_battle()
    {
        (DataManager dm, long[] now) = Setup();
        now[0] += 10_000_000;
        dm.TickBossBattleIdle();            // 던지지 않고 무동작
        Assert.True(dm.CurrentTarget() <= 0);
    }
}

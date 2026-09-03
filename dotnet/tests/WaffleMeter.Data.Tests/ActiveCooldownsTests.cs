using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>
/// 스킬 쿨타임 오버레이가 데이터 계층에서 받는 행(<see cref="DataManager.ActiveCooldowns"/>)의 스펙.
/// <para>표시 목록은 <b>학습</b>된다 — 클라이언트가 로그인 시점 핫바 스냅샷을 보내지 않으므로, 이번 세션에
/// 실제로 쓴(또는 서버가 쿨타임을 알려 온) 스킬만이 정직하게 그릴 수 있는 전부다.</para>
/// </summary>
public sealed class ActiveCooldownsTests
{
    private const int Me = 700;
    private const int Ally = 701;

    // 실제 배포 카탈로그를 쓴다 — 여기서 쓰는 코드가 카탈로그에서 사라지면 그것도 잡아야 할 회귀다.
    private const int BlessedBow = 14_220_000;      // 궁성 축복의 활
    private const int BlessedBowCast = 14_220_050;  // 그 스킬의 특화 변종(와이어가 실제로 싣는 코드)
    private const int Weisel = 14_310_000;          // 궁성 바이젤의 권능

    private static CooldownCatalog ShippedCatalog()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "Assets", "json", "cooldown_catalog.json");
            if (File.Exists(candidate))
            {
                return CooldownCatalog.Load(candidate);
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("cooldown_catalog.json not found above " + AppContext.BaseDirectory);
    }

    private static DataManager WithSelf(long t0)
    {
        var dm = new DataManager { Clock = () => t0 };
        dm.LoadCooldownCatalog(ShippedCatalog());
        dm.SaveNickname(Me, "본인", isExecutor: true, server: 3, jobByte: 0);
        return dm;
    }

    [Fact]
    public void A_skill_appears_only_after_it_has_been_seen()
    {
        long t0 = 1_000_000;
        DataManager dm = WithSelf(t0);
        Assert.Empty(dm.ActiveCooldowns(t0));

        dm.SaveCooldown(BlessedBowCast, 80_000, t0, actorId: Me, fromCast: true);

        SkillCooldownView row = Assert.Single(dm.ActiveCooldowns(t0 + 1_000));
        Assert.Equal(BlessedBow, row.GroupId);
        Assert.Equal("축복의 활", row.Name);
        Assert.Equal(BlessedBowCast, row.DisplayCode);
    }

    [Fact]
    public void The_cast_frame_supplies_the_ring_denominator()
    {
        long t0 = 1_000_000;
        DataManager dm = WithSelf(t0);
        dm.SaveCooldown(BlessedBowCast, 80_000, t0, actorId: Me, fromCast: true);

        SkillCooldownView row = Assert.Single(dm.ActiveCooldowns(t0 + 20_000));
        Assert.Equal(80_000, row.TotalMs);
        Assert.Equal(60_000, row.RemainingMs);
        Assert.False(row.IsReady);
    }

    [Fact]
    public void A_correction_never_shrinks_the_denominator()
    {
        // 0x3847 은 '남은 시간'만 싣는다. 그걸 총량으로 삼으면 링의 분모가 매 틱 줄어들어 60초 쿨이 3초처럼
        // 보인다 — 오버레이가 조용히 거짓말하는 가장 쉬운 길이다.
        long t0 = 1_000_000;
        DataManager dm = WithSelf(t0);
        dm.SaveCooldown(BlessedBowCast, 80_000, t0, actorId: Me, fromCast: true);
        dm.SaveCooldown(BlessedBow, 30_000, t0 + 50_000, actorId: 0);

        SkillCooldownView row = Assert.Single(dm.ActiveCooldowns(t0 + 50_000));
        Assert.Equal(80_000, row.TotalMs);
        Assert.Equal(30_000, row.RemainingMs);
    }

    [Fact]
    public void A_ready_skill_stays_on_the_list_as_a_ready_row()
    {
        // 준비된 스킬이 목록에서 사라지면 아이콘 자리가 계속 바뀌어 근육기억으로 읽을 수 없다.
        long t0 = 1_000_000;
        DataManager dm = WithSelf(t0);
        dm.SaveCooldown(BlessedBowCast, 5_000, t0, actorId: Me, fromCast: true);

        SkillCooldownView row = Assert.Single(dm.ActiveCooldowns(t0 + 6_000));
        Assert.True(row.IsReady);
        Assert.Equal(0, row.RemainingMs);
    }

    [Fact]
    public void A_cast_inside_the_grace_window_reads_as_ready()
    {
        // 시전 값은 잠정이다 — 서버가 곧바로 초기화하고 0x3847 로 알려 오는 경우가 흔해서, 그 250~350ms 를
        // 쿨로 칠하면 연타 스킬에서 상시 깜빡인다.
        long t0 = 1_000_000;
        DataManager dm = WithSelf(t0);
        dm.SaveCooldown(BlessedBowCast, 80_000, t0, actorId: Me, fromCast: true);

        Assert.True(Assert.Single(dm.ActiveCooldowns(t0 + 100)).IsReady);
        Assert.False(Assert.Single(dm.ActiveCooldowns(t0 + 500)).IsReady);
    }

    [Fact]
    public void Another_players_cast_is_not_my_cooldown()
    {
        long t0 = 1_000_000;
        DataManager dm = WithSelf(t0);
        dm.SaveCooldown(BlessedBowCast, 80_000, t0, actorId: Ally, fromCast: true);

        Assert.Empty(dm.ActiveCooldowns(t0 + 1_000));
    }

    [Fact]
    public void Casts_that_arrive_before_the_executor_is_known_are_replayed_not_dropped()
    {
        // 실측: 한 세션에서 본인 시전 811건 중 224건(27.6%)이 본인 닉네임 패킷보다 먼저 도착했다.
        long t0 = 1_000_000;
        var dm = new DataManager { Clock = () => t0 };
        dm.LoadCooldownCatalog(ShippedCatalog());

        dm.SaveCooldown(BlessedBowCast, 80_000, t0, actorId: Me, fromCast: true);
        Assert.Empty(dm.ActiveCooldowns(t0 + 1_000)); // executor 미확정 — 아직 내 것인지 모른다

        dm.Clock = () => t0 + 2_000;
        dm.SaveNickname(Me, "본인", isExecutor: true, server: 3, jobByte: 0);

        SkillCooldownView row = Assert.Single(dm.ActiveCooldowns(t0 + 2_000));
        Assert.Equal(BlessedBow, row.GroupId);
        Assert.Equal(78_000, row.RemainingMs);
    }

    [Fact]
    public void A_staged_cast_that_already_expired_is_not_resurrected()
    {
        long t0 = 1_000_000;
        var dm = new DataManager { Clock = () => t0 };
        dm.LoadCooldownCatalog(ShippedCatalog());

        dm.SaveCooldown(BlessedBowCast, 5_000, t0, actorId: Me, fromCast: true);

        dm.Clock = () => t0 + 60_000;
        dm.SaveNickname(Me, "본인", isExecutor: true, server: 3, jobByte: 0);

        Assert.Empty(dm.ActiveCooldowns(t0 + 60_000));
    }

    [Fact]
    public void Rows_are_ordered_by_job_then_by_the_catalog_order()
    {
        long t0 = 1_000_000;
        DataManager dm = WithSelf(t0);
        dm.SaveCooldown(Weisel, 40_000, t0, actorId: 0);
        dm.SaveCooldown(BlessedBow, 80_000, t0, actorId: 0);

        IReadOnlyList<SkillCooldownView> rows = dm.ActiveCooldowns(t0 + 1_000);
        Assert.Equal(2, rows.Count);
        Assert.True(rows[0].Order < rows[1].Order);
        Assert.Equal(BlessedBow, rows[0].GroupId); // 14220000 < 14310000
    }

    [Fact]
    public void Skills_outside_the_catalog_are_not_drawn()
    {
        // 대시(1101) 같은 공용 코드는 0x3847 이 실제로 실어 보내지만 이름도 아이콘도 없다 — 번호만 뜬 슬롯을
        // 그리느니 빼는 편이 낫다.
        long t0 = 1_000_000;
        DataManager dm = WithSelf(t0);
        dm.SaveCooldown(1101, 3_000, t0, actorId: 0);

        Assert.Empty(dm.ActiveCooldowns(t0 + 1_000));
    }
}

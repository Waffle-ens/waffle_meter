using System.Collections.Generic;
using WaffleMeter.Capture;
using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>
/// Feature 2 (염화의 수호검 한정) — 무스펠 성배는 파티가 5/5로 나뉘어 근처 두 '염화의 수호검'을 동시에 잡는다.
/// 단일 _currentTarget은 먼저 교전된 쪽에 primary-lock으로 고정되므로, 본인이 반대쪽을 때리면 남의 전투가 보인다.
/// 이 스위치는 <b>현재·신규 타깃이 둘 다 '염화의 수호검'</b>이고, 본인(executor)이 신규 쪽을 <b>지속적으로</b> 때리며
/// 현재 쪽엔 <b>조용해진</b> 경우에만 발동한다(그 외 인카운터·단발 스침·본인이 현재도 계속 때리는 경우엔 미발동 →
/// primary-lock 그대로). back-date/윈도 무결성(191M)은 DpsCalculator 계층 몫이라 여기선 타깃 선택만 검증한다.
/// </summary>
public sealed class SplitBossSelfFollowTests
{
    private const int Self = 500;
    private const int SuhoA = 25132; // 다른 파티가 먼저 잡는 수호검 → primary-lock으로 표시됨
    private const int SuhoB = 32255; // 본인이 잡는 수호검
    private const int CodeA = 2301067;
    private const int CodeB = 2301055;

    // 두 '염화의 수호검'이 동시 교전 중이고, A가 먼저 표시된(primary-lock) 상태를 만든다.
    private static DataManager Split(string bossName = "염화의 수호검")
    {
        long[] now = { 1_000_000 };
        var dm = new DataManager { Clock = () => now[0] };
        dm.LoadMobs(new Dictionary<int, Mob>
        {
            [CodeA] = new Mob(CodeA, bossName, Boss: true),
            [CodeB] = new Mob(CodeB, bossName, Boss: true),
        });
        dm.SaveMobId(SuhoA, CodeA);
        dm.SaveMobId(SuhoB, CodeB);
        dm.MobHp(SuhoA, 50_000_000); // 둘 다 생존
        dm.MobHp(SuhoB, 50_000_000);
        dm.SaveNickname(Self, "본인", isExecutor: true, server: 2003, jobByte: 32);

        dm.StartBattle(SuhoA);
        Assert.Equal(SuhoA, dm.CurrentTarget());
        dm.StartBattle(SuhoB);
        Assert.Equal(SuhoA, dm.CurrentTarget()); // primary-lock: 살아있는 A를 B의 토글이 못 덮는다
        return dm;
    }

    private static void SelfHit(DataManager dm, int target, long timestamp) =>
        dm.SaveDamage(
            new ParsedDamagePacket { ActorId = Self, TargetId = target, Damage = 100_000, Timestamp = timestamp },
            dm.CurrentEpoch());

    [Fact]
    public void Sustained_self_damage_switches_to_the_suhogeom_the_self_is_fighting()
    {
        DataManager dm = Split();
        long t = 2_000_000;
        SelfHit(dm, SuhoB, t);          // hit 1
        SelfHit(dm, SuhoB, t + 600);    // hit 2
        SelfHit(dm, SuhoB, t + 1200);   // hit 3 — dwell 1200 < 1500 아직 부족
        Assert.Equal(SuhoA, dm.CurrentTarget()); // 아직 전환 전
        SelfHit(dm, SuhoB, t + 1600);   // hit 4 — hits>=3 && dwell>=1500 && A엔 본인 딜 0(조용) → 전환
        Assert.Equal(SuhoB, dm.CurrentTarget());
    }

    [Fact]
    public void A_single_stray_self_hit_does_not_switch()
    {
        DataManager dm = Split();
        SelfHit(dm, SuhoB, 2_000_000);           // 단발(AoE 스침)
        Assert.Equal(SuhoA, dm.CurrentTarget());  // anti-thrash: 지속 아님 → 유지
    }

    [Fact]
    public void While_the_self_keeps_hitting_the_shown_suhogeom_it_never_switches()
    {
        DataManager dm = Split();
        long t = 2_000_000;
        // 본인이 A(표시중)를 계속 때리면서 B도 스침 — 현재에 조용하지 않으므로 primary(A) 유지.
        for (int i = 0; i < 6; i++)
        {
            SelfHit(dm, SuhoA, t + i * 400);       // 현재 타깃에 지속 딜
            SelfHit(dm, SuhoB, t + i * 400 + 100); // B도 스침
        }

        Assert.Equal(SuhoA, dm.CurrentTarget());
    }

    [Fact]
    public void Non_suhogeom_bosses_are_unaffected_primary_lock_holds()
    {
        DataManager dm = Split(bossName: "일반 보스"); // 스코프 밖 — 전환 로직 자체가 안 켜진다
        long t = 2_000_000;
        SelfHit(dm, SuhoB, t);
        SelfHit(dm, SuhoB, t + 600);
        SelfHit(dm, SuhoB, t + 1200);
        SelfHit(dm, SuhoB, t + 1600);
        Assert.Equal(SuhoA, dm.CurrentTarget()); // 수호검이 아니면 primary-lock 그대로
    }
}

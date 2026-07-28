using WaffleMeter.Capture;
using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>
/// <see cref="DpsReport.BattleFinished"/>의 계약을 고정한다: 진행 중 리포트는 false, 전투가 끝난 뒤 대기 상태에서
/// 계속 내보내는 리포트는 true. 표시 계층(OverlayRowBuilder Feature 1)이 "직전 전투 위로 파티 로스터 프리뷰를
/// 다시 띄울지"를 이 플래그로 판정한다.
/// <para>왜 파티 스냅샷으로 대신 판정하면 안 되는가: 파티 없이 <b>혼자</b> 잡은 전투는 0x9702 로스터가 없어
/// <see cref="DpsReport.PartyIdentitiesSnapshot"/>이 정당하게 빈다. 그걸 "끝난 전투가 아니다"로 읽으면 그 뒤
/// 파티에 들어가도 프리뷰가 영영 뜨지 않고 직전 솔로 전투가 화면에 남는다(실측 증상).</para>
/// </summary>
public sealed class DpsCalculatorBattleFinishedFlagTests
{
    private const int Instance = 100;
    private const int BossCode = 2301008;
    private const int Solo = 5001;

    [Fact]
    public void A_solo_battle_ends_flagged_finished_even_though_it_froze_no_party()
    {
        long[] now = { 1_000_000 };
        var dm = new DataManager { Clock = () => now[0] };
        dm.LoadMobs(new Dictionary<int, Mob> { [BossCode] = new Mob(BossCode, "악몽", Boss: true) });
        dm.SaveMobId(Instance, BossCode);
        dm.SaveNickname(Solo, "본인", isExecutor: true, server: 2003, jobByte: 34);
        var calc = new DpsCalculator(dm);

        // ---- 혼자 보스를 친다 (0x9702 로스터 없음 = 파티 없음) ----
        dm.MobHp(Instance, 5000);
        dm.StartBattle(Instance);
        dm.SaveDamage(
            new ParsedDamagePacket { ActorId = Solo, TargetId = Instance, Damage = 3000, Timestamp = now[0] + 1_000 },
            dm.CurrentEpoch());
        DpsReport live = calc.GetDps();
        Assert.False(live.BattleFinished); // 진행 중 — 여기서 true면 전투 중에 미터가 프리뷰로 비워진다

        // ---- 전투 종료 ----
        now[0] += 3_000;
        dm.EndBattle(Instance);
        DpsReport ended = calc.GetDps();

        Assert.True(ended.BattleFinished);
        Assert.Empty(ended.PartyIdentitiesSnapshot); // 솔로라 스냅샷은 정당하게 빈다 — 그래서 프록시로 못 쓴다
        Assert.Empty(ended.PartySnapshot);
        Assert.NotEmpty(ended.Information);          // 직전 전투 행이 남아 있는 상태 그대로

        // 대기 상태가 이어져도 계속 "끝난 전투"다 (프리뷰 판정은 매 틱 다시 돈다).
        DpsReport standby = calc.GetDps();
        Assert.True(standby.BattleFinished);
    }
}

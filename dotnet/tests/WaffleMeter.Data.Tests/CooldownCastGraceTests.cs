using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>
/// 시전(0x3802)에서 온 쿨타임은 잠정값이라는 스펙. 서버는 시전 직후 쿨타임을 초기화하거나 줄이고 그 사실을
/// 0x3847로 알려 주는데, 그 통지가 median 251~348ms(p90 352ms) 뒤에 온다. 시전 값을 즉시 믿으면 매 시전마다
/// 그 4분의 1초 동안 아이콘이 헛회색이 된다 — 한 세션에서 도약 찍기 649시전이 255초, 단죄 529시전이 197초를
/// 그렇게 날렸다. 그래서 시전 값은 유예 창 동안 아무것도 회색으로 만들지 않고, 같은 스킬의 0x3847이 오면
/// 그 값이 유예를 끝내고 그대로 이긴다.
/// </summary>
public sealed class CooldownCastGraceTests
{
    private const int Me = 700;
    private const int JobBuff = 117800071; // 검성 '노련한 반격' 런타임 코드
    private const int JobBuffBase = 11780000;
    private const long GraceMs = 400;

    private static DataManager WithSelfAndBuff(long t0)
    {
        var dm = new DataManager { Clock = () => t0 };
        dm.SaveNickname(Me, "본인", isExecutor: true, server: 3, jobByte: 0);
        dm.SaveUseBuff(Me, JobBuff, t0, t0 + 60_000, 60_000, actorId: Me);
        return dm;
    }

    private static bool OnCooldown(DataManager dm, long atMs)
        => Assert.Single(dm.ActiveOwnerBuffs(atMs)).OnCooldown;

    [Fact]
    public void A_cast_does_not_gray_anything_inside_the_grace_window()
    {
        long t0 = 1_000_000;
        DataManager dm = WithSelfAndBuff(t0);

        dm.SaveCooldown(JobBuffBase, 20_000, t0, actorId: Me, fromCast: true);

        Assert.False(OnCooldown(dm, t0));
        Assert.False(OnCooldown(dm, t0 + GraceMs - 1));
    }

    [Fact]
    public void A_cast_grays_once_the_grace_window_has_passed_with_no_correction()
    {
        long t0 = 1_000_000;
        DataManager dm = WithSelfAndBuff(t0);

        dm.SaveCooldown(JobBuffBase, 20_000, t0, actorId: Me, fromCast: true);

        Assert.True(OnCooldown(dm, t0 + GraceMs));
        Assert.True(OnCooldown(dm, t0 + 19_000));
        Assert.False(OnCooldown(dm, t0 + 20_001)); // 쿨 종료
    }

    [Fact]
    public void A_snapshot_saying_ready_kills_the_cast_guess_outright()
    {
        // 도약 찍기·단죄가 매 시전마다 겪던 경로: 시전이 11초를 걸고, 서버가 곧바로 초기화해 rem=0을 보낸다.
        long t0 = 1_000_000;
        DataManager dm = WithSelfAndBuff(t0);

        dm.SaveCooldown(JobBuffBase, 11_150, t0, actorId: Me, fromCast: true);
        dm.SaveCooldown(JobBuffBase, 0, t0 + 348, actorId: 0);

        Assert.False(OnCooldown(dm, t0 + 349));
        Assert.False(OnCooldown(dm, t0 + 5_000));
    }

    [Fact]
    public void A_snapshot_confirming_the_cooldown_grays_immediately_without_waiting_out_the_grace()
    {
        long t0 = 1_000_000;
        DataManager dm = WithSelfAndBuff(t0);

        dm.SaveCooldown(JobBuffBase, 20_000, t0, actorId: Me, fromCast: true);
        dm.SaveCooldown(JobBuffBase, 19_800, t0 + 200, actorId: 0);

        Assert.True(OnCooldown(dm, t0 + 201));
    }

    [Fact]
    public void A_snapshot_on_its_own_is_authoritative_from_the_instant_it_lands()
    {
        // 0x3847 은 유예를 타지 않는다 — 기존 동작 그대로.
        long t0 = 1_000_000;
        DataManager dm = WithSelfAndBuff(t0);

        dm.SaveCooldown(JobBuffBase, 20_000, t0, actorId: 0);

        Assert.True(OnCooldown(dm, t0));
    }
}

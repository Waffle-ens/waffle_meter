using WaffleMeter.Capture;
using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>
/// The stat window's headline numbers are NOT on the wire. The server sends the terms and the client adds them
/// up, so the meter has to do the same arithmetic — and sending one term as if it were the total is worse than
/// sending nothing: the calculator's marginal efficiency is a finite difference, so a base that is ~28% of the
/// real one makes every point of attack look ~3.5x more valuable and inverts the whole option ranking.
///
/// <para>Every expectation below is a MEASURED pair — one character, one moment, meter beside the in-game stat
/// window (2026-08-31). They are goldens, not derivations: if the game changes how it composes these, these
/// tests are what notices.</para>
/// </summary>
public sealed class StatWindowDerivationTests
{
    /// <summary>The captured sheet for that character, at that moment.</summary>
    private static PlayerStatSheet Measured() => Sheet(
        (PlayerStatIds.Attack, 3778),
        (PlayerStatIds.AdditionalAttack, 1083),
        (PlayerStatIds.MinimumAttack, 973),
        (PlayerStatIds.MaximumAttack, 1563),
        (PlayerStatIds.AttackIncreasePercent, 12301),   // 123.01%
        (PlayerStatIds.Defense, 10666),
        (PlayerStatIds.ArmorDefense, 16393),
        (PlayerStatIds.DefenseIncreasePercent, 2400),   // 24%
        (PlayerStatIds.Accuracy, 1697),
        (PlayerStatIds.WeaponAccuracy, 391),
        (PlayerStatIds.AccuracyIncreasePercent, 5350),  // 53.5%
        (PlayerStatIds.Critical, 2278),
        (PlayerStatIds.CriticalIncreasePercent, 5950)); // 59.5%

    private static PlayerStatSheet Sheet(params (int Stat, int Value)[] stats)
    {
        var store = new PlayerStatStore();
        store.SetOwner(1, resetSheet: false);
        store.Accept(1, stats, fullSnapshot: true, arrivedAt: 1);
        return Assert.IsType<PlayerStatSheet>(store.Current);
    }

    [Fact]
    public void Attack_power_matches_the_stat_window()
    {
        // (기본 3,778 + 추가 1,083 + 무기 (973+1,563)/2) × 2.2301 = 13,668.28 → 스탯창 13,668
        Assert.Equal(13_668, Math.Round(Measured().AttackPower()!.Value), 0);
    }

    [Fact]
    public void Defense_matches_the_stat_window()
    {
        // (기본 10,666 + 방어구 16,393) × 1.24 = 33,553.16 → 스탯창 33,553
        Assert.Equal(33_553, Math.Round(Measured().DefensePower()!.Value), 0);
    }

    [Fact]
    public void Accuracy_matches_the_stat_window()
    {
        // (기본 1,697 + 무기 391) × 1.535 = 3,205.08 → 스탯창 3,205
        Assert.Equal(3_205, Math.Round(Measured().AccuracyTotal()!.Value), 0);
    }

    [Fact]
    public void Critical_matches_the_stat_window_and_has_no_weapon_term()
    {
        // 2,278 × 1.595 = 3,633.41 → 스탯창 3,633. 공격력·명중과 달리 무기 몫이 따로 없다.
        Assert.Equal(3_633, Math.Round(Measured().CriticalTotal()!.Value), 0);
    }

    [Fact]
    public void The_raw_attack_term_alone_is_nowhere_near_the_total()
    {
        // 이 격차가 이 파일이 존재하는 이유다 — 항 하나를 총합으로 보내면 계산기가 공격력을 실제의 28%로 본다.
        PlayerStatSheet sheet = Measured();
        Assert.Equal(3778, sheet.Raw(PlayerStatIds.Attack));
        Assert.True(sheet.AttackPower()!.Value > sheet.Raw(PlayerStatIds.Attack)! * 3.5);
    }

    [Fact]
    public void A_missing_headline_term_reports_null_rather_than_a_partial_total()
    {
        // 증가율이 없으면 0으로 두고 계산하지만(그 항이 없다는 뜻), 기준이 되는 항 자체가 없으면 합계를
        // 만들지 않는다 — 0을 돌려주면 "공격력 0"이라는 거짓말이 계산기로 넘어간다.
        PlayerStatSheet noAttack = Sheet((PlayerStatIds.Penetration, 1940));

        Assert.Null(noAttack.AttackPower());
        Assert.Null(noAttack.DefensePower());
        Assert.Null(noAttack.AccuracyTotal());
        Assert.Null(noAttack.CriticalTotal());
    }

    [Fact]
    public void An_absent_increase_rate_is_treated_as_no_increase()
    {
        PlayerStatSheet flat = Sheet((PlayerStatIds.Critical, 1000));

        Assert.Equal(1000, flat.CriticalTotal()!.Value, 6);
    }
}

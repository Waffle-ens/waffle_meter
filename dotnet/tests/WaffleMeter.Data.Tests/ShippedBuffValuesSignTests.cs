using System.IO;
using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>
/// Reads the SHIPPED <c>Assets/json/buff_values.json</c> and prices a few boss debuffs whose direction the table
/// must get right.
///
/// <para>The table keeps the client's sign: a '감소' row is negative. An earlier generator stored every '감소' as
/// its absolute value, so a debuff that LOWERS the boss's own 공격 속도·피해 증폭 was priced as if its caster had
/// handed the party that much — and no test read the shipped file, so nothing caught it. Re-exporting an
/// absolute-value table makes these fail.</para>
/// </summary>
public sealed class ShippedBuffValuesSignTests
{
    // 증폭·공격력 버킷이 빈 기준선이라 %p 가 곧 이득이다.
    private static readonly BuffGainContext Bare = new(
        AmpBucketPercent: 0,
        AttackIncreasePercent: 0,
        CritAmpPercent: 0,
        FrontAmpPercent: 0,
        CritRate: 0,
        SmiteRate: 0,
        PerfectRate: 0,
        FrontRate: 0,
        PerfectBonusRatio: 1.0);

    private static BuffValueCatalog Shipped()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null)
        {
            string path = Path.Combine(dir.FullName, "Assets", "json", "buff_values.json");
            if (File.Exists(path))
            {
                var catalog = new BuffValueCatalog();
                catalog.Load(ReferenceJson.LoadBuffValues(path));
                return catalog;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Assets/json/buff_values.json not found above " + AppContext.BaseDirectory);
    }

    /// <summary>A party member's debuff on the boss, up the whole fight.</summary>
    private static double OnBoss(BuffValueCatalog catalog, int code) => DpsMetrics.Gain(
        new MetricBuffInput(code, DataManager.BuffDisplayBase(code), 2, 100, 0, true), catalog, Bare);

    [Theory]
    [InlineData(100000111)] // 무기력: 공격 속도 10% 감소
    [InlineData(170800401)] // 약화의 낙인: PVE 피해 증폭 15% 감소
    [InlineData(180800401)] // 파동격: PVE 피해 증폭 15% 감소
    [InlineData(111400001)] // 위협의 포효: 피해 증폭 20% 감소
    public void A_debuff_that_weakens_the_boss_own_offense_is_not_a_party_gain(int code)
    {
        Assert.Equal(0.0, OnBoss(Shipped(), code), 9);
    }

    [Fact]
    public void A_debuff_that_strips_the_boss_resistance_is_everyones_gain()
    {
        BuffValueCatalog catalog = Shipped();

        Assert.Equal(0.15, OnBoss(catalog, 170700401), 9); // 고통의 연쇄: PVE 피해 내성 15% 감소
        // 위축: 공격력 20% 감소(보스 자신의 딜이라 0) + PVE 피해 내성 10% 감소
        Assert.Equal(0.10, OnBoss(catalog, 121200501), 9);
    }

    [Fact]
    public void A_defense_debuff_is_not_priced_on_the_resistance_scale()
    {
        // 방어 파괴: 방어력 20% 감소. 방어력은 공격력에서 빠지는 양이라 피해 내성 %p 로 읽으면 크기가 틀린다.
        Assert.Equal(0.0, OnBoss(Shipped(), 18041251), 9);
    }
}

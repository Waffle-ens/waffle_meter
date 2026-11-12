using System;
using System.IO;
using System.Linq;
using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>
/// 버프 오버레이의 쿨 회색은 "이 버프를 거는 스킬이 쿨이라 다시 못 건다"는 뜻이다. 2026-11-11 패치로 광분(검성)과
/// 투기(권성)는 base 스킬 말고도 여러 스킬이 같은 코드로 거는 효과 버프가 됐다 — base 쿨이 돌아도 다른 스킬로
/// 곧바로 다시 걸리니 회색이 거짓말이 된다. 이 둘만 회색 판정에서 빼고, 원천이 하나인 버프(감응)는 그대로 회색이며,
/// 스킬 쿨 칸(<see cref="DataManager.ActiveCooldowns"/>)은 영향받지 않는다.
/// </summary>
public sealed class CooldownVeilExclusionTests
{
    private const int Me = 700;

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

    private static DataManager WithSelfAndBuff(long t0, int buffCode, CooldownCatalog? catalog = null)
    {
        var dm = new DataManager { Clock = () => t0 };
        if (catalog != null)
        {
            dm.LoadCooldownCatalog(catalog);
        }

        dm.SaveNickname(Me, "본인", isExecutor: true, server: 3, jobByte: 0);
        dm.SaveUseBuff(Me, buffCode, t0, t0 + 60_000, 60_000, actorId: Me);
        return dm;
    }

    private static bool OnCooldown(DataManager dm, long atMs)
        => Assert.Single(dm.ActiveOwnerBuffs(atMs)).OnCooldown;

    [Theory]
    [InlineData(111000071, 11100000)] // 검성 광분 ← 파멸의 맹타 쿨
    [InlineData(190900001, 19090000)] // 권성 투기 ← 지면 강타 쿨
    public void A_multi_source_effect_buff_is_never_grayed(int buffCode, int skillBase)
    {
        long t0 = 1_000_000;
        DataManager dm = WithSelfAndBuff(t0, buffCode);

        dm.SaveCooldown(skillBase, 20_000, t0, actorId: 0); // 0x3847 스냅샷 — 즉시 확정

        Assert.False(OnCooldown(dm, t0 + 1_000));
    }

    [Fact]
    public void A_single_source_buff_still_grays_while_its_skill_cools()
    {
        // 대조군: 감응은 파열 화살 하나만 건다 — 쿨이면 정말로 다시 못 건다.
        long t0 = 1_000_000;
        DataManager dm = WithSelfAndBuff(t0, 140800071);

        dm.SaveCooldown(14080000, 20_000, t0, actorId: 0);

        Assert.True(OnCooldown(dm, t0 + 1_000));
    }

    [Fact]
    public void The_skill_cooldown_cell_still_shows_the_base_skill_cooling()
    {
        long t0 = 1_000_000;
        DataManager dm = WithSelfAndBuff(t0, 111000071, ShippedCatalog());

        dm.SaveCooldown(11100000, 20_000, t0, actorId: 0);

        Assert.False(OnCooldown(dm, t0 + 1_000));
        SkillCooldownView cell = Assert.Single(dm.ActiveCooldowns(t0 + 1_000), c => c.GroupId == 11100000);
        Assert.False(cell.IsReady);
        Assert.True(cell.RemainingMs > 0);
    }

    [Fact]
    public void A_specialized_variant_of_the_base_skill_does_not_gray_it_either()
    {
        // 출하 카탈로그로 특화 변종이 같은 그룹(11100000)으로 접히는 경로까지 확인한다.
        long t0 = 1_000_000;
        CooldownCatalog catalog = ShippedCatalog();
        Assert.Equal(11100000, catalog.GroupId(11100048));
        DataManager dm = WithSelfAndBuff(t0, 111000071, catalog);

        dm.SaveCooldown(11100048, 20_000, t0, actorId: 0);

        Assert.False(OnCooldown(dm, t0 + 1_000));
        Assert.Contains(dm.ActiveCooldowns(t0 + 1_000), c => c.GroupId == 11100000 && !c.IsReady);
    }
}

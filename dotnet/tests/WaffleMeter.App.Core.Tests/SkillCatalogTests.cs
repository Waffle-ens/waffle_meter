using WaffleMeter.App.Core;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

public class SkillCatalogTests
{
    [Fact]
    public void Maps_cover_all_skills_with_unique_codes()
    {
        Assert.Equal(167, SkillCatalog.Skills.Count); // 148 + 19 권성 (2026-07-01 신직업)
        Assert.Equal(SkillCatalog.Skills.Count, SkillCatalog.Skills.Select(s => s.Code).Distinct().Count());
        Assert.Equal(SkillCatalog.Skills.Count, SkillCatalog.DefaultVisibleCodes.Count);
    }

    [Fact]
    public void Every_job_has_tracked_skills_including_권성()
    {
        foreach (string job in SkillCatalog.JobPrefix.Keys)
        {
            Assert.True(SkillCatalog.Skills.Any(s => s.Job == job), $"no tracked skills for {job}");
        }

        // 권성 (the new job) must be populated with both normal + stigma skills.
        Assert.Equal(19, SkillCatalog.Skills.Count(s => s.Job == "권성"));
        Assert.Contains(SkillCatalog.Skills, s => s.Job == "권성" && s.IsStigma);
        Assert.Contains(SkillCatalog.Skills, s => s.Job == "권성" && !s.IsStigma);
    }

    [Fact]
    public void Get_and_metadata_resolve()
    {
        SkillMeta? m = SkillCatalog.Get(15210000); // 마도성 불꽃 화살
        Assert.NotNull(m);
        Assert.Equal("불꽃 화살", m!.Name);
        Assert.Equal("마도성", m.Job);
        Assert.False(m.IsStigma);
        Assert.Equal("불꽃 화살", SkillCatalog.GetName(15210000));
        Assert.Null(SkillCatalog.GetName(99999999));
    }

    [Fact]
    public void Order_follows_source_order()
    {
        // 소스에 적힌 순서 그대로. 코드를 박아 두면 카탈로그를 손볼 때마다 같이 썩으므로(2026-08-23 에
        // 죽은 패시브 19개를 액티브로 교체하면서 실제로 그렇게 됐다) 카탈로그 자신에게서 뽑는다.
        Assert.True(SkillCatalog.Order(SkillCatalog.Skills[0].Code) < SkillCatalog.Order(SkillCatalog.Skills[1].Code));
        Assert.Equal(999, SkillCatalog.Order(99999999)); // unknown -> tail
    }

    [Theory]
    [InlineData(15210000, 15210000)] // exact match
    [InlineData(15210042, 15210000)] // sub-code -> floor base
    [InlineData(99999999, 99999999)] // unknown -> self
    public void Normalize_maps_to_base_or_self(int input, int expected)
        => Assert.Equal(expected, SkillCatalog.Normalize(input));

    [Fact]
    public void GroupedByJob_splits_normal_and_stigma()
    {
        Assert.Equal(9, SkillCatalog.GroupedByJob.Count);
        GroupedJobSkills sorc = SkillCatalog.GroupedByJob.First(g => g.Job == "마도성");
        Assert.Contains(15210000, sorc.NormalSkills);  // 불꽃 화살 (normal)
        Assert.Contains(15360000, sorc.StigmaSkills);   // 신성 폭발 (stigma)
        Assert.DoesNotContain(15210000, sorc.StigmaSkills);
    }

    /// <summary>
    /// 일반(비스티그마) 항목은 전부 액티브여야 한다. 조인 패널 뱃지는 공식 홈 장착 정보에서만 오는데 그쪽은
    /// 패시브를 영원히 equip:0 으로 주므로, 여기 실린 패시브는 픽커에서 켜져 있어도 절대 뜨지 않는 칸이다.
    /// 2026-08-23 라이브 108명 표본에서 전 직업에 그런 항목이 있었고 권성은 6개 중 4개였다.
    ///
    /// 클라 데이터에 category 가 없어 코드 대역으로 판정한다: 실측상 x71xxxx~x80xxxx 가 패시브 대역이다.
    /// </summary>
    [Fact]
    public void Normal_skills_are_never_from_the_passive_band()
    {
        var passives = SkillCatalog.Skills
            .Where(s => !s.IsStigma && s.Code % 1_000_000 >= 700_000)
            .Select(s => $"{s.Code} {s.Job} {s.Name}")
            .ToArray();

        Assert.Empty(passives);
    }
}

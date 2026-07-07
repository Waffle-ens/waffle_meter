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
        // 검성 첫 스킬이 두 번째보다 앞.
        Assert.True(SkillCatalog.Order(11800000) < SkillCatalog.Order(11750000));
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
}

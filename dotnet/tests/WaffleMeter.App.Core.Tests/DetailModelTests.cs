using WaffleMeter.App.Core;
using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

public sealed class DetailModelTests
{
    private static AnalyzedSkill Skill(int code, string name, int dmg, int hits, int crit = 0, int dot = 0, int dotTimes = 0) =>
        new() { SkillCode = code, Name = name, DamageAmount = dmg, Times = hits, CritTimes = crit, DotDamageAmount = dot, DotTimes = dotTimes };

    private static DetailModel Compute(
        Dictionary<string, AnalyzedSkill> skills,
        List<OperatingData>? own = null,
        List<OperatingData>? boss = null,
        JobClass? job = JobClass.GLADIATOR,
        Func<int, string?>? names = null) =>
        DetailModel.Compute(skills, own ?? new(), boss ?? new(), uid: 1, job, contribution: 60.0, combatMs: 30000,
            names ?? (id => null));

    [Fact]
    public void Skips_zero_damage_and_emits_dot_row()
    {
        var model = Compute(new Dictionary<string, AnalyzedSkill>
        {
            ["99999"] = Skill(99999, "강타", dmg: 1000, hits: 10, crit: 5, dot: 200, dotTimes: 4),
            ["88888"] = Skill(88888, "무피해", dmg: 0, hits: 3),
        });

        // A non-chain skill's direct + DOT rows are separate singleton groups (React sorts each by
        // damage); the zero-damage skill is dropped.
        Assert.Equal(2, model.Skills.Count);
        DetailSkillGroup dotGroup = Assert.Single(model.Skills, g => g.Merged.IsDot);
        Assert.Equal("강타 - 지속", dotGroup.Merged.Name);
        Assert.Null(dotGroup.Merged.CritPct);
        Assert.Equal(1200, model.TotalDamage); // 1000 direct + 200 dot
    }

    [Fact]
    public void Per_skill_and_total_pcts()
    {
        var model = Compute(new Dictionary<string, AnalyzedSkill>
        {
            ["99999"] = Skill(99999, "강타", dmg: 1000, hits: 10, crit: 5),
        });

        DetailSkillRow direct = model.Skills[0].Children.Single(r => !r.IsDot);
        Assert.Equal(50, direct.CritPct);   // 5/10*100
        Assert.Equal(50.0, model.CritPct);  // total one-decimal
    }

    [Fact]
    public void Chain_group_merges_main_and_child()
    {
        var model = Compute(new Dictionary<string, AnalyzedSkill>
        {
            ["11020000"] = Skill(11020000, "절단", dmg: 1000, hits: 10, crit: 4),
            ["11030000"] = Skill(11030000, "연계", dmg: 500, hits: 5, crit: 1),
            ["99999"] = Skill(99999, "기타", dmg: 300, hits: 3),
        });

        Assert.Equal(2, model.Skills.Count); // one chain group + one singleton
        DetailSkillGroup chain = model.Skills[0]; // sorted by merged dmg desc -> the 1500 group
        Assert.True(chain.HasChildren);
        Assert.Equal(1500, chain.Merged.Damage);
        Assert.Equal(15, chain.Merged.Hits);
        Assert.Equal(2, chain.Children.Count);

        DetailSkillGroup singleton = model.Skills[1];
        Assert.False(singleton.HasChildren);
        Assert.Equal(300, singleton.Merged.Damage);
    }

    [Fact]
    public void Own_buffs_categorize_mine_party_other()
    {
        var own = new List<OperatingData>
        {
            new(110200500, "내버프", null, null, 95.5, 1),   // actor==uid, prefix 11 == GLADIATOR -> 내 버프
            new(150000000, "타직업버프", null, null, 40.0, 1), // actor==uid, prefix 15 -> 그 외
            new(110200500, "파티버프", null, null, 50.0, 2),   // actor 2 != uid -> 파티원 버프
        };

        DetailModel model = Compute(new(), own: own, job: JobClass.GLADIATOR);

        Assert.Equal(new[] { "내 버프", "파티원 버프", "그 외" },
            model.Buffs.Select(s => s.Label).OrderBy(l => l == "내 버프" ? 0 : l == "파티원 버프" ? 1 : 2).ToArray());
        Assert.Equal(95.5, model.Buffs.Single(s => s.Label == "내 버프").Rows[0].Rate);
    }

    [Fact]
    public void Boss_debuffs_group_by_actor_with_names()
    {
        var boss = new List<OperatingData>
        {
            new(990000000, "디버프A", null, null, 80.0, 1),
            new(990000001, "디버프B", null, null, 60.0, 2),
        };

        DetailModel model = Compute(new(), boss: boss, names: id => id == 1 ? "Hero" : null);

        Assert.Equal(2, model.Debuffs.Count);
        Assert.Contains(model.Debuffs, s => s.Label == "Hero");
        Assert.Contains(model.Debuffs, s => s.Label == "플레이어 2");
    }
}

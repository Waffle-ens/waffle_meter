using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>
/// Regression guard for the empty history-replay detail. A SAVED report carries Packets==null, so before
/// the fix <see cref="DpsCalculator.BattleDetails"/> could only rebuild from packets and returned an EMPTY
/// skill table for every replayed battle — which also zeroed 누적 피해량 + every hit-rate %, because the
/// detail's summary is derived from the skill rows. The saved report now carries a frozen
/// <see cref="DpsReport.SkillDetailsSnapshot"/> that BattleDetails prefers.
/// </summary>
public sealed class BattleDetailsSnapshotTests
{
    private static DpsCalculator NewCalc() => new(new DataManager());

    private static AnalyzedSkill Skill(int code, string name, int dmg, int hits) =>
        new() { SkillCode = code, Name = name, DamageAmount = dmg, Times = hits };

    private static DpsReport SavedReport(int uid, params AnalyzedSkill[] skills)
    {
        var byCode = skills.ToDictionary(s => s.SkillCode.ToString(), s => s);
        return new DpsReport
        {
            Packets = null, // saved/history reports never carry packets
            SkillDetailsSnapshot = new Dictionary<int, Dictionary<string, AnalyzedSkill>>
            {
                [uid] = byCode,
            },
        };
    }

    [Fact]
    public void Prefers_frozen_snapshot_when_packets_are_null()
    {
        DpsReport report = SavedReport(uid: 15485, Skill(11020000, "절단", dmg: 5000, hits: 12));

        Dictionary<string, AnalyzedSkill> result = NewCalc().BattleDetails(report, uid: 15485);

        AnalyzedSkill skill = Assert.Single(result).Value;
        Assert.Equal(5000, skill.DamageAmount);
        Assert.Equal(12, skill.Times);
        Assert.Equal("절단", skill.Name);
    }

    [Fact]
    public void Returns_empty_for_a_uid_absent_from_the_snapshot()
    {
        DpsReport report = SavedReport(uid: 15485, Skill(11020000, "절단", dmg: 5000, hits: 12));

        Assert.Empty(NewCalc().BattleDetails(report, uid: 99999));
    }

    [Fact]
    public void Snapshot_result_is_a_copy_and_does_not_mutate_the_report()
    {
        DpsReport report = SavedReport(uid: 1, Skill(11020000, "절단", dmg: 5000, hits: 12));

        Dictionary<string, AnalyzedSkill> first = NewCalc().BattleDetails(report, uid: 1);
        first["11020000"].DamageAmount = 999; // mutate the returned copy

        Dictionary<string, AnalyzedSkill> second = NewCalc().BattleDetails(report, uid: 1);
        Assert.Equal(5000, second["11020000"].DamageAmount); // snapshot untouched
    }

    [Fact]
    public void Empty_snapshot_with_null_packets_yields_no_skills()
    {
        // The pre-fix shape: a non-live report with no snapshot and no packets has nothing to rebuild from.
        var report = new DpsReport { Packets = null };

        Assert.Empty(NewCalc().BattleDetails(report, uid: 1));
    }
}

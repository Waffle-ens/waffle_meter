using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>
/// Locks the 어노멀 레벨 (buff level) plumbing: the wire already carries the caster's skill level for a buff
/// (<c>StreamProcessor.ReadAbnormalLevel</c>, self-validated 1..40), and it used to survive only inside the live
/// overlay store — where its single consumer was the exclusive-pair winner check. Everything downstream (uptime
/// rows, the combat-detail chip, the stats payload) got nothing.
///
/// <para>Why the level has to reach those places: a support buff's magnitude is LINEAR in it (노련한 반격 =
/// 5.4% + 0.4%/level, 불패의 진언 = 10.5% + 0.5%/level), so uptime alone cannot say how much a buffer
/// contributed. The buff CODE only carries the 5-step rank, not the level — a 검성 at level 22 and one at 25
/// both broadcast 117800072 — so the code can never substitute for it.</para>
/// </summary>
public sealed class BuffLevelPropagationTests
{
    private const long Start = 0L;
    private const long End = 1000L;

    private static Buff Catalogued(int code, string name) => new(code, name, "요약", "효과");

    [Fact]
    public void Saved_buff_keeps_the_level_it_arrived_with()
    {
        var dm = new DataManager();
        dm.SaveUseBuff(uid: 9, skillCode: 117800072, buffStart: 0, buffEnd: 500, duration: 500, actorId: 1, level: 22);

        UseBuff saved = Assert.Single(dm.BattleBuff(9, Start, End));
        Assert.Equal(22, saved.Level);
    }

    [Fact]
    public void Level_defaults_to_zero_when_the_wire_did_not_give_one()
    {
        // Consumables/scrolls sit outside the 11..19 job-skill band, so ReadAbnormalLevel declines and reports 0.
        // 0 must stay 0 — it means "unknown", and the display layer draws no badge rather than "Lv.0".
        var dm = new DataManager();
        dm.SaveUseBuff(uid: 9, skillCode: 22101031, buffStart: 0, buffEnd: 500, duration: 500, actorId: 9);

        Assert.Equal(0, Assert.Single(dm.BattleBuff(9, Start, End)).Level);
    }

    [Fact]
    public void Uptime_row_reports_the_highest_level_among_the_applications_it_merged()
    {
        // One row = (base, name, caster), so every application in it is the same person casting the same skill —
        // and a skill level does not change mid-fight. The max is therefore the caster's level, and it must not be
        // dragged down by an application whose tail failed self-validation (level 0 = unknown, not "level zero").
        var dm = new DataManager();
        dm.LoadBuffs([Catalogued(117800071, "노련한 반격"), Catalogued(117800072, "노련한 반격")]);

        dm.SaveUseBuff(9, 117800071, 0, 400, 400, actorId: 1, level: 0);
        dm.SaveUseBuff(9, 117800072, 200, 600, 400, actorId: 1, level: 22);

        OperatingData row = Assert.Single(new DpsCalculator(dm).GetBuffOperatingRate(9, Start, End));

        Assert.Equal(22, row.Level);
        Assert.Equal(11780000, row.BaseCode);
    }

    [Fact]
    public void Two_casters_of_the_same_buff_keep_their_own_levels()
    {
        // 8인 공대 has two 검성 at different skill levels. The grouping key already splits them by caster, so each
        // row must report ITS caster's level — collapsing to one number would mis-credit both.
        var dm = new DataManager();
        dm.LoadBuffs([Catalogued(117800072, "노련한 반격")]);

        dm.SaveUseBuff(9, 117800072, 0, 500, 500, actorId: 1, level: 22);
        dm.SaveUseBuff(9, 117800072, 0, 500, 500, actorId: 2, level: 25);

        List<OperatingData> rows = new DpsCalculator(dm).GetBuffOperatingRate(9, Start, End);

        Assert.Equal(2, rows.Count);
        Assert.Equal(22, Assert.Single(rows, r => r.ActorId == 1).Level);
        Assert.Equal(25, Assert.Single(rows, r => r.ActorId == 2).Level);
    }

    [Fact]
    public void Buff_timeline_carries_the_same_level_as_the_uptime_row()
    {
        // The DPS-graph lane and the uptime tab are built from the same groups; a level that appeared in one and
        // not the other would read as two different facts about one buff.
        var dm = new DataManager();
        dm.LoadBuffs([Catalogued(181900511, "불패의 진언")]);
        dm.SaveUseBuff(9, 181900511, 0, 800, 800, actorId: 3, level: 25);

        var calc = new DpsCalculator(dm);
        OperatingData rate = Assert.Single(calc.GetBuffOperatingRate(9, Start, End));
        BuffTimeline lane = Assert.Single(calc.GetBuffIntervals(9, Start, End));

        Assert.Equal(25, rate.Level);
        Assert.Equal(rate.Level, lane.Level);
    }
}

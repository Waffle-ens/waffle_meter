using System.Collections.Generic;
using System.Linq;
using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>
/// Locks the buff-uptime grouping key: one row per (base skill, name, caster) on one entity.
///
/// A skill fires several codes at once — the buff it puts on the caster and the debuff it puts on the target —
/// and the entity a row landed on already tells buff from debuff. What must NOT be used as a key:
///   - the runtime code : 격노 폭발 puts 113900071 + 113900072 on its target, so each rank became its own row
///   - the name alone   : 지연 피해 is 5 unrelated skills across 5 bases that share a display name
///   - the base alone   : base 16300000 holds 4원소, 피해 내성 감소 and 지연 피해 under different names
/// </summary>
public sealed class BuffOperatingRateGroupingTests
{
    private const long Start = 0L;
    private const long End = 1000L;

    private static Buff Catalogued(int code, string name) => new(code, name, "요약", "효과");

    private static void Apply(DataManager dm, int uid, int code, long start, long end, int actorId) =>
        dm.SaveUseBuff(uid, code, start, end, end - start, actorId);

    [Fact]
    public void Rank_variants_of_one_skill_merge_into_one_row_with_unioned_uptime()
    {
        var dm = new DataManager();
        dm.LoadBuffs([Catalogued(113900071, "격노 폭발"), Catalogued(113900072, "격노 폭발")]);

        // Overlapping applications of the two ranks cover [0,600] = 60% — not 40% + 40%, and not two rows.
        Apply(dm, uid: 9, code: 113900071, start: 0, end: 400, actorId: 1);
        Apply(dm, uid: 9, code: 113900072, start: 200, end: 600, actorId: 1);

        OperatingData row = Assert.Single(new DpsCalculator(dm).GetBuffOperatingRate(9, Start, End));

        Assert.Equal("격노 폭발", row.Name);
        Assert.Equal(11390000, row.BaseCode);
        Assert.Equal(60.0, row.OperatingRate, 3);
    }

    [Fact]
    public void Everything_one_skill_put_on_one_entity_is_a_single_row()
    {
        // 살기 파열 (검성) fires a stack buff (118000071) and two 치명타 피해 내성 감소 codes (118000081/091), and the
        // live capture shows all three landing on the caster. They are one skill's uptime, not three.
        var dm = new DataManager();
        dm.LoadBuffs([
            Catalogued(118000071, "살기 파열"),
            Catalogued(118000081, "살기 파열"),
            Catalogued(118000091, "살기 파열"),
        ]);

        Apply(dm, uid: 9, code: 118000071, start: 0, end: 998, actorId: 9);
        Apply(dm, uid: 9, code: 118000081, start: 0, end: 812, actorId: 9);
        Apply(dm, uid: 9, code: 118000091, start: 100, end: 900, actorId: 9);

        OperatingData row = Assert.Single(new DpsCalculator(dm).GetBuffOperatingRate(9, Start, End));

        Assert.Equal(11800000, row.BaseCode);
        Assert.Equal(99.8, row.OperatingRate, 3);
    }

    [Fact]
    public void The_same_skill_on_the_caster_and_on_the_target_stays_two_rows()
    {
        // 격노 폭발: "대상에게 공격력 10% 감소, 자신에게 PVE 피해 증폭 10% 증가". The caster's buff (113900481) and the
        // target's debuff (113900071) share a base and a name; only the entity separates them.
        var dm = new DataManager();
        dm.LoadBuffs([Catalogued(113900071, "격노 폭발"), Catalogued(113900481, "격노 폭발")]);

        Apply(dm, uid: 9, code: 113900481, start: 0, end: 661, actorId: 9);   // on the 검성
        Apply(dm, uid: 500, code: 113900071, start: 0, end: 655, actorId: 9); // on the boss

        var calc = new DpsCalculator(dm);

        Assert.Equal(66.1, Assert.Single(calc.GetBuffOperatingRate(9, Start, End)).OperatingRate, 3);
        Assert.Equal(65.5, Assert.Single(calc.GetBuffOperatingRate(500, Start, End)).OperatingRate, 3);
    }

    [Fact]
    public void Same_name_under_different_bases_stays_separate()
    {
        // 정령성 casts two distinct 지연 피해 skills (bases 16300000 and 16330000). Name-keyed dedup would collapse
        // them into one row and lose half the uptime.
        var dm = new DataManager();
        dm.LoadBuffs([Catalogued(163000008, "지연 피해"), Catalogued(163300201, "지연 피해")]);

        Apply(dm, uid: 9, code: 163000008, start: 0, end: 569, actorId: 1);
        Apply(dm, uid: 9, code: 163300201, start: 0, end: 473, actorId: 1);

        List<OperatingData> rows = new DpsCalculator(dm).GetBuffOperatingRate(9, Start, End);

        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { 16300000, 16330000 }, rows.Select(r => r.BaseCode).OrderBy(b => b).ToArray());
    }

    [Fact]
    public void Different_names_under_one_base_stay_separate()
    {
        // base 16300000 carries 4원소 and 원소 as distinct effects. Base-keyed dedup would merge them.
        var dm = new DataManager();
        dm.LoadBuffs([Catalogued(163000002, "4원소"), Catalogued(163000003, "원소")]);

        Apply(dm, uid: 9, code: 163000002, start: 0, end: 878, actorId: 9);
        Apply(dm, uid: 9, code: 163000003, start: 0, end: 500, actorId: 9);

        List<OperatingData> rows = new DpsCalculator(dm).GetBuffOperatingRate(9, Start, End);

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(16300000, r.BaseCode));
        Assert.Equal(new[] { "4원소", "원소" }, rows.Select(r => r.Name).OrderBy(n => n).ToArray());
    }

    [Fact]
    public void Same_buff_from_two_casters_stays_separate()
    {
        var dm = new DataManager();
        dm.LoadBuffs([Catalogued(181600011, "질주의 진언")]);

        Apply(dm, uid: 9, code: 181600011, start: 0, end: 500, actorId: 4);
        Apply(dm, uid: 9, code: 181600011, start: 500, end: 800, actorId: 5);

        List<OperatingData> rows = new DpsCalculator(dm).GetBuffOperatingRate(9, Start, End);

        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { 4, 5 }, rows.Select(r => r.ActorId).OrderBy(a => a).ToArray());
    }

    [Fact]
    public void An_uncatalogued_rank_merges_and_the_row_keeps_a_catalogued_representative()
    {
        // 113900073 isn't in buff.json; it resolves only to the skill NAME via base 11390000. It must join the
        // catalogued ranks, and the row must keep a catalogued code so icon / buff-value lookups still resolve.
        var dm = new DataManager();
        dm.LoadBuffs([Catalogued(113900071, "격노 폭발"), Catalogued(113900072, "격노 폭발")]);
        dm.LoadSkills([new Skill(11390000, "격노 폭발")]);

        Apply(dm, uid: 9, code: 113900071, start: 0, end: 443, actorId: 1);
        Apply(dm, uid: 9, code: 113900072, start: 0, end: 442, actorId: 1);
        Apply(dm, uid: 9, code: 113900073, start: 900, end: 961, actorId: 1);

        OperatingData row = Assert.Single(new DpsCalculator(dm).GetBuffOperatingRate(9, Start, End));

        Assert.Equal(113900071, row.Code); // catalogued, not the 8-digit base 11390000
        Assert.Equal(50.4, row.OperatingRate, 3); // [0,443] ∪ [900,961]
    }

    [Fact]
    public void JobPrefix_comes_from_the_raw_code_so_a_fallback_row_is_still_a_self_buff()
    {
        // The fallback path reports the 8-digit base as the display code. 11800000 / 10_000_000 == 1, so reading
        // the prefix off the display code would classify a 검성 self-buff as 그 외 and the stats web would drop it.
        var dm = new DataManager();
        dm.LoadSkills([new Skill(11800000, "살기 파열")]);

        Apply(dm, uid: 9, code: 118000099, start: 0, end: 500, actorId: 9);

        OperatingData row = Assert.Single(new DpsCalculator(dm).GetBuffOperatingRate(9, Start, End));

        Assert.Equal(11800000, row.Code);
        Assert.Equal(11, row.JobPrefix);
        Assert.Equal(11, row.EffectiveJobPrefix);
    }

    [Fact]
    public void An_eight_digit_mob_debuff_carries_no_job_prefix()
    {
        // 12000101(중독) is a mob effect, not a 수호성 skill. Deriving a prefix from its leading digits would make
        // it look like that player's own class buff.
        var dm = new DataManager();
        dm.LoadBuffs([Catalogued(12000101, "중독")]);

        Apply(dm, uid: 9, code: 12000101, start: 0, end: 500, actorId: 9);

        OperatingData row = Assert.Single(new DpsCalculator(dm).GetBuffOperatingRate(9, Start, End));

        Assert.Equal(0, row.JobPrefix);
        Assert.Equal(0, row.EffectiveJobPrefix);
        Assert.Equal(12000101, row.BaseCode);
    }

    [Fact]
    public void Custom_override_replaces_the_display_text_of_a_catalogued_code()
    {
        var dm = new DataManager();
        dm.LoadBuffs([Catalogued(181600011, "질주의 진언")]);
        dm.LoadBuffs([new Buff(181600011, "질주의 진언", "새 요약", "새 효과")]);

        Buff stored = Assert.IsType<Buff>(dm.Buff(181600011));

        Assert.Equal("새 요약", stored.Summary);
    }
}

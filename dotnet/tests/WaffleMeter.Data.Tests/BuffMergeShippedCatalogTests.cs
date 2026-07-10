using System.IO;
using System.Linq;
using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>
/// Replays the reported symptoms against the SHIPPED catalog (Assets/json/buff.json + buff_custom.json +
/// skills.json) rather than a synthetic one, so a datamine drop that renames or re-codes these skills fails
/// here instead of silently un-fixing the buff-uptime view.
///
/// Each case is anchored to an in-game tooltip or to a live packet capture (2026-07-08 / 2026-07-09).
/// </summary>
public sealed class BuffMergeShippedCatalogTests
{
    private const long Start = 0L;
    private const long End = 1000L;

    private static string FindAssetsJsonDir()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "Assets", "json");
            if (File.Exists(Path.Combine(candidate, "buff.json")))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Assets/json/buff.json not found above " + AppContext.BaseDirectory);
    }

    private static DataManager ShippedCatalog()
    {
        string jsonDir = FindAssetsJsonDir();
        var dm = new DataManager();
        foreach (string file in new[] { "buff.json", "buff_custom.json" })
        {
            dm.LoadBuffs(ReferenceJson.LoadBuffs(Path.Combine(jsonDir, file)));
        }

        dm.LoadSkills(ReferenceJson.LoadSkills(Path.Combine(jsonDir, "skills.json")));
        return dm;
    }

    private static void Apply(DataManager dm, int uid, int code, long start, long end, int actorId) =>
        dm.SaveUseBuff(uid, code, start, end, end - start, actorId);

    [Fact]
    public void The_three_격노_폭발_rows_on_the_boss_collapse_to_one()
    {
        // The web showed 격노 폭발 at 44.3% / 44.2% / 6.1% on one boss from one caster: the two catalogued ranks
        // plus an uncatalogued one that only resolved through skills.json to base 11390000.
        DataManager dm = ShippedCatalog();
        Apply(dm, uid: 500, code: 113900071, start: 0, end: 443, actorId: 1);
        Apply(dm, uid: 500, code: 113900072, start: 0, end: 442, actorId: 1);
        Apply(dm, uid: 500, code: 113900073, start: 900, end: 961, actorId: 1);

        OperatingData row = Assert.Single(new DpsCalculator(dm).GetBuffOperatingRate(500, Start, End));

        Assert.Equal("격노 폭발", row.Name);
        Assert.Equal(11390000, row.BaseCode);
        Assert.Equal(113900071, row.Code);        // catalogued representative, not the 8-digit base
        Assert.Equal(50.4, row.OperatingRate, 3); // [0,443] ∪ [900,961]
    }

    [Fact]
    public void 격노_폭발_stays_two_rows_because_the_caster_gets_a_buff_and_the_target_a_debuff()
    {
        // Tooltip: "적중 시 대상에게 10초 동안 공격력을 10% 감소시키고, 10초 동안 자신의 PVE 피해 증폭이 10% 증가".
        // Live capture: 113900071/072 land on the target (352 + 1,148 events), 113900481 on the caster (1,016).
        // Only the entity separates them — they share base 11390000 and the name 격노 폭발.
        DataManager dm = ShippedCatalog();
        Assert.NotNull(dm.Buff(113900071));
        Assert.Null(dm.Buff(113900481)); // uncatalogued: resolves to the skill name via base 11390000

        Apply(dm, uid: 9, code: 113900481, start: 0, end: 661, actorId: 9);
        Apply(dm, uid: 500, code: 113900071, start: 0, end: 655, actorId: 9);

        var calc = new DpsCalculator(dm);
        OperatingData onCaster = Assert.Single(calc.GetBuffOperatingRate(9, Start, End));
        OperatingData onBoss = Assert.Single(calc.GetBuffOperatingRate(500, Start, End));

        Assert.Equal(66.1, onCaster.OperatingRate, 3);
        Assert.Equal(65.5, onBoss.OperatingRate, 3);
        Assert.Equal(11390000, onCaster.BaseCode);
        Assert.Equal(11390000, onBoss.BaseCode);
    }

    [Fact]
    public void The_two_살기_파열_rows_in_the_buff_tab_collapse_to_one()
    {
        // The web showed 살기 파열 twice (99.8% / 81.2%). Live capture: the stack buff 118000071 (29,038 events) and
        // the 치명타 피해 내성 감소 codes 118000081/091 (502 + 1,255) ALL land on the caster. One skill, one row.
        DataManager dm = ShippedCatalog();
        Apply(dm, uid: 9, code: 118000071, start: 0, end: 998, actorId: 9);
        Apply(dm, uid: 9, code: 118000081, start: 0, end: 812, actorId: 9);
        Apply(dm, uid: 9, code: 118000091, start: 100, end: 900, actorId: 9);

        OperatingData row = Assert.Single(new DpsCalculator(dm).GetBuffOperatingRate(9, Start, End));

        Assert.Equal("살기 파열", row.Name);
        Assert.Equal(11800000, row.BaseCode);
        Assert.Equal(99.8, row.OperatingRate, 3);
    }

    [Fact]
    public void 정령성_지연_피해_keeps_its_two_rows()
    {
        // Two DIFFERENT 정령성 skills share the name 지연 피해 (bases 16300000 and 16330000). Name-keyed dedup would
        // silently merge them; the web's two rows (56.9% / 47.3%) were correct all along.
        DataManager dm = ShippedCatalog();
        Assert.Equal("지연 피해", dm.Buff(163000008)!.Name);
        Assert.Equal("지연 피해", dm.Buff(163300201)!.Name);

        Apply(dm, uid: 500, code: 163000008, start: 0, end: 569, actorId: 1);
        Apply(dm, uid: 500, code: 163300201, start: 0, end: 473, actorId: 1);

        var rows = new DpsCalculator(dm).GetBuffOperatingRate(500, Start, End);

        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { 16300000, 16330000 }, rows.Select(r => r.BaseCode).OrderBy(b => b).ToArray());
    }

    [Fact]
    public void Differently_named_effects_under_base_16300000_stay_separate()
    {
        // 4원소(163000002) and 피해 내성 감소(163000007) share base 16300000. Base-keyed dedup would merge them.
        DataManager dm = ShippedCatalog();
        Assert.Equal("4원소", dm.Buff(163000002)!.Name);
        Assert.Equal("피해 내성 감소", dm.Buff(163000007)!.Name);

        Apply(dm, uid: 9, code: 163000002, start: 0, end: 878, actorId: 9);
        Apply(dm, uid: 9, code: 163000007, start: 0, end: 970, actorId: 9);

        var rows = new DpsCalculator(dm).GetBuffOperatingRate(9, Start, End);

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(16300000, r.BaseCode));
    }

    [Fact]
    public void Five_ranks_of_one_skill_report_as_a_single_row()
    {
        // 마도성's 지연 피해 ships five rank codes under base 15320000.
        DataManager dm = ShippedCatalog();
        int[] ranks = [153200001, 153200101, 153200201, 153200301, 153200401];
        Assert.All(ranks, c => Assert.Equal("지연 피해", dm.Buff(c)!.Name));

        for (int i = 0; i < ranks.Length; i++)
        {
            Apply(dm, uid: 500, code: ranks[i], start: i * 100, end: i * 100 + 150, actorId: 1); // covers [0,550]
        }

        OperatingData row = Assert.Single(new DpsCalculator(dm).GetBuffOperatingRate(500, Start, End));

        Assert.Equal(15320000, row.BaseCode);
        Assert.Equal(55.0, row.OperatingRate, 3); // union, not 5 × 15% and not five rows
    }

    [Fact]
    public void An_uncatalogued_rank_merges_with_its_catalogued_sibling()
    {
        // Live capture: 격앙 arrives as 127800011 (catalogued) and 127800012 (not). One buff, one row.
        DataManager dm = ShippedCatalog();
        Assert.NotNull(dm.Buff(127800011));
        Assert.Null(dm.Buff(127800012));

        Apply(dm, uid: 9, code: 127800012, start: 0, end: 385, actorId: 7);
        Apply(dm, uid: 9, code: 127800011, start: 900, end: 967, actorId: 7);

        OperatingData row = Assert.Single(new DpsCalculator(dm).GetBuffOperatingRate(9, Start, End));

        Assert.Equal("격앙", row.Name);
        Assert.Equal(12780000, row.BaseCode);
        Assert.Equal(127800011, row.Code);
        Assert.Equal(45.2, row.OperatingRate, 3);
    }

    [Fact]
    public void Buff_custom_overrides_the_display_text_of_the_shipped_row()
    {
        Buff buff = Assert.IsType<Buff>(ShippedCatalog().Buff(181600011));

        Assert.Equal("질주의 진언", buff.Name);
        Assert.Contains("이동 속도", buff.Summary); // buff_custom.json's text won
    }
}

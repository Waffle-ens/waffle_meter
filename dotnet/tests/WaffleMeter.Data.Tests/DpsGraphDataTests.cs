using WaffleMeter.Capture;
using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>
/// Covers the combat-detail DPS graph's data sources: the per-second damage series
/// (<see cref="DpsCalculator.GetDpsSeries"/>), the buff timeline (<see cref="DpsCalculator.GetBuffIntervals"/>,
/// which mirrors <see cref="DpsCalculator.GetBuffOperatingRate"/> but keeps the merged spans), and — most
/// important — that BOTH are frozen onto a SAVED report so a history-replayed graph is not empty (the same
/// freeze-regression class as <c>BattleDetailsSnapshotTests</c>).
/// </summary>
public sealed class DpsGraphDataTests
{
    private const int Instance = 100;
    private const int BossCode = 2301008;
    private const int Dealer = 5001;

    private static (DataManager Dm, DpsCalculator Calc, long[] Clock) Boss()
    {
        long[] now = { 1_000_000 };
        var dm = new DataManager { Clock = () => now[0] };
        dm.LoadMobs(new Dictionary<int, Mob> { [BossCode] = new Mob(BossCode, "보스", Boss: true) });
        dm.SaveMobId(Instance, BossCode);
        dm.SaveNickname(Dealer, "딜러", isExecutor: true, server: 2003, jobByte: 32);
        return (dm, new DpsCalculator(dm), now);
    }

    private static void Hit(DataManager dm, long timestamp, int damage) =>
        dm.SaveDamage(
            new ParsedDamagePacket { ActorId = Dealer, TargetId = Instance, Damage = damage, Timestamp = timestamp },
            dm.CurrentEpoch());

    private static Buff Cat(int code, string name) => new(code, name, "요약", "효과");

    private static void Apply(DataManager dm, int uid, int code, long start, long end, int actorId) =>
        dm.SaveUseBuff(uid, code, start, end, end - start, actorId);

    // ── GetDpsSeries ─────────────────────────────────────────────────────────────────────────────────────
    [Fact]
    public void GetDpsSeries_buckets_damage_into_per_second_offsets_from_battle_start()
    {
        (DataManager dm, DpsCalculator calc, long[] clock) = Boss();
        long toggle = clock[0];
        dm.MobHp(Instance, 5000);
        dm.StartBattle(Instance);
        Hit(dm, toggle + 1_000, 3000);   // second 0 (== BattleStart)
        Hit(dm, toggle + 2_000, 4000);   // second 1
        Hit(dm, toggle + 2_500, 1000);   // second 1 (same whole-second bucket → summed)
        Hit(dm, toggle + 5_000, 2000);   // second 4

        DpsReport live = calc.GetDps();
        long[] series = calc.GetDpsSeries(Dealer, live.BattleStart, live.BattleEnd);

        Assert.True(series.Length >= 5);
        Assert.Equal(3000L, series[0]);
        Assert.Equal(5000L, series[1]);   // 4000 + 1000 folded into the same second
        Assert.Equal(0L, series[2]);
        Assert.Equal(0L, series[3]);
        Assert.Equal(2000L, series[4]);
        Assert.Equal(10_000L, series.Sum()); // the series conserves total damage (nothing leaked to other buckets)
    }

    [Fact]
    public void GetDpsSeries_is_empty_for_an_unknown_entity_or_nonpositive_window()
    {
        (DataManager dm, DpsCalculator calc, long[] clock) = Boss();
        long toggle = clock[0];
        dm.MobHp(Instance, 5000);
        dm.StartBattle(Instance);
        Hit(dm, toggle + 1_000, 3000);
        DpsReport live = calc.GetDps();

        Assert.Empty(calc.GetDpsSeries(999_999, live.BattleStart, live.BattleEnd)); // no such uid
        Assert.Empty(calc.GetDpsSeries(Dealer, live.BattleStart, live.BattleStart)); // zero-length window
    }

    // ── GetBuffIntervals (mirrors GetBuffOperatingRate) ──────────────────────────────────────────────────
    [Fact]
    public void GetBuffIntervals_merges_overlapping_ranks_into_one_row_with_unioned_spans()
    {
        var dm = new DataManager();
        dm.LoadBuffs([Cat(113900071, "격노 폭발"), Cat(113900072, "격노 폭발")]);
        Apply(dm, uid: 9, code: 113900071, start: 0, end: 400, actorId: 1);
        Apply(dm, uid: 9, code: 113900072, start: 200, end: 600, actorId: 1);

        BuffTimeline row = Assert.Single(new DpsCalculator(dm).GetBuffIntervals(9, 0, 1000));

        Assert.Equal("격노 폭발", row.Name);
        Assert.Equal(11390000, row.BaseCode);
        Assert.Equal(new List<(long, long)> { (0L, 600L) }, row.Spans); // union, not two rows / not [0,400]+[200,600]
    }

    [Fact]
    public void GetBuffIntervals_span_coverage_matches_GetBuffOperatingRate()
    {
        var dm = new DataManager();
        dm.LoadBuffs([Cat(118000071, "살기 파열"), Cat(118000081, "살기 파열"), Cat(118000091, "살기 파열")]);
        Apply(dm, uid: 9, code: 118000071, start: 0, end: 998, actorId: 9);
        Apply(dm, uid: 9, code: 118000081, start: 0, end: 812, actorId: 9);
        Apply(dm, uid: 9, code: 118000091, start: 100, end: 900, actorId: 9);

        var calc = new DpsCalculator(dm);
        OperatingData rate = Assert.Single(calc.GetBuffOperatingRate(9, 0, 1000));
        BuffTimeline timeline = Assert.Single(calc.GetBuffIntervals(9, 0, 1000));

        long covered = timeline.Spans.Sum(s => s.End - s.Start);
        Assert.Equal(998L, covered);                                    // [0,998] union
        Assert.Equal(rate.OperatingRate, covered / 1000.0 * 100.0, 3);  // same coverage the uptime tab shows
        Assert.Equal(rate.Code, timeline.Code);                          // same representative → same icon
    }

    [Fact]
    public void GetBuffIntervals_returns_empty_for_a_nonpositive_window()
    {
        var dm = new DataManager();
        dm.LoadBuffs([Cat(113900071, "격노 폭발")]);
        Apply(dm, uid: 9, code: 113900071, start: 0, end: 400, actorId: 1);

        Assert.Empty(new DpsCalculator(dm).GetBuffIntervals(9, 100, 100));
    }

    // ── Freeze regression (the important one) ────────────────────────────────────────────────────────────
    [Fact]
    public void Saved_report_freezes_the_dps_series_and_buff_timeline_for_history_replay()
    {
        (DataManager dm, DpsCalculator calc, long[] clock) = Boss();
        dm.LoadBuffs([Cat(118000071, "살기 파열")]);
        DpsLog? saved = null;
        calc.OnBattleLogged = l => saved = l;

        long toggle = clock[0];
        dm.MobHp(Instance, 5000);
        dm.StartBattle(Instance);
        Hit(dm, toggle + 1_000, 3000);
        Hit(dm, toggle + 2_000, 4000);
        Hit(dm, toggle + 3_000, 3000);
        // A self-buff on the dealer for part of the fight — captured BEFORE the save-time buff prune.
        dm.SaveUseBuff(Dealer, 118000071, toggle + 1_000, toggle + 2_500, 1500, Dealer);
        calc.GetDps();                 // live tick accumulates damage + registers the target

        clock[0] = toggle + 4_000;
        dm.EndBattle(Instance);
        calc.GetDps();                 // end transition → SaveRecentBattleLog → OnBattleLogged

        Assert.NotNull(saved);

        // The per-second series is frozen (Packets are null on a saved report) and conserves total damage.
        Assert.True(saved!.Report.DpsSeries.TryGetValue(Dealer, out long[]? frozenSeries));
        Assert.Equal(10_000L, frozenSeries!.Sum());
        Assert.Null(saved.Report.Packets);

        // The buff timeline is frozen too (built before UseBuffRepository.PruneBefore ran inside SaveBattleLog).
        Assert.True(saved.Report.BuffIntervals.TryGetValue(Dealer, out List<BuffTimeline>? frozenBuffs));
        BuffTimeline buff = Assert.Single(frozenBuffs!);
        Assert.Equal("살기 파열", buff.Name);
        Assert.Equal(1500L, buff.Spans.Sum(s => s.End - s.Start));
    }
}

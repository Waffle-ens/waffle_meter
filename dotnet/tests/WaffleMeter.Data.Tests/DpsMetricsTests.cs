using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>
/// The nDPS/rDPS model. Its shape is deliberately the stats site's (<c>src/shared/dps-metrics.ts</c>) so the
/// two cannot drift into different definitions of the same word; what the meter adds is the caster's SKILL
/// LEVEL, which the site's snapshot table has no room for.
/// </summary>
public sealed class DpsMetricsTests
{
    private const double Duration = 100.0;

    private static BuffValueCatalog EmptyCatalog() => new();

    private static BuffValueCatalog CatalogWith(params (int Code, BuffGainCategory Category, double Value)[] rows)
    {
        var catalog = new BuffValueCatalog();
        catalog.Load(rows.Select(r =>
            (r.Code, (IReadOnlyList<BuffGainEffect>)[new BuffGainEffect(r.Category, r.Value)])));
        return catalog;
    }

    private static MetricBuffInput Buff(
        int displayBase, int actorId, double rate, int level = 0, int? code = null, bool boss = false) =>
        new(code ?? displayBase, displayBase, actorId, rate, level, boss);

    private static MetricParticipantInput Player(
        int uid, double dps, IReadOnlyList<MetricBuffInput>? buffs = null,
        IReadOnlyDictionary<int, long>? granted = null) =>
        new(uid, dps, dps * Duration, buffs ?? [], granted ?? new Dictionary<int, long>());

    [Fact]
    public void Without_external_buffs_ndps_and_rdps_are_just_dps()
    {
        Dictionary<int, DpsMetricResult> r = DpsMetrics.Compute(
            [Player(1, 1000)], [], EmptyCatalog(), Duration);

        Assert.Equal(1000, r[1].Ndps, 6);
        Assert.Equal(1000, r[1].Rdps, 6);
        Assert.Equal(0, r[1].GivenBuffDps, 6);
        Assert.Equal(0, r[1].TakenBuffDps, 6);
    }

    [Fact]
    public void A_players_own_buff_is_their_own_play_and_is_not_stripped()
    {
        // Self buffs must not be normalized away: they are the player's rotation, not something lent to them.
        var self = Buff(PartySynergyCatalog.SwordCounter, actorId: 1, rate: 100, level: 25);

        Dictionary<int, DpsMetricResult> r = DpsMetrics.Compute(
            [Player(1, 1000, [self])], [], EmptyCatalog(), Duration);

        Assert.Equal(1000, r[1].Ndps, 6);
    }

    [Fact]
    public void A_party_buff_is_divided_out_of_the_recipient_and_credited_to_its_caster()
    {
        // 노련한 반격 at level 25 = 5.4 + 0.4×24 = 15.0% PvE amp, at 100% uptime → gain 0.15.
        var fromTwo = Buff(PartySynergyCatalog.SwordCounter, actorId: 2, rate: 100, level: 25);

        Dictionary<int, DpsMetricResult> r = DpsMetrics.Compute(
            [Player(1, 1150, [fromTwo]), Player(2, 500)], [], EmptyCatalog(), Duration);

        Assert.Equal(1150 / 1.15, r[1].Ndps, 6);
        Assert.Equal(1150 - 1150 / 1.15, r[1].TakenBuffDps, 6);
        // The buffer keeps their own nDPS and gains the share of the recipient's normalized rate they explain.
        Assert.Equal(500 + 1150 / 1.15 * 0.15, r[2].Rdps, 6);
        Assert.Equal(500, r[2].Ndps, 6);
    }

    [Fact]
    public void Uptime_scales_the_gain_linearly()
    {
        var half = Buff(PartySynergyCatalog.SwordCounter, actorId: 2, rate: 50, level: 25);

        Dictionary<int, DpsMetricResult> r = DpsMetrics.Compute(
            [Player(1, 1000, [half]), Player(2, 0)], [], EmptyCatalog(), Duration);

        Assert.Equal(1000 / 1.075, r[1].Ndps, 6); // 15% × 0.5 uptime
    }

    [Theory]
    // 노련한 반격 = 5.4% + 0.4%/level. The site's snapshot says a flat 10.7% for every level.
    [InlineData(1, 5.4)]
    [InlineData(22, 13.8)]
    [InlineData(25, 15.0)]
    public void Level_prices_the_synergy_buff_instead_of_the_snapshot(int level, double expectedPercent)
    {
        // The snapshot deliberately carries a WRONG number here so the test proves the level path wins.
        BuffValueCatalog snapshot = CatalogWith(
            (PartySynergyCatalog.SwordCounter, BuffGainCategory.OffenseAmp, 10.7));

        double gain = DpsMetrics.Gain(
            Buff(PartySynergyCatalog.SwordCounter, actorId: 2, rate: 100, level: level), snapshot);

        Assert.Equal(expectedPercent / 100.0, gain, 9);
    }

    [Fact]
    public void An_unknown_level_floors_at_level_one_instead_of_falling_through_to_the_snapshot()
    {
        // level 0 = the wire never gave one. The snapshot is NOT a safe fallback for a modelled synergy: it has
        // no row at all for several of them, and where it does the value is on a different scale. Flooring at
        // the bottom of the real curve under-credits rather than inventing.
        BuffValueCatalog snapshot = CatalogWith(
            (PartySynergyCatalog.SwordCounter, BuffGainCategory.OffenseAmp, 10.7));

        double gain = DpsMetrics.Gain(
            Buff(PartySynergyCatalog.SwordCounter, actorId: 2, rate: 100, level: 0), snapshot);

        Assert.Equal(0.054, gain, 9); // level 1 노련한 반격
    }

    [Fact]
    public void A_buff_the_catalog_does_not_model_still_uses_the_snapshot()
    {
        BuffValueCatalog snapshot = CatalogWith((987_654_321, BuffGainCategory.OffenseAmp, 12.0));

        Assert.Equal(0.12, DpsMetrics.Gain(Buff(987_654_321, actorId: 2, rate: 100), snapshot), 9);
    }

    [Fact]
    public void Mantra_stacks_its_level_breakpoints_on_top_of_the_linear_term()
    {
        // 불패의 진언 at 25: amp 10.5 + 0.5×24 = 22.5, plus 치명타 피해 증폭 5, 강타 5, 완벽 10 — each its own
        // multiplicative effect, exactly as the site composes them.
        double gain = DpsMetrics.Gain(
            Buff(PartySynergyCatalog.ChanterMantra, actorId: 2, rate: 100, level: 25), EmptyCatalog());

        double expected = 1.225 * 1.05 * 1.05 * 1.10 - 1.0;
        Assert.Equal(expected, gain, 9);
    }

    [Fact]
    public void Gale_gives_nothing_below_level_20_and_adds_weapon_amp_at_25()
    {
        Assert.Equal(0.0, DpsMetrics.Gain(
            Buff(PartySynergyCatalog.ChanterGale, actorId: 2, rate: 100, level: 19), EmptyCatalog()), 9);
        Assert.Equal(0.10, DpsMetrics.Gain(
            Buff(PartySynergyCatalog.ChanterGale, actorId: 2, rate: 100, level: 20), EmptyCatalog()), 9);
        Assert.Equal(1.10 * 1.05 - 1.0, DpsMetrics.Gain(
            Buff(PartySynergyCatalog.ChanterGale, actorId: 2, rate: 100, level: 25), EmptyCatalog()), 9);
    }

    [Fact]
    public void Earth_promise_only_counts_as_a_gain_when_it_is_on_the_boss()
    {
        // 대지의 약속 strips the target's PvE resistance. On the boss that is everyone's damage gain; the same
        // row misfiled onto a player would be that player's own survivability and must move nothing.
        var onBoss = Buff(PartySynergyCatalog.ChanterEarthPromise, actorId: 2, rate: 100, level: 21, boss: true);
        var onPlayer = Buff(PartySynergyCatalog.ChanterEarthPromise, actorId: 2, rate: 100, level: 21);

        Assert.Equal(0.134, DpsMetrics.Gain(onBoss, EmptyCatalog()), 9); // 5.4 + 0.4×20
        Assert.Equal(0.0, DpsMetrics.Gain(onPlayer, EmptyCatalog()), 9);
    }

    [Fact]
    public void A_boss_debuff_helps_everyone_except_the_player_who_applied_it()
    {
        var debuff = Buff(PartySynergyCatalog.ChanterEarthPromise, actorId: 2, rate: 100, level: 1, boss: true);

        Dictionary<int, DpsMetricResult> r = DpsMetrics.Compute(
            [Player(1, 1054), Player(2, 1054)], [debuff], EmptyCatalog(), Duration);

        Assert.Equal(1054 / 1.054, r[1].Ndps, 6);
        Assert.Equal(1054, r[2].Ndps, 6); // the applier gets no gain from their own debuff
    }

    [Fact]
    public void Only_one_half_of_an_exclusive_pair_is_counted()
    {
        // 노련한 반격 (검성) and 격앙 (수호성) never stack in game — but the server broadcasts both, overlapping
        // for their whole duration. Counting both would credit a support for a buff that did nothing.
        var counter = Buff(PartySynergyCatalog.SwordCounter, actorId: 2, rate: 100, level: 25);   // 15.0%
        var fervor = Buff(PartySynergyCatalog.GuardianFervor, actorId: 3, rate: 100, level: 10);  // 9.5%

        Dictionary<int, DpsMetricResult> r = DpsMetrics.Compute(
            [Player(1, 1150, [counter, fervor]), Player(2, 0), Player(3, 0)], [], EmptyCatalog(), Duration);

        // Higher level wins: only 노련한 반격's 15% applies, and only its caster is credited.
        Assert.Equal(1150 / 1.15, r[1].Ndps, 6);
        Assert.True(r[2].GivenBuffDps > 0);
        Assert.Equal(0, r[3].GivenBuffDps, 6);
    }

    [Fact]
    public void Gale_suppresses_earth_blessing_regardless_of_level()
    {
        // The pair declares 질풍의 권능 a FIXED winner: the server blocks a new 대지의 축복 while 질풍 is up but
        // does not remove one already applied, so the loser can linger ~20 s and would otherwise be counted.
        var gale = Buff(PartySynergyCatalog.ChanterGale, actorId: 2, rate: 100, level: 20);
        var blessing = Buff(PartySynergyCatalog.ClericEarthBlessing, actorId: 3, rate: 100, level: 25);

        Dictionary<int, DpsMetricResult> r = DpsMetrics.Compute(
            [Player(1, 1100, [gale, blessing]), Player(2, 0), Player(3, 0)], [], EmptyCatalog(), Duration);

        Assert.Equal(1100 / 1.10, r[1].Ndps, 6); // 질풍's 10% only
        Assert.Equal(0, r[3].GivenBuffDps, 6);
    }

    [Fact]
    public void Granted_damage_moves_from_the_recipient_to_the_granting_class()
    {
        // 흡혈의 검's 착취 lands as a real damage packet on the party member's meter under the 검성's skill code.
        // It is the 검성's damage passing through; it must leave the recipient's nDPS and land on the 검성's rDPS.
        var bloodBlade = Buff(PartySynergyCatalog.SwordBloodBlade, actorId: 2, rate: 100, level: 10);
        var granted = new Dictionary<int, long> { [PartySynergyCatalog.SwordBloodBlade] = 20_000 };

        Dictionary<int, DpsMetricResult> r = DpsMetrics.Compute(
            [Player(1, 1000, [bloodBlade], granted), Player(2, 500)], [], EmptyCatalog(), Duration);

        Assert.Equal(800, r[1].Ndps, 6);            // 20,000 over 100 s = 200/s of someone else's damage
        Assert.Equal(700, r[2].Rdps, 6);            // 500 own + 200 granted
        Assert.Equal(20_000, r[2].GrantedDamage);
        Assert.Equal(0, r[1].GrantedDamage);
    }

    [Fact]
    public void The_granting_classes_own_hits_stay_their_own()
    {
        // A 검성's own 흡혈의 검 hits carry the SAME skill code as the shared 착취. Only the buff's caster can
        // tell them apart, so a self-cast grant must not be moved anywhere.
        var selfCast = Buff(PartySynergyCatalog.SwordBloodBlade, actorId: 1, rate: 100, level: 10);
        var granted = new Dictionary<int, long> { [PartySynergyCatalog.SwordBloodBlade] = 20_000 };

        Dictionary<int, DpsMetricResult> r = DpsMetrics.Compute(
            [Player(1, 1000, [selfCast], granted)], [], EmptyCatalog(), Duration);

        // The damage still leaves ndps (it is priced separately), but there is nobody else to credit, so no
        // participant is awarded it — and crucially the player is not credited twice.
        Assert.Equal(0, r[1].GrantedDamage);
        Assert.Equal(1000, r[1].Rdps, 6);
    }

    [Fact]
    public void Total_external_gain_is_capped_so_a_corrupt_uptime_cannot_zero_out_ndps()
    {
        // Same guard as the site: without it a bad rate could divide DPS by an arbitrarily large number and
        // report "this player did almost nothing".
        var huge = Enumerable.Range(2, 12)
            .Select(a => Buff(PartySynergyCatalog.ChanterMantra + a, actorId: a, rate: 100, level: 0))
            .ToList();
        var catalog = new BuffValueCatalog();
        catalog.Load(huge.Select(b =>
            (b.Code, (IReadOnlyList<BuffGainEffect>)[new BuffGainEffect(BuffGainCategory.OffenseAmp, 90)])));

        var players = new List<MetricParticipantInput> { Player(1, 5000, huge) };
        players.AddRange(Enumerable.Range(2, 12).Select(a => Player(a, 0)));

        Dictionary<int, DpsMetricResult> r = DpsMetrics.Compute(players, [], catalog, Duration);

        Assert.Equal(5000 / 5.0, r[1].Ndps, 6); // capped at a total gain of 4
    }

    // ---- regressions from the 2026-08-31 adversarial review ----

    [Fact]
    public void Protect_light_is_priced_even_though_the_shipped_snapshot_has_no_row_for_it()
    {
        // The synergy catalog used to return null here and lean on "the snapshot will cover it". It does not:
        // buff_values.json has no 1741 key, and its second-tier lookup by 8-digit base is dead for every job
        // buff, so the healer's whole contribution priced at zero.
        double gain = DpsMetrics.Gain(
            Buff(PartySynergyCatalog.ClericProtectLight, actorId: 2, rate: 100, level: 25), EmptyCatalog());

        Assert.Equal(0.05, gain, 9);
    }

    [Fact]
    public void A_modelled_synergy_never_falls_through_to_a_snapshot_row()
    {
        // Where a snapshot row does exist it is not on the same scale — 질풍의 권능's rows carry a flat 치명타
        // RATING of 200, which the gain model would read as +200%, clamp to +100%, and hand out as a doubling.
        // An unreadable level must floor at level 1, not fall through.
        BuffValueCatalog poisoned = CatalogWith(
            (PartySynergyCatalog.ChanterGale, BuffGainCategory.OffenseCrit, 200));

        double gain = DpsMetrics.Gain(
            Buff(PartySynergyCatalog.ChanterGale, actorId: 2, rate: 100, level: 0), poisoned);

        Assert.Equal(0.0, gain, 9); // level 1 질풍 gives nothing, and the 200 never applies
    }

    [Fact]
    public void An_exclusive_loser_keeps_the_time_the_winner_was_not_up()
    {
        // The pair rule is instantaneous, but these inputs are whole-battle unions. 대지의 축복 covering almost
        // the whole fight must not be deleted because 질풍 flickered for a moment.
        var gale = new MetricBuffInput(
            PartySynergyCatalog.ChanterGale, PartySynergyCatalog.ChanterGale, 2, 2.0, 20, false,
            [(0L, 6_000L)]);
        var blessing = new MetricBuffInput(
            PartySynergyCatalog.ClericEarthBlessing, PartySynergyCatalog.ClericEarthBlessing, 3, 95.0, 25, false,
            [(0L, 285_000L)]);

        Dictionary<int, DpsMetricResult> r = DpsMetrics.Compute(
            [Player(1, 1000, [gale, blessing]), Player(2, 0), Player(3, 0)], [], EmptyCatalog(), 300.0);

        // 질풍 wins (fixed winner) but only for its 6 s; the healer keeps the other 279 s and is still credited.
        Assert.True(r[3].GivenBuffDps > 0, "the healer must keep the uptime the chanter never covered");
        Assert.True(r[1].Ndps < 1000);
    }

    [Fact]
    public void A_fully_overlapped_exclusive_loser_is_dropped_entirely()
    {
        var gale = new MetricBuffInput(
            PartySynergyCatalog.ChanterGale, PartySynergyCatalog.ChanterGale, 2, 100.0, 20, false,
            [(0L, 300_000L)]);
        var blessing = new MetricBuffInput(
            PartySynergyCatalog.ClericEarthBlessing, PartySynergyCatalog.ClericEarthBlessing, 3, 50.0, 25, false,
            [(0L, 150_000L)]);

        Dictionary<int, DpsMetricResult> r = DpsMetrics.Compute(
            [Player(1, 1100, [gale, blessing]), Player(2, 0), Player(3, 0)], [], EmptyCatalog(), 300.0);

        Assert.Equal(0, r[3].GivenBuffDps, 6);
        Assert.Equal(1100 / 1.10, r[1].Ndps, 6);
    }

    [Fact]
    public void Without_spans_the_exclusive_loser_falls_back_to_subtracting_rates()
    {
        // Old saved battles carry no intervals. Subtracting rates is exact when one window contains the other
        // and never over-credits otherwise — what must NOT happen is deleting the row.
        var counter = Buff(PartySynergyCatalog.SwordCounter, actorId: 2, rate: 10, level: 25);  // winner (level)
        var fervor = Buff(PartySynergyCatalog.GuardianFervor, actorId: 3, rate: 90, level: 10); // loser, mostly alone

        Dictionary<int, DpsMetricResult> r = DpsMetrics.Compute(
            [Player(1, 1000, [counter, fervor]), Player(2, 0), Player(3, 0)], [], EmptyCatalog(), 100.0);

        Assert.True(r[2].GivenBuffDps > 0, "the level winner keeps its own uptime");
        Assert.True(r[3].GivenBuffDps > 0, "the loser keeps the 80% it was up alone");
    }

    [Fact]
    public void Two_casters_of_a_granting_buff_split_the_damage_by_uptime()
    {
        // Two 검성 in one raid produce one indistinguishable pile of 흡혈의 검 damage on a teammate's meter.
        // Uptime is the only evidence about who supplied what, so it is the split — the first row must not
        // take all of it.
        var fromTwo = Buff(PartySynergyCatalog.SwordBloodBlade, actorId: 2, rate: 75, level: 10);
        var fromThree = Buff(PartySynergyCatalog.SwordBloodBlade, actorId: 3, rate: 25, level: 10);
        var granted = new Dictionary<int, long> { [PartySynergyCatalog.SwordBloodBlade] = 40_000 };

        Dictionary<int, DpsMetricResult> r = DpsMetrics.Compute(
            [Player(1, 1000, [fromTwo, fromThree], granted), Player(2, 0), Player(3, 0)],
            [], EmptyCatalog(), 100.0);

        Assert.Equal(30_000, r[2].GrantedDamage);
        Assert.Equal(10_000, r[3].GrantedDamage);
        Assert.Equal(40_000, r[2].GrantedDamage + r[3].GrantedDamage); // nothing invented, nothing lost
    }

    [Fact]
    public void A_granting_class_keeps_its_own_share_when_a_second_caster_is_present()
    {
        // The player is a 검성 themself AND another 검성 buffed them. Only the other caster's share may move;
        // crediting the whole pile away would take the player's own hits from them.
        var mine = Buff(PartySynergyCatalog.SwordBloodBlade, actorId: 1, rate: 50, level: 10);
        var theirs = Buff(PartySynergyCatalog.SwordBloodBlade, actorId: 2, rate: 50, level: 10);
        var granted = new Dictionary<int, long> { [PartySynergyCatalog.SwordBloodBlade] = 40_000 };

        Dictionary<int, DpsMetricResult> r = DpsMetrics.Compute(
            [Player(1, 1000, [mine, theirs], granted), Player(2, 0)], [], EmptyCatalog(), 100.0);

        Assert.Equal(20_000, r[2].GrantedDamage);
        Assert.Equal(0, r[1].GrantedDamage);
        Assert.Equal(800, r[1].Ndps, 6); // only half the pile (200/s over 100 s) left this player
    }

    [Fact]
    public void Categories_the_model_does_not_price_contribute_nothing()
    {
        // 이동 속도 / 받는 피해 감소 / PvP 증폭 are carried in the shipped table but move no PvE damage.
        BuffValueCatalog catalog = CatalogWith((900, BuffGainCategory.None, 50));

        Assert.Equal(0.0, DpsMetrics.Gain(Buff(900, actorId: 2, rate: 100), catalog), 9);
    }
}

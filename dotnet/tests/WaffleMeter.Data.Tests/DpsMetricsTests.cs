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
    public void An_unknown_level_falls_back_to_the_shipped_snapshot_rather_than_guessing()
    {
        // level 0 = the wire never gave one. Inventing a level would fabricate a number; the snapshot value is
        // at least measured, even if it is stale.
        BuffValueCatalog snapshot = CatalogWith(
            (PartySynergyCatalog.SwordCounter, BuffGainCategory.OffenseAmp, 10.7));

        double gain = DpsMetrics.Gain(
            Buff(PartySynergyCatalog.SwordCounter, actorId: 2, rate: 100, level: 0), snapshot);

        Assert.Equal(0.107, gain, 9);
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

    [Fact]
    public void Categories_the_model_does_not_price_contribute_nothing()
    {
        // 이동 속도 / 받는 피해 감소 / PvP 증폭 are carried in the shipped table but move no PvE damage.
        BuffValueCatalog catalog = CatalogWith((900, BuffGainCategory.None, 50));

        Assert.Equal(0.0, DpsMetrics.Gain(Buff(900, actorId: 2, rate: 100), catalog), 9);
    }
}

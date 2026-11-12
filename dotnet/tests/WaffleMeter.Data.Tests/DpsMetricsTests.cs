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

    private static BuffValueCatalog CatalogWith(params (int Code, BuffEffectKind Kind, double Value)[] rows)
    {
        var catalog = new BuffValueCatalog();
        catalog.Load(rows.Select(r =>
            (r.Code, (IReadOnlyList<BuffGainEffect>)[new BuffGainEffect(r.Kind, r.Value)])));
        return catalog;
    }

    /// <summary>
    /// A baseline with every bucket EMPTY: 증폭 0%p, 공격력 증가율 0%p, 발동률 0, 완벽 보너스비 1.
    ///
    /// <para>At this baseline every denominator is 1, so a buff's percentage points pass straight through as
    /// percent and the arithmetic of the tests below stays checkable by hand. It is a measuring stick, NOT a
    /// claim about a real player — a real one divides by a bucket that is already ~275 (see
    /// <see cref="BuffGainContext.Default"/>), which is the whole point of the model. The tests that care about
    /// that use the realistic baseline instead.</para>
    ///
    /// <para>치명타 계열은 여기서 0이 된다 — 치명타 피해 증폭은 치명타가 터진 타격에만 붙으므로 발동률 0이면
    /// 정말로 값이 0이다. 그건 산술의 사고가 아니라 모델이 맞는 답을 낸 것이다.</para>
    /// </summary>
    private static readonly BuffGainContext Bare = new(
        AmpBucketPercent: 0,
        AttackIncreasePercent: 0,
        CritAmpPercent: 0,
        FrontAmpPercent: 0,
        CritRate: 0,
        SmiteRate: 0,
        PerfectRate: 0,
        FrontRate: 0,
        PerfectBonusRatio: 1.0);

    private static MetricBuffInput Buff(
        int displayBase, int actorId, double rate, int level = 0, int? code = null, bool boss = false) =>
        new(code ?? displayBase, displayBase, actorId, rate, level, boss);

    private static MetricParticipantInput Player(
        int uid, double dps, IReadOnlyList<MetricBuffInput>? buffs = null,
        IReadOnlyDictionary<int, long>? granted = null, int jobPrefix = 0,
        BuffGainContext? context = null) =>
        new(uid, dps, dps * Duration, buffs ?? [], granted ?? new Dictionary<int, long>(), jobPrefix,
            context ?? Bare);

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
        // 노련한 반격 at level 25 = 0.4×25 = 10.0% PvE amp, at 100% uptime → gain 0.10.
        var fromTwo = Buff(PartySynergyCatalog.SwordCounter, actorId: 2, rate: 100, level: 25);

        Dictionary<int, DpsMetricResult> r = DpsMetrics.Compute(
            [Player(1, 1100, [fromTwo]), Player(2, 500)], [], EmptyCatalog(), Duration);

        Assert.Equal(1100 / 1.10, r[1].Ndps, 6);
        Assert.Equal(1100 - 1100 / 1.10, r[1].TakenBuffDps, 6);
        // The buffer keeps their own nDPS and gains the share of the recipient's normalized rate they explain.
        Assert.Equal(500 + 1100 / 1.10 * 0.10, r[2].Rdps, 6);
        Assert.Equal(500, r[2].Ndps, 6);
    }

    [Fact]
    public void Uptime_scales_the_gain_linearly()
    {
        var half = Buff(PartySynergyCatalog.SwordCounter, actorId: 2, rate: 50, level: 25);

        Dictionary<int, DpsMetricResult> r = DpsMetrics.Compute(
            [Player(1, 1000, [half]), Player(2, 0)], [], EmptyCatalog(), Duration);

        Assert.Equal(1000 / 1.05, r[1].Ndps, 6); // 10% × 0.5 uptime
    }

    [Theory]
    // 노련한 반격 = 0.4%/level since client 125 (2026-11-11; it was 5.4% + 0.4%/level before — the patch took
    // 5%p off the base and left the per-level step alone). The site's snapshot says a flat 10.7% for every level.
    [InlineData(1, 0.4)]
    [InlineData(22, 8.8)]
    [InlineData(25, 10.0)]
    [InlineData(40, 16.0)]   // 최대 레벨 — 클라 표의 1600
    public void Level_prices_the_synergy_buff_instead_of_the_snapshot(int level, double expectedPercent)
    {
        // The snapshot deliberately carries a WRONG number here so the test proves the level path wins.
        BuffValueCatalog snapshot = CatalogWith(
            (PartySynergyCatalog.SwordCounter, BuffEffectKind.DamageAmp, 10.7));

        double gain = DpsMetrics.Gain(
            Buff(PartySynergyCatalog.SwordCounter, actorId: 2, rate: 100, level: level), snapshot, Bare);

        Assert.Equal(expectedPercent / 100.0, gain, 9);
    }

    [Fact]
    public void An_unknown_level_floors_at_level_one_instead_of_falling_through_to_the_snapshot()
    {
        // level 0 = the wire never gave one. The snapshot is NOT a safe fallback for a modelled synergy: it has
        // no row at all for several of them, and where it does the value is on a different scale. Flooring at
        // the bottom of the real curve under-credits rather than inventing.
        BuffValueCatalog snapshot = CatalogWith(
            (PartySynergyCatalog.SwordCounter, BuffEffectKind.DamageAmp, 10.7));

        double gain = DpsMetrics.Gain(
            Buff(PartySynergyCatalog.SwordCounter, actorId: 2, rate: 100, level: 0), snapshot, Bare);

        Assert.Equal(0.004, gain, 9); // level 1 노련한 반격
    }

    [Fact]
    public void A_buff_the_catalog_does_not_model_still_uses_the_snapshot()
    {
        BuffValueCatalog snapshot = CatalogWith((987_654_321, BuffEffectKind.DamageAmp, 12.0));

        Assert.Equal(0.12, DpsMetrics.Gain(Buff(987_654_321, actorId: 2, rate: 100), snapshot, Bare), 9);
    }

    [Fact]
    public void Mantra_prices_each_breakpoint_in_its_own_bucket_instead_of_multiplying_them_all()
    {
        // 불패의 진언 L25 = 증폭 17.5%p + 치명타 피해 증폭 5%p + 강타 5%p + 완벽 10%p.
        // (클라 124 까지는 증폭이 22.5%p 였다 — 2026-11-11 패치가 파티 피해 증폭을 일괄 5%p 내렸다.)
        //
        // 🔑 이 테스트가 버킷 모델의 이유다. 종전 모델은 넷을 전부 데미지 배수로 읽어
        //     1.225 × 1.05 × 1.05 × 1.10 − 1 = 48.6%
        // 를 냈고, 그 값으로 5인 파티에서 호법성 한 명이 파티 전체 딜의 26%를 자기 기여로 가져갔다.
        // 넷은 사실 데미지 공식의 서로 다른 자리에 들어간다 (BuffEffectKind 참조):
        //   증폭 17.5%p     → 이미 175%p 인 가산 버킷 위 → 17.5/275   =  6.36%
        //   치명타 피해 5%p → 치명타가 터진 타격에만      → c·0.05/mCrit =  2.04%
        //   강타 5%p        → 발동률, mSmite = 1 + p      → 0.05/1.468  =  3.41%
        //   완벽 10%p       → 구간 상단으로만 옮김        → 0.1·β/…     =  0.76%
        // 합성 13.1% (증폭이 22.5%p 이던 때는 15.0%). 증폭 항만 바뀌었으므로 두 값의 비는 정확히
        // (1 + 17.5/275) / (1 + 22.5/275) 여야 한다 — 나머지 세 항이 패치로 움직이지 않았다는 그물이다.
        double gain = DpsMetrics.Gain(
            Buff(PartySynergyCatalog.ChanterMantra, actorId: 2, rate: 100, level: 25),
            EmptyCatalog(),
            BuffGainContext.Default);

        Assert.Equal(0.1309, gain, 4);
        Assert.Equal(1.1502 * (1 + 17.5 / 275) / (1 + 22.5 / 275) - 1, gain, 4);
        Assert.True(gain < 0.20, $"불패의 진언 L25 는 20%를 넘을 수 없다 (측정 {gain:P2})");
    }

    /// <summary>
    /// 클라 125 (2026-11-11) 의 불패의 진언 랭크 재배치. 랭크 n 은 5n 레벨에서 열리고, 딜에 들어가는 세 항이
    /// 열리는 레벨이 바뀌었다: 치명타 피해 증폭 15 → <b>10</b>, 완벽 25 → <b>20</b>, 강타 20 → <b>25</b>.
    /// 15레벨에 새로 붙은 이동 속도는 피해와 무관해 싣지 않는다.
    /// <para>출처: <c>SkillAbnormalEffect</c> 1819002x1(AmplifyCriticalDamage 500) · 1819004x1(Perfect 1000) ·
    /// 1819005x1(HardHit 500). 25레벨의 합은 종전과 같고, 달라지는 것은 10~24레벨 구간이다.</para>
    /// </summary>
    [Theory]
    [InlineData(1, 5.5, false, false, false)]
    [InlineData(9, 9.5, false, false, false)]
    [InlineData(10, 10.0, true, false, false)]    // 치명타 피해 증폭이 여기서 열린다 (종전 15)
    [InlineData(15, 12.5, true, false, false)]    // 15레벨은 이제 이동 속도뿐 — 딜 항은 그대로
    [InlineData(19, 14.5, true, false, false)]
    [InlineData(20, 15.0, true, true, false)]     // 완벽 (종전엔 강타가 여기 있었다)
    [InlineData(24, 17.0, true, true, false)]
    [InlineData(25, 17.5, true, true, true)]      // 강타 (종전엔 완벽이 여기 있었다)
    public void Mantra_opens_its_rank_effects_at_the_levels_the_client_table_gives(
        int level, double amp, bool critDamage, bool perfect, bool smite)
    {
        IReadOnlyList<BuffGainEffect> effects = PartySynergyCatalog.Effects(PartySynergyCatalog.ChanterMantra, level)!;

        var expected = new List<BuffGainEffect> { new(BuffEffectKind.DamageAmp, amp) };
        if (critDamage) expected.Add(new BuffGainEffect(BuffEffectKind.CritDamageAmp, 5.0));
        if (perfect) expected.Add(new BuffGainEffect(BuffEffectKind.PerfectRate, 10.0));
        if (smite) expected.Add(new BuffGainEffect(BuffEffectKind.SmiteRate, 5.0));

        Assert.Equal(expected.Count, effects.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].Kind, effects[i].Kind);
            Assert.Equal(expected[i].Value, effects[i].Value, 9);
        }
    }

    [Fact]
    public void The_same_amp_buff_is_worth_less_to_someone_whose_bucket_is_already_full()
    {
        // 증폭은 가산 버킷이라 %p 하나의 값어치가 "이미 얼마나 갖고 있나"에 반비례한다. 이 관계가 없으면
        // 지원 직업의 기여가 파티원 장비와 무관하게 고정돼 버린다.
        var counter = Buff(PartySynergyCatalog.SwordCounter, actorId: 2, rate: 100, level: 25); // 10.0%p

        double onEmpty = DpsMetrics.Gain(counter, EmptyCatalog(), Bare);
        double onFull = DpsMetrics.Gain(
            counter, EmptyCatalog(), Bare with { AmpBucketPercent = 175.0 });

        Assert.Equal(0.100, onEmpty, 9);                 // 10/100
        Assert.Equal(10.0 / 275.0, onFull, 9);           // 10/(100+175) = 3.64%
        Assert.True(onFull < onEmpty / 2.0);
    }

    [Fact]
    public void A_proc_rate_buff_is_worth_nothing_once_the_rate_is_capped()
    {
        // 발동률과 배수의 결정적 차이. 강타 100%인 사람에게 강타 +5%p 는 넣을 자리가 없다 — 배수 모델이었다면
        // ×1.05 를 영원히 더 곱했을 자리다.
        var blessing = Buff(PartySynergyCatalog.ClericEarthBlessing, actorId: 2, rate: 100, level: 10);

        Assert.Equal(0.05, DpsMetrics.Gain(blessing, EmptyCatalog(), Bare), 9);
        Assert.Equal(0.0, DpsMetrics.Gain(
            blessing, EmptyCatalog(), Bare with { SmiteRate = 1.0 }), 9);
    }

    [Fact]
    public void A_crit_damage_buff_is_priced_by_how_often_the_recipient_actually_crits()
    {
        // 치명타 피해 증폭은 치명타가 터진 타격에만 붙는다. 발동률이 실측으로 들어오므로 이건 가정이 아니라
        // 그 사람의 패킷에서 읽은 값이다.
        var effect = new BuffGainEffect(BuffEffectKind.CritDamageAmp, 5.0);
        BuffGainContext never = Bare with { CritRate = 0.0 };
        BuffGainContext always = Bare with { CritRate = 1.0 };

        Assert.Equal(0.0, DpsMetrics.RelativeGain(effect, bossScope: false, never), 9);
        // mCrit = 1 + 1.0 × (0.5 + 0) = 1.5 → 1.0 × 0.05 / 1.5
        Assert.Equal(0.05 / 1.5, DpsMetrics.RelativeGain(effect, bossScope: false, always), 9);
    }

    [Fact]
    public void A_crit_RATING_buff_is_priced_at_zero_rather_than_read_as_percent()
    {
        // 질풍의 권능의 스냅샷 행은 `Critical: 200` 을 싣는데 이건 발동률 %p 가 아니라 치명타 <수치>다.
        // 퍼센트로 읽으면 효과당 상한(100%)에 걸려 받는 사람의 데미지를 통째로 2배로 만든다.
        // 수치→발동률 곡선이 실측되기 전까지는 0 이 맞다 — 지어내는 것보다 덜 주는 쪽이 낫다.
        BuffValueCatalog snapshot = CatalogWith((987_654_321, BuffEffectKind.CritRating, 200.0));

        Assert.Equal(0.0, DpsMetrics.Gain(
            Buff(987_654_321, actorId: 2, rate: 100), snapshot, BuffGainContext.Default), 9);
    }

    [Fact]
    public void The_snapshot_reads_its_bucket_from_the_stat_not_the_category()
    {
        // `offense_crit` 한 카테고리에 강타(발동률 %p)와 치명타 수치(rating)가 같이 들어 있다. 카테고리만으로는
        // 어느 버킷인지 못 가르므로 export 가 스탯 이름을 함께 싣고, 여기서 그걸로 가른다.
        Assert.Equal(BuffEffectKind.SmiteRate, BuffValueCatalog.ParseKind("HardHit", "offense_crit"));
        Assert.Equal(BuffEffectKind.CritRating, BuffValueCatalog.ParseKind("Critical", "offense_crit"));
        Assert.Equal(BuffEffectKind.DamageAmp, BuffValueCatalog.ParseKind("PvEAmplifyDamage", "offense_amp"));
        Assert.Equal(BuffEffectKind.FrontAmp, BuffValueCatalog.ParseKind("AmplifyFrontAttack", "offense_amp"));
        Assert.Equal(BuffEffectKind.AttackRatio, BuffValueCatalog.ParseKind("DamageRatio", "offense_atk"));

        // 피해 내성(PvE·전체)이 보스 내성 감소다. 방어력은 단위가 달라 매기지 않는다.
        Assert.Equal(BuffEffectKind.BossResistDown, BuffValueCatalog.ParseKind("PvEDecreaseDamage", "mitigation"));
        Assert.Equal(BuffEffectKind.BossResistDown, BuffValueCatalog.ParseKind("DecreaseDamage", "mitigation"));
        Assert.Equal(BuffEffectKind.None, BuffValueCatalog.ParseKind("DefenseRatio", "defense"));
        Assert.Equal(BuffEffectKind.None, BuffValueCatalog.ParseKind("Defense", "defense"));

        // PvP 계열과 아직 모르는 스탯은 아무것도 기여하지 않는다 — 버킷을 추측하지 않는다.
        Assert.Equal(BuffEffectKind.None, BuffValueCatalog.ParseKind("PvPAmplifyDamage", "offense_amp_pvp"));
        Assert.Equal(BuffEffectKind.None, BuffValueCatalog.ParseKind("SomethingNewNextPatch", "offense_amp"));
    }

    [Fact]
    public void Gale_gives_nothing_below_level_20_and_adds_weapon_amp_at_25()
    {
        Assert.Equal(0.0, DpsMetrics.Gain(
            Buff(PartySynergyCatalog.ChanterGale, actorId: 2, rate: 100, level: 19), EmptyCatalog(), Bare), 9);
        Assert.Equal(0.10, DpsMetrics.Gain(
            Buff(PartySynergyCatalog.ChanterGale, actorId: 2, rate: 100, level: 20), EmptyCatalog(), Bare), 9);
        Assert.Equal(1.10 * 1.05 - 1.0, DpsMetrics.Gain(
            Buff(PartySynergyCatalog.ChanterGale, actorId: 2, rate: 100, level: 25), EmptyCatalog(), Bare), 9);
    }

    [Fact]
    public void Earth_promise_only_counts_as_a_gain_when_it_is_on_the_boss()
    {
        // 대지의 약속 strips the target's PvE resistance. On the boss that is everyone's damage gain; the same
        // row misfiled onto a player would be that player's own survivability and must move nothing.
        var onBoss = Buff(PartySynergyCatalog.ChanterEarthPromise, actorId: 2, rate: 100, level: 21, boss: true);
        var onPlayer = Buff(PartySynergyCatalog.ChanterEarthPromise, actorId: 2, rate: 100, level: 21);

        Assert.Equal(0.134, DpsMetrics.Gain(onBoss, EmptyCatalog(), Bare), 9); // 5.4 + 0.4×20
        Assert.Equal(0.0, DpsMetrics.Gain(onPlayer, EmptyCatalog(), Bare), 9);
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
        var counter = Buff(PartySynergyCatalog.SwordCounter, actorId: 2, rate: 100, level: 25);   // 10.0%
        var fervor = Buff(PartySynergyCatalog.GuardianFervor, actorId: 3, rate: 100, level: 10);  // 5.0%

        Dictionary<int, DpsMetricResult> r = DpsMetrics.Compute(
            [Player(1, 1100, [counter, fervor]), Player(2, 0), Player(3, 0)], [], EmptyCatalog(), Duration);

        // Higher level wins: only 노련한 반격's 10% applies, and only its caster is credited.
        Assert.Equal(1100 / 1.10, r[1].Ndps, 6);
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
    public void A_grant_with_no_buff_row_is_attributed_by_the_granting_class()
    {
        // 실측(2026-09-01): 파티에 호법성이 있으면 질풍의 권능이 대지의 축복 <적용>을 막는데도 추가 피해는
        // 파티원 전원에게 계속 들어온다 — 피해는 있고 버프 행은 시전자에게만 있는 상태다. 버프 행만 근거로
        // 삼으면 그 피해가 아무에게도 안 붙어 치유성의 기여가 통째로 0이 된다.
        var granted = new Dictionary<int, long> { [PartySynergyCatalog.ClericEarthBlessing] = 30_000 };

        Dictionary<int, DpsMetricResult> r = DpsMetrics.Compute(
            [
                Player(1, 1000, granted: granted, jobPrefix: 16), // 정령성 — 자기 것일 수 없다
                Player(2, 500, jobPrefix: 17),                    // 치유성
            ],
            [], EmptyCatalog(), Duration);

        Assert.Equal(30_000, r[2].GrantedDamage);
        Assert.Equal(700, r[1].Ndps, 6); // 30,000 / 100s = 300/s 가 빠진다
    }

    [Fact]
    public void A_buff_row_still_wins_over_the_class_fallback()
    {
        // 시전자를 아는 경우가 언제나 더 정확하다 — 치유성이 둘일 때 특히.
        var fromThree = Buff(PartySynergyCatalog.ClericEarthBlessing, actorId: 3, rate: 100, level: 25);
        var granted = new Dictionary<int, long> { [PartySynergyCatalog.ClericEarthBlessing] = 30_000 };

        Dictionary<int, DpsMetricResult> r = DpsMetrics.Compute(
            [
                Player(1, 1000, [fromThree], granted, jobPrefix: 16),
                Player(2, 0, jobPrefix: 17),
                Player(3, 0, jobPrefix: 17),
            ],
            [], EmptyCatalog(), Duration);

        Assert.Equal(30_000, r[3].GrantedDamage);
        Assert.Equal(0, r[2].GrantedDamage);
    }

    [Fact]
    public void The_granting_class_keeps_its_own_grant_when_nothing_names_a_caster()
    {
        // 치유성이 맞은 대지의 축복은 자기 것일 수도, 다른 치유성 것일 수도 있다. 가릴 근거가 없으면 옮기지
        // 않는다 — 잘못 옮기면 남의 피해가 되거나 총합에서 증발한다.
        var granted = new Dictionary<int, long> { [PartySynergyCatalog.ClericEarthBlessing] = 30_000 };

        Dictionary<int, DpsMetricResult> r = DpsMetrics.Compute(
            [Player(1, 1000, granted: granted, jobPrefix: 17), Player(2, 0, jobPrefix: 17)],
            [], EmptyCatalog(), Duration);

        Assert.Equal(0, r[2].GrantedDamage);
        Assert.Equal(1000, r[1].Ndps, 6); // 그대로 남는다
    }

    [Fact]
    public void The_class_fallback_splits_evenly_between_two_of_that_class()
    {
        var granted = new Dictionary<int, long> { [PartySynergyCatalog.ClericEarthBlessing] = 30_000 };

        Dictionary<int, DpsMetricResult> r = DpsMetrics.Compute(
            [
                Player(1, 1000, granted: granted, jobPrefix: 16),
                Player(2, 0, jobPrefix: 17),
                Player(3, 0, jobPrefix: 17),
            ],
            [], EmptyCatalog(), Duration);

        Assert.Equal(15_000, r[2].GrantedDamage);
        Assert.Equal(15_000, r[3].GrantedDamage);
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
            (b.Code, (IReadOnlyList<BuffGainEffect>)[new BuffGainEffect(BuffEffectKind.DamageAmp, 90)])));

        var players = new List<MetricParticipantInput> { Player(1, 5000, huge) };
        players.AddRange(Enumerable.Range(2, 12).Select(a => Player(a, 0)));

        Dictionary<int, DpsMetricResult> r = DpsMetrics.Compute(players, [], catalog, Duration);

        Assert.Equal(5000 / 5.0, r[1].Ndps, 6); // capped at a total gain of 4
    }

    // ---- regressions from the 2026-08-31 adversarial review ----

    [Fact]
    public void Protect_light_is_priced_from_the_level_even_without_a_snapshot_row()
    {
        // The synergy catalog used to return null here and lean on "the snapshot will cover it". It must not:
        // the healer's contribution would then hinge on whatever the table happens to hold.
        double Gain(int level) => DpsMetrics.Gain(
            Buff(PartySynergyCatalog.ClericProtectLight, actorId: 2, rate: 100, level: level), EmptyCatalog(), Bare);

        // PvE 피해 증폭 5.5 + 0.5/레벨(클라 125 — 종전 10.5), 20레벨 완벽 +10, 25레벨 강타 +5. Bare 기준선이라
        // %p 가 곧 이득이다. 클라 표: 1741000x11 의 550 → 25레벨 1750, 4랭크 Perfect 1000, 5랭크 HardHit 500.
        Assert.Equal(0.055, Gain(1), 9);
        Assert.Equal(0.145, Gain(19), 9);
        Assert.Equal(1.15 * 1.10 - 1.0, Gain(20), 9);
        Assert.Equal(1.175 * 1.10 * 1.05 - 1.0, Gain(25), 9);
    }

    [Fact]
    public void Fervor_follows_the_client_ladder()
    {
        // 127800011: PvE 피해 증폭 레벨당 50 — 1레벨 50, 40레벨 2000 (클라 125, 2026-11-11). 종전에는 1레벨이
        // 550 이었다(5.5% + 0.5%p×(레벨−1), 40레벨 25%). 사이트와 같은 식이다.
        Assert.Equal(0.005, DpsMetrics.Gain(
            Buff(PartySynergyCatalog.GuardianFervor, actorId: 2, rate: 100, level: 1), EmptyCatalog(), Bare), 9);
        Assert.Equal(0.05, DpsMetrics.Gain(
            Buff(PartySynergyCatalog.GuardianFervor, actorId: 2, rate: 100, level: 10), EmptyCatalog(), Bare), 9);
        Assert.Equal(0.20, DpsMetrics.Gain(
            Buff(PartySynergyCatalog.GuardianFervor, actorId: 2, rate: 100, level: 40), EmptyCatalog(), Bare), 9);
    }

    [Fact]
    public void A_modelled_synergy_never_falls_through_to_a_snapshot_row()
    {
        // Where a snapshot row does exist it is not on the same scale — 질풍의 권능's rows carry a flat 치명타
        // RATING of 200, which the gain model would read as +200%, clamp to +100%, and hand out as a doubling.
        // An unreadable level must floor at level 1, not fall through.
        BuffValueCatalog poisoned = CatalogWith(
            (PartySynergyCatalog.ChanterGale, BuffEffectKind.CritRating, 200));

        double gain = DpsMetrics.Gain(
            Buff(PartySynergyCatalog.ChanterGale, actorId: 2, rate: 100, level: 0), poisoned, Bare);

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
    public void Stats_the_model_does_not_price_contribute_nothing()
    {
        // 이동 속도 / 받는 피해 감소 / PvP 증폭 are carried in the shipped table but move no PvE damage.
        BuffValueCatalog catalog = CatalogWith((900, BuffEffectKind.None, 50));

        Assert.Equal(0.0, DpsMetrics.Gain(Buff(900, actorId: 2, rate: 100), catalog, Bare), 9);
    }
}

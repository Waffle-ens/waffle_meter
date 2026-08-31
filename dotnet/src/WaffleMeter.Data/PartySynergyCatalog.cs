namespace WaffleMeter.Data;

/// <summary>
/// The party-synergy support buffs, modelled from the CASTER'S SKILL LEVEL instead of from a fixed snapshot.
///
/// <para>Every buff here scales linearly (or in level breakpoints) with the level its caster put into the
/// skill, and the wire tells us that level on every application (어노멀 레벨, see <see cref="UseBuff.Level"/>).
/// The shipped <see cref="BuffValueCatalog"/> snapshot cannot express that — one row per buff code, no level —
/// which is why it credits 불패의 진언 at level 25 with its level-1 value (10.5% instead of 22.5%), has no row
/// for 질풍의 권능's rank-5 code (so it credits nothing at all), and no row for 흡혈의 검. This catalog wins
/// wherever both have an opinion; everything not listed here still comes from the snapshot.</para>
///
/// <para><b>Two of these do not multiply anything.</b> 흡혈의 검's 착취 and 대지의 축복's 공격 적중 시 추가 피해
/// arrive as REAL DAMAGE PACKETS on each party member, carrying the granting class's skill code — measured
/// on a live 8-player raid capture, all five party members carried both. Those are attributed by moving the
/// measured damage, not by estimating a multiplier; see <see cref="GrantedDamageSource"/>.</para>
/// </summary>
public static class PartySynergyCatalog
{
    // Display base codes (DataManager.BuffDisplayBase), which is what the exclusive-pair table also keys on.
    public const int SwordCounter = 11_780_000;     // 검성 노련한 반격
    public const int SwordBloodBlade = 11_340_000;  // 검성 흡혈의 검
    public const int GuardianFervor = 12_780_000;   // 수호성 격앙
    public const int ClericProtectLight = 17_410_000; // 치유성 보호의 빛
    public const int ClericEarthBlessing = 17_400_058; // 치유성 대지의 축복 (대지의 징벌 레벨에 따라 변함)
    public const int ChanterMantra = 18_190_000;    // 호법성 불패의 진언
    public const int ChanterGale = 18_250_000;      // 호법성 질풍의 권능
    public const int ChanterEarthPromise = 18_780_000; // 호법성 대지의 약속 (보스 디버프)

    /// <summary>
    /// The level-correct effects for a modelled synergy buff, or <c>null</c> when this base is not one of them
    /// (the caller then falls back to the shipped snapshot).
    /// <para>Returns <c>null</c> for a KNOWN base whose level we could not read (<paramref name="level"/> 0):
    /// guessing a level would silently invent a number, and the snapshot's fixed value — wrong as it is — is
    /// at least a measured one. 대지의 축복 is the exception, because its tier is legible from the buff code
    /// even without a level.</para>
    /// </summary>
    public static IReadOnlyList<BuffGainEffect>? Effects(int displayBase, int level)
    {
        if (level <= 0)
        {
            return null;
        }

        return displayBase switch
        {
            // 1레벨 PvE 피해 증폭 5.4%, 이후 레벨당 +0.4%p.
            SwordCounter => [Amp(5.4 + 0.4 * (level - 1))],

            // 1레벨 PvE 피해 증폭 5%, 이후 레벨당 +0.5%p.
            GuardianFervor => [Amp(5.0 + 0.5 * (level - 1))],

            // 1레벨 PvE 피해 증폭 10.5%, 레벨당 +0.5%p, 그 위에 레벨 breakpoint 3개가 얹힌다.
            ChanterMantra => Mantra(level),

            // 20레벨부터 파티원 공격력 +10%, 25레벨에서 무기 피해 증폭 +5%. 그 아래 레벨에서는 전투 속도/명중
            // 계열만 올려 주므로 피해 이득이 0이다 (전투 속도는 site 모델에서도 별도 family로 잡히지만, 이
            // 스킬의 저레벨 구간 값은 실측이 없어 넣지 않는다 — 없는 숫자를 지어내지 않는다).
            ChanterGale => Gale(level),

            // 보스의 PvE 피해 내성을 1레벨 5.4%, 레벨당 +0.4%p 깎는다. 보스에게 걸리는 디버프이므로 Defense
            // 계열에 음수로 싣는다 — site 모델이 "boss scope + 음수 Defense = 모두에게 이득"으로 읽는 관례다.
            ChanterEarthPromise => [new BuffGainEffect(BuffGainCategory.Defense, -(5.4 + 0.4 * (level - 1)))],

            // 대지의 축복은 대지의 징벌 레벨을 따라간다: 10에서 파티원 강타 +5%, 15에서 공격력 +5%,
            // 25에서 공격 적중 시 추가 피해(= 배수가 아니라 실제 데미지 패킷, GrantedDamageSource 참조).
            ClericEarthBlessing => EarthBlessing(level),

            // 보호의 빛: 레벨별 계수 실측이 아직 없다. null 을 돌려 스냅샷 값(강타 5%)을 그대로 쓰게 둔다 —
            // 여기에 추정식을 넣으면 "레벨 기반이라 정확하다"는 이 카탈로그의 약속이 깨진다.
            ClericProtectLight => null,

            _ => null,
        };
    }

    private static BuffGainEffect Amp(double value) => new(BuffGainCategory.OffenseAmp, value);

    private static IReadOnlyList<BuffGainEffect> Mantra(int level)
    {
        var effects = new List<BuffGainEffect> { Amp(10.5 + 0.5 * (level - 1)) };
        if (level >= 15) effects.Add(new BuffGainEffect(BuffGainCategory.OffenseCrit, 5.0));  // 치명타 피해 증폭
        if (level >= 20) effects.Add(new BuffGainEffect(BuffGainCategory.OffenseCrit, 5.0));  // 강타
        if (level >= 25) effects.Add(new BuffGainEffect(BuffGainCategory.OffenseCrit, 10.0)); // 완벽
        return effects;
    }

    private static IReadOnlyList<BuffGainEffect> Gale(int level)
    {
        var effects = new List<BuffGainEffect>();
        if (level >= 20) effects.Add(new BuffGainEffect(BuffGainCategory.OffenseAtk, 10.0)); // 파티원 공격력
        if (level >= 25) effects.Add(Amp(5.0));                                              // 무기 피해 증폭
        return effects;
    }

    private static IReadOnlyList<BuffGainEffect> EarthBlessing(int level)
    {
        var effects = new List<BuffGainEffect>();
        if (level >= 10) effects.Add(new BuffGainEffect(BuffGainCategory.OffenseCrit, 5.0)); // 파티원 강타
        if (level >= 15) effects.Add(new BuffGainEffect(BuffGainCategory.OffenseAtk, 5.0));  // 파티원 공격력
        // 25레벨의 "공격 적중 시 추가 피해"는 배수가 아니라 실제 데미지라 여기서 곱하지 않는다.
        return effects;
    }

    /// <summary>
    /// The synergy buff that GRANTED this damage-skill code to a party member, or 0 when the code is ordinary
    /// damage. Two effects put damage on other people's meters under the granting class's own skill code:
    /// <list type="bullet">
    /// <item>검성 흡혈의 검 (level ≥ 10 shares 착취) — codes 1134xxxx.</item>
    /// <item>치유성 대지의 축복 (level ≥ 25 adds 공격 적중 시 추가 피해) — code 17400058 exactly.</item>
    /// </list>
    /// <para>대지의 축복 must be matched on the exact code, NOT on a rounded base: 대지의 징벌's own DoT rows
    /// (17400000 / 17400040 / 17400050) share the 17400000 base and are the healer's ordinary damage.</para>
    /// <para>The code alone does not say the damage was granted — the 검성's own 흡혈의 검 hits carry the same
    /// code. Who cast the buff on this player decides that, so the caller pairs this with the buff row's
    /// caster and keeps the damage when the caster IS the player.</para>
    /// </summary>
    public static int GrantedDamageSource(int skillCode)
    {
        if (skillCode == ClericEarthBlessing) return ClericEarthBlessing;
        return skillCode / 10_000 * 10_000 == SwordBloodBlade ? SwordBloodBlade : 0;
    }

    /// <summary>Whether this base ALSO puts measured damage on party members, on top of whatever multiplier
    /// <see cref="Effects"/> reports for it. Used by the combat detail to label the row.
    /// <para>There is no double-counting to guard against: 흡혈의 검 has no multiplier at all (it is not in the
    /// snapshot either), and <see cref="EarthBlessing"/> deliberately stops at the level-10/15 강타·공격력 terms
    /// and never tries to price the level-25 추가 피해 — that damage is moved, not estimated.</para></summary>
    public static bool IsMeasuredGrant(int displayBase) =>
        displayBase is SwordBloodBlade or ClericEarthBlessing;
}

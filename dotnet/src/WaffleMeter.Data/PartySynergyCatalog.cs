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
    /// (only then does the caller fall back to the shipped snapshot).
    ///
    /// <para><b>A modelled base NEVER falls through to the snapshot</b>, not even when the level is unreadable.
    /// Two reasons. First, the snapshot has no row at all for several of these — 보호의 빛, 흡혈의 검, and
    /// 질풍의 권능's and 불패의 진언's rank-5 codes are simply absent, and its second-tier lookup by 8-digit base
    /// cannot help because the table is keyed by 9-digit runtime codes only. Falling through would price them at
    /// ZERO, silently erasing a support's entire contribution. Second, where a row does exist it is not on the
    /// same scale: 질풍의 권능's snapshot rows carry <c>offense_crit: 200</c> — a flat 치명타 rating that the gain
    /// model would read as +200%, clamp to +100%, and hand out as a doubling.</para>
    ///
    /// <para>So an unreadable level (0) is treated as <b>level 1</b> — the floor of the real scale, and a number
    /// we can defend. It under-credits rather than inventing, and it is rare: a row reports the MAX level across
    /// its applications, so level 0 means every single application's tail failed validation (the measured rate
    /// is 99.2% carrying a level, and the 0.8% are consumables, which are not in this catalog).</para>
    /// </summary>
    public static IReadOnlyList<BuffGainEffect>? Effects(int displayBase, int level)
    {
        // 0 = the wire never gave one. Floor at the real scale's bottom instead of guessing or falling through.
        if (level <= 0)
        {
            level = 1;
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
            ChanterEarthPromise =>
                [new BuffGainEffect(BuffEffectKind.BossResistDown, -(5.4 + 0.4 * (level - 1)))],

            // 대지의 축복은 대지의 징벌 레벨을 따라간다: 10에서 파티원 강타 +5%, 15에서 공격력 +5%,
            // 25에서 공격 적중 시 추가 피해(= 배수가 아니라 실제 데미지 패킷, GrantedDamageSource 참조).
            ClericEarthBlessing => EarthBlessing(level),

            // 보호의 빛: 레벨별 계수 실측이 아직 없다. 사이트가 쓰는 고정값(강타 5%)을 그대로 쓰되, 여기에
            // 명시적으로 적는다 — 예전처럼 null 로 두고 "스냅샷이 받아 주겠지" 하면 안 된다. 출하 스냅샷에는
            // 이 버프의 행이 아예 없어서(1741 로 시작하는 키 0개) 기여가 통째로 0이 된다.
            // ⚠️ 레벨식이 실측되면 여기를 고친다. 그 전까지 이 값은 레벨과 무관하다.
            ClericProtectLight => [new BuffGainEffect(BuffEffectKind.SmiteRate, 5.0)],

            // 흡혈의 검: 배수가 아니라 실제 피해로만 기여한다(GrantedDamageSource 참조). 빈 목록을 돌려
            // "모델링됐고 배수는 0"임을 분명히 한다 — null 이면 스냅샷으로 떨어지는데, 거기에도 행이 없다.
            SwordBloodBlade => [],

            _ => null,
        };
    }

    private static BuffGainEffect Amp(double value) => new(BuffEffectKind.DamageAmp, value);

    private static IReadOnlyList<BuffGainEffect> Mantra(int level)
    {
        // 세 breakpoint 가 전부 `offense_crit` 한 덩어리였을 때는 셋 다 데미지 %로 곱해져 L25 가 48.6% 로
        // 값이 매겨졌다. 셋은 사실 서로 다른 버킷이다 — 치명타 피해 증폭은 치명타가 터졌을 때만, 강타·완벽은
        // 발동률이라 이미 높은 발동률 위에서는 값이 줄어든다.
        var effects = new List<BuffGainEffect> { Amp(10.5 + 0.5 * (level - 1)) };
        if (level >= 15) effects.Add(new BuffGainEffect(BuffEffectKind.CritDamageAmp, 5.0));
        if (level >= 20) effects.Add(new BuffGainEffect(BuffEffectKind.SmiteRate, 5.0));
        if (level >= 25) effects.Add(new BuffGainEffect(BuffEffectKind.PerfectRate, 10.0));
        return effects;
    }

    private static IReadOnlyList<BuffGainEffect> Gale(int level)
    {
        var effects = new List<BuffGainEffect>();
        if (level >= 20) effects.Add(new BuffGainEffect(BuffEffectKind.AttackRatio, 10.0)); // 파티원 공격력
        if (level >= 25) effects.Add(Amp(5.0));                                               // 무기 피해 증폭
        return effects;
    }

    private static IReadOnlyList<BuffGainEffect> EarthBlessing(int level)
    {
        var effects = new List<BuffGainEffect>();
        if (level >= 10) effects.Add(new BuffGainEffect(BuffEffectKind.SmiteRate, 5.0));   // 파티원 강타
        if (level >= 15) effects.Add(new BuffGainEffect(BuffEffectKind.AttackRatio, 5.0)); // 파티원 공격력
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

    /// <summary>
    /// 이 시너지를 <b>줄 수 있는 직업</b>의 접두(11 검성 … 19 권성), 아니면 0.
    ///
    /// <para>버프 행이 없을 때의 마지막 근거다. 실측(2026-09-01, 5인 파티)에서 파티원 전원이 대지의 축복
    /// 추가 피해(17400058, 1인당 31~54만)를 맞고 있는데 <b>버프 행은 치유성 본인에게만</b> 있었다 — 파티에
    /// 호법성이 있어 질풍의 권능이 대지의 축복 <i>적용</i>을 막았는데도 추가 피해는 계속 들어온 것이다.
    /// 버프 행만 근거로 삼으면 그 피해는 아무에게도 귀속되지 않고 조용히 사라진다(그 전투에서 치유성의
    /// 넘긴 피해가 정확히 0이었다).</para>
    ///
    /// <para>직업이 근거가 되는 이유: 이 스킬 코드들은 직업 전용이다. 치유성이 아닌 사람 미터에 찍힌
    /// 17400058 은 그 사람 것일 수 없다.</para>
    /// </summary>
    public static int GrantingJobPrefix(int displayBase) => displayBase switch
    {
        SwordBloodBlade => 11,      // 검성
        ClericEarthBlessing => 17,  // 치유성
        _ => 0,
    };

    /// <summary>Whether this base ALSO puts measured damage on party members, on top of whatever multiplier
    /// <see cref="Effects"/> reports for it. Used by the combat detail to label the row.
    /// <para>There is no double-counting to guard against: 흡혈의 검 has no multiplier at all (it is not in the
    /// snapshot either), and <see cref="EarthBlessing"/> deliberately stops at the level-10/15 강타·공격력 terms
    /// and never tries to price the level-25 추가 피해 — that damage is moved, not estimated.</para></summary>
    public static bool IsMeasuredGrant(int displayBase) =>
        displayBase is SwordBloodBlade or ClericEarthBlessing;
}

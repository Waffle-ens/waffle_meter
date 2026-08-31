using System.Text.Json;

namespace WaffleMeter.Data;

/// <summary>
/// Which bucket of the damage formula a buff effect lands in.
///
/// <para><b>Why a bucket and not a percentage.</b> Every one of these arrives from the game as PERCENTAGE
/// POINTS added to a stat, not as a multiplier on damage — and the damage formula
/// (<c>src/shared/stat-efficiency.ts</c>) adds them inside buckets that are already large. Treating the number
/// as a damage multiplier is a first-order error that grows with the recipient's gear:</para>
/// <list type="bullet">
/// <item>피해 증폭 +22.5%p on a player already at 175%p is <c>22.5/(100+175) = 8.2%</c> more damage, not 22.5%.</item>
/// <item>강타 +5%p is five points of PROC RATE. It shows up as <c>Δp/(1+p)</c>, worth 3.4% at p=0.47 — and
/// nothing at all once the rate is capped.</item>
/// <item>완벽 +10%p only swaps the hit onto the upper end of the attack range, so it is worth ten points times
/// that range's share — under 1% on a narrow-range weapon.</item>
/// </list>
/// <para>Composed, that is the difference between pricing 불패의 진언 L25 at 48.6% and at 15.0%. The 48.6%
/// version credited one 호법성 with 26% of a five-player raid's damage.</para>
/// </summary>
public enum BuffEffectKind
{
    /// <summary>Contributes nothing to damage — utility, healing, PvP-only, mitigation on a player.</summary>
    None = 0,

    /// <summary>피해 증폭 계열 %p → the additive <c>ampBucket</c>.</summary>
    DamageAmp,

    /// <summary>보스에게서 깎아낸 피해 내성 %p → the same additive <c>ampBucket</c> (it enters as a subtraction
    /// there), so it is priced identically. Only counts on a BOSS-scoped row with a negative value.</summary>
    BossResistDown,

    /// <summary>공격력 증가율 %p → the equipped-attack multiplier, which is also additive.</summary>
    AttackRatio,

    /// <summary>강타 발동률 %p.</summary>
    SmiteRate,

    /// <summary>완벽 발동률 %p.</summary>
    PerfectRate,

    /// <summary>치명타 피해 증폭 %p.</summary>
    CritDamageAmp,

    /// <summary>치명타 발동률 %p (a rate, not the rating — see <see cref="CritRating"/>).</summary>
    CritRate,

    /// <summary>전방 피해 증폭 %p.</summary>
    FrontAmp,

    /// <summary>
    /// 치명타 <b>수치</b> (a rating, not a rate). Priced at ZERO, deliberately.
    ///
    /// <para>Rating converts to a rate through a curve whose position depends on the recipient's own rating,
    /// which the meter only knows for the local player. Guessing it is worse than declining: the previous model
    /// read these rows as percent and, because 질풍의 권능's snapshot rows carry <c>Critical: 200</c>, handed out
    /// a flat DOUBLING of the recipient's damage after the per-effect cap. Zero under-credits a buff that is in
    /// any case worth little at the crit rates measured in raids (86% on the reference character, near the cap).
    /// ⚠️ If the rating curve is ever measured, this is where it goes.</para>
    /// </summary>
    CritRating,

    /// <summary>전투 속도 % → casts per second. The one kind that really is close to a direct multiplier: it
    /// buys attempts, not damage per attempt, so it stays <c>v/100</c>.</summary>
    CombatSpeed,
}

/// <summary>One stat a buff moves, and by how much (in percentage points).</summary>
/// <param name="Value">
/// Percentage points. Positive = the holder deals more damage. NEGATIVE is meaningful only together with
/// <see cref="BuffEffectKind.BossResistDown"/> on a BOSS-scoped row: that is a resistance the debuff stripped
/// off the boss, which everyone hitting it benefits from. A negative value anywhere else contributes nothing —
/// the same rule the site applies, kept identical so the two never disagree about a sign.
/// </param>
public readonly record struct BuffGainEffect(BuffEffectKind Kind, double Value);

/// <summary>
/// The baseline a buff's relative gain is measured against — where in the damage formula the recipient already
/// sits. Adding %p to a bucket that is already full is worth less than adding it to an empty one, so the answer
/// is not a property of the buff alone.
///
/// <para><b>Where each number comes from, and how much to trust it.</b></para>
/// <list type="bullet">
/// <item><b>The four rates are MEASURED, per participant</b>, from that player's own damage packets
/// (치명타/강타/완벽/전방 판정 비율). These are real observations of the person the buff landed on, not an
/// assumption — and they are what the rate-shaped effects (강타·완벽·치명타) are priced against.</item>
/// <item><b>The stat-shaped baselines are the LOCAL player's</b>, read off 0x364A. The meter never sees a
/// teammate's stat sheet — the game does not broadcast it — so a party member's 증폭 bucket is taken to look
/// like the local player's. ⚠️ That is the model's weakest premise. It is still far better than the
/// alternative it replaces (pretending the bucket is empty, i.e. dividing by 100), and it errs in a bounded
/// direction: a better-geared teammate's real bucket is larger, so their buffs are slightly over-credited.</item>
/// </list>
/// </summary>
/// <param name="AmpBucketPercent">피해 증폭 + PvE 증폭 + 보스 증폭, in points.</param>
/// <param name="AttackIncreasePercent">공격력 증가율, in points.</param>
/// <param name="CritAmpPercent">치명타 피해 증폭, in points.</param>
/// <param name="FrontAmpPercent">전방 피해 증폭, in points.</param>
/// <param name="CritRate">0..1, measured.</param>
/// <param name="SmiteRate">0..1, measured.</param>
/// <param name="PerfectRate">0..1, measured.</param>
/// <param name="FrontRate">0..1, measured.</param>
/// <param name="PerfectBonusRatio">
/// How much more a 완벽 hit lands for, as a fraction of a normal hit: <c>0.82 × 구간폭 / 공격력</c>, from the
/// formula's <c>perfectAttack = 최대공격력 + 0.32 × 구간폭</c> and <c>최대공격력 = 공격력 + 구간폭/2</c>.
/// A narrow attack range makes 완벽 nearly worthless, which is why this cannot be a constant.
/// </param>
public readonly record struct BuffGainContext(
    double AmpBucketPercent,
    double AttackIncreasePercent,
    double CritAmpPercent,
    double FrontAmpPercent,
    double CritRate,
    double SmiteRate,
    double PerfectRate,
    double FrontRate,
    double PerfectBonusRatio)
{
    /// <summary>
    /// The fallback used when neither a stat sheet nor measured hits are available (a battle replayed from an
    /// old save, or the seconds before 0x364A arrives).
    ///
    /// <para>These are ONE measured endgame character (2026-08-31): 증폭 63.4 + PvE 102.4 + 보스 9.1 = 174.9,
    /// 공격력 증가율 123.0, 치명타 피해 증폭 79.2, 구간폭 590 / 공격력 6129 → 완벽 보너스비 0.079; the rates are
    /// that player's own raid packets. ⚠️ A defensible order of magnitude, not a population average — the point
    /// is that the denominators are NON-EMPTY, which is the error being fixed. Every one of them is overridden
    /// the moment the meter has the real thing.</para>
    /// </summary>
    public static readonly BuffGainContext Default = new(
        AmpBucketPercent: 174.9,
        AttackIncreasePercent: 123.0,
        CritAmpPercent: 79.2,
        FrontAmpPercent: 13.7,
        CritRate: 0.865,
        SmiteRate: 0.468,
        PerfectRate: 0.510,
        FrontRate: 0.20,
        PerfectBonusRatio: 0.079);

    /// <summary>The formula's 치명타 factor at this baseline: <c>1 + 발동률 × (0.5 + 치명타 피해 증폭/100)</c>.</summary>
    public double CritMultiplier => 1.0 + Clamp01(CritRate) * (0.5 + Math.Max(CritAmpPercent, 0.0) / 100.0);

    /// <summary>The formula's 강타 factor. 강타는 그 타격을 2배로 만들므로 <c>1 + 발동률</c>.</summary>
    public double SmiteMultiplier => 1.0 + Clamp01(SmiteRate);

    /// <summary>The formula's 완벽 factor: <c>1 + 발동률 × 보너스비</c>.</summary>
    public double PerfectMultiplier => 1.0 + Clamp01(PerfectRate) * Math.Max(PerfectBonusRatio, 0.0);

    /// <summary>The formula's 방향 factor: <c>1 + 발동률 × 증폭/100</c>.</summary>
    public double FrontMultiplier => 1.0 + Clamp01(FrontRate) * Math.Max(FrontAmpPercent, 0.0) / 100.0;

    private static double Clamp01(double v) => Math.Clamp(v, 0.0, 1.0);
}

/// <summary>
/// The shipped snapshot of per-buff-code effect values, exported from the stats site's
/// <c>src/shared/buff-values.ts</c> by <c>dotnet/tools/export-buff-values.ts</c>. It is the fallback source
/// for every buff the meter does not model by level — consumables, scrolls, and other classes' incidental
/// buffs.
/// <para><b>It is deliberately NOT the authority for the party-synergy buffs.</b> The table holds one fixed
/// number per buff code and has no room for the caster's skill level, so it reads 불패의 진언 at level 25 as
/// its level-1 value, has no row at all for 질풍의 권능's rank-5 code, and none for 흡혈의 검.
/// <see cref="PartySynergyCatalog"/> overrides those from the level the wire gives us.</para>
/// </summary>
public sealed class BuffValueCatalog
{
    private readonly Dictionary<int, IReadOnlyList<BuffGainEffect>> _byCode = new();

    /// <summary>Effects for an exact runtime buff code, or an empty list when the snapshot has no row.</summary>
    public IReadOnlyList<BuffGainEffect> Get(int buffCode) =>
        _byCode.TryGetValue(buffCode, out IReadOnlyList<BuffGainEffect>? v) ? v : [];

    public int Count => _byCode.Count;

    public void Load(IEnumerable<(int Code, IReadOnlyList<BuffGainEffect> Effects)> rows)
    {
        foreach ((int code, IReadOnlyList<BuffGainEffect> effects) in rows)
        {
            _byCode[code] = effects;
        }
    }

    /// <summary>Parse <c>buff_values.json</c>:
    /// <c>{ "&lt;buffCode&gt;": [ { "s": "PvEAmplifyDamage", "c": "offense_amp", "v": 10.5 } ] }</c>.
    /// Unknown stats map to <see cref="BuffEffectKind.None"/> rather than throwing — the site can add one
    /// before the meter knows it, and an unknown stat must contribute nothing, not crash or guess a bucket.</summary>
    public static List<(int Code, IReadOnlyList<BuffGainEffect> Effects)> Parse(string json)
    {
        var result = new List<(int, IReadOnlyList<BuffGainEffect>)>();
        using JsonDocument doc = JsonDocument.Parse(json);

        foreach (JsonProperty entry in doc.RootElement.EnumerateObject())
        {
            if (!int.TryParse(entry.Name, out int code) || entry.Value.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var effects = new List<BuffGainEffect>();
            foreach (JsonElement effect in entry.Value.EnumerateArray())
            {
                if (effect.ValueKind != JsonValueKind.Object) continue;
                string stat = effect.TryGetProperty("s", out JsonElement s) ? s.GetString() ?? "" : "";
                string category = effect.TryGetProperty("c", out JsonElement c) ? c.GetString() ?? "" : "";
                double value = effect.TryGetProperty("v", out JsonElement v) && v.TryGetDouble(out double d) ? d : 0.0;
                if (value == 0.0) continue;
                effects.Add(new BuffGainEffect(ParseKind(stat, category), value));
            }

            if (effects.Count > 0)
            {
                result.Add((code, effects));
            }
        }

        return result;
    }

    /// <summary>
    /// Map a snapshot row onto the damage formula's bucket. The STAT decides — the category cannot, because
    /// <c>offense_crit</c> covers 치명타 수치 and 강타, which land in different places and on different scales,
    /// and <c>offense_amp</c> covers both the all-damage bucket and the 전방-only one.
    /// </summary>
    public static BuffEffectKind ParseKind(string? stat, string? category) => stat switch
    {
        "PvEAmplifyDamage" or "AmplifyAllDamage" => BuffEffectKind.DamageAmp,
        "AmplifyFrontAttack" => BuffEffectKind.FrontAmp,
        "DamageRatio" => BuffEffectKind.AttackRatio,
        "HardHit" => BuffEffectKind.SmiteRate,
        "Critical" => BuffEffectKind.CritRating,
        "CombatSpeed" => BuffEffectKind.CombatSpeed,

        // 방어 계열은 보스에게 걸린 음수일 때만 이득이다 (판정은 Gain 에서).
        _ when category == "defense" => BuffEffectKind.BossResistDown,

        // utility / healing / mitigation / PvP 계열, 그리고 아직 모르는 스탯.
        _ => BuffEffectKind.None,
    };
}

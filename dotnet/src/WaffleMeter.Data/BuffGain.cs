using System.Text.Json;

namespace WaffleMeter.Data;

/// <summary>
/// The stat family a buff effect belongs to, as the stats site's nDPS/rDPS model classifies it
/// (<c>src/shared/buff-values.ts</c>). Only the five offensive/defensive families below turn into a damage
/// gain; <c>utility</c>, <c>mitigation</c>, <c>healing</c> and the PvP families are carried in the shipped
/// table but contribute nothing, and map to <see cref="None"/> here.
/// </summary>
public enum BuffGainCategory
{
    None = 0,
    Defense,
    OffenseAmp,
    OffenseAtk,
    OffenseCrit,
    OffenseSpeed,
}

/// <summary>One stat a buff moves, and by how much (in percentage points).</summary>
/// <param name="Value">
/// Percentage points. Positive = the holder deals more damage. NEGATIVE is meaningful only together with
/// <see cref="BuffGainCategory.Defense"/> on a BOSS-scoped row: that is a resistance the debuff stripped off
/// the boss, which everyone hitting it benefits from. A negative value anywhere else contributes nothing —
/// the same rule the site applies, kept identical so the two never disagree about a sign.
/// </param>
public readonly record struct BuffGainEffect(BuffGainCategory Category, double Value);

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

    /// <summary>Parse <c>buff_values.json</c>: <c>{ "&lt;buffCode&gt;": [ { "c": "offense_amp", "v": 10.5 } ] }</c>.
    /// Unknown category strings map to <see cref="BuffGainCategory.None"/> rather than throwing — the site can
    /// add a family before the meter knows it, and an unknown family must contribute nothing, not crash.</summary>
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
                string category = effect.TryGetProperty("c", out JsonElement c) ? c.GetString() ?? "" : "";
                double value = effect.TryGetProperty("v", out JsonElement v) && v.TryGetDouble(out double d) ? d : 0.0;
                if (value == 0.0) continue;
                effects.Add(new BuffGainEffect(ParseCategory(category), value));
            }

            if (effects.Count > 0)
            {
                result.Add((code, effects));
            }
        }

        return result;
    }

    public static BuffGainCategory ParseCategory(string? category) => category switch
    {
        "defense" => BuffGainCategory.Defense,
        "offense_amp" => BuffGainCategory.OffenseAmp,
        "offense_atk" => BuffGainCategory.OffenseAtk,
        "offense_crit" => BuffGainCategory.OffenseCrit,
        "offense_speed" => BuffGainCategory.OffenseSpeed,
        _ => BuffGainCategory.None,
    };
}

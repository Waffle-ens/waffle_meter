using System.Text.Json;

namespace WaffleMeter.Replay;

/// <summary>How a mechanic's zone is anchored — decided by the client, not guessed.</summary>
public enum ZoneAnchor
{
    /// <summary>On the caster's body (the boss). Directional shapes rotate with its facing at cast.</summary>
    Caster,

    /// <summary>Stuck to the marked player — the marker follows them until it goes off.</summary>
    Target,

    /// <summary>Dropped on the ground where the marked player stood at cast (a puddle that does NOT follow).</summary>
    TargetLocation,
}

/// <summary>The zone a mechanic paints on the floor. Values are world units / degrees, straight from the
/// client's <c>SkillEffectFilter</c> row (see tools/skill-shapes-export.py).</summary>
/// <param name="Kind">Circle · Sphere · Ring · RingArc · Arc · Rectangle.</param>
/// <param name="Values">Raw <c>EffectRangeValues</c>: <c>[offAcross, offForward, offZ, rotDeg, A, B, C…]</c>
/// — the client authors offsets in a Y-forward frame, so the FIRST value is the sideways one — where
/// A/B/C read per kind — see <see cref="ReplaySkillZone.Radius"/> and friends.</param>
/// <param name="NoticeMs">Telegraph pre-warning: the floor lights up this long before the hit.</param>
/// <param name="Anchor">Where the zone sits.</param>
public sealed record ReplaySkillZone(string Kind, IReadOnlyList<int> Values, int NoticeMs, ZoneAnchor Anchor)
{
    private int V(int i) => i < Values.Count ? Values[i] : 0;

    /// <summary>Local offset along the caster's facing (world units). The client's local frame is
    /// Y-forward: the forward component is the SECOND value, not the first. 검은 피 블라트's five-line
    /// sweep (1804570/1804580) is the measuring stick — its five parallel beams only tile into adjacent
    /// bands under this reading; the old <c>[forward, right]</c> guess stacked all four side lines onto
    /// one spot beside the boss.</summary>
    public double OffsetForward => V(1);

    /// <summary>Local offset to the caster's right (world units). The client's first axis points the
    /// caster's LEFT in this projection, hence the sign flip.</summary>
    public double OffsetRight => -V(0);

    /// <summary>Extra rotation of this zone on top of the caster's facing (degrees) — how a 4-way
    /// quadrant mechanic ships as four Arc rows at 0/90/180/270.</summary>
    public double RotationDeg => V(3);

    /// <summary>Circle/Sphere: radius · Ring/RingArc: OUTER radius · Arc: radius · Rectangle: length ·
    /// Triangle: reach (apex→base) · Cross: length of the bar along the facing.</summary>
    public double Radius => V(4);

    /// <summary>Ring/RingArc: inner radius (the safe hole). 0 for the other kinds.</summary>
    public double InnerRadius => Kind is "Ring" or "RingArc" ? V(5) : 0;

    /// <summary>Arc/RingArc/Triangle: the cone/wedge opening angle in degrees. 0 for the other kinds. The angle
    /// sits in a DIFFERENT slot per kind: an Arc/Triangle is <c>[…, radius(4), angle(5), height(6)]</c>, but a
    /// RingArc inserts the inner radius at slot 5 — <c>[…, outer(4), inner(5), height(6), angle(7)]</c> — so its
    /// angle is V(7). Reading V(5) for a RingArc grabs the inner radius (e.g. 800) as the angle: half=400° sweeps
    /// 800°, wraps past 360° and paints the whole donut instead of the real 25–180° slice — the reported
    /// over-sizing.</summary>
    public double AngleDeg => Kind is "Arc" or "Triangle" ? V(5) : Kind == "RingArc" ? V(7) : 0;

    /// <summary>Rectangle: width (the beam's thickness). 0 for the other kinds.</summary>
    public double Width => Kind == "Rectangle" ? V(5) : 0;

    /// <summary>Cross: length of the SECOND bar (across the facing). <see cref="Radius"/> is the first bar's
    /// length. 0 for the other kinds.</summary>
    public double CrossArmB => Kind == "Cross" ? V(5) : 0;

    /// <summary>Cross: thickness of the first bar (along the facing). A Cross row is
    /// <c>[…, lenA(4), lenB(5), height(6), widthA(7), widthB(8)]</c>. 0 for the other kinds.</summary>
    public double CrossWidthA => Kind == "Cross" ? V(7) : 0;

    /// <summary>Cross: thickness of the second bar (across the facing). 0 for the other kinds.</summary>
    public double CrossWidthB => Kind == "Cross" ? V(8) : 0;

    /// <summary>True when the shape's orientation matters (a cone, wedge, line, or cross) — i.e. it must be
    /// rotated by the caster's facing rather than drawn radially symmetric.</summary>
    public bool IsDirectional => Kind is "Arc" or "RingArc" or "Rectangle" or "Triangle" or "Cross";
}

/// <summary>
/// The bundled boss-mechanic zone catalog: skill code → the zone(s) it paints. Data-mined from the client
/// (<c>SkillEffectFilter</c>, joined arithmetically as <c>filterId = skillCode*10 + index</c>, so a
/// multi-stage mechanic — say an expanding shockwave — arrives as several zones in cast order).
/// <para>
/// Absent / malformed catalog is non-fatal: the replay just draws no zones. Loaded once at first use.
/// </para>
/// </summary>
public sealed class ReplaySkillShapes
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    private readonly Dictionary<int, IReadOnlyList<ReplaySkillZone>> _bySkill = new();

    // Base codes (skillCode with the last digit cleared) whose trailing-digit variants DIVERGE in shape kind —
    // e.g. 1230120 is a Rectangle but its sibling 1230125 is an Arc. For those families the trailing digit is
    // semantically meaningful (a different mechanic, not a mere stage/level), so guessing an ABSENT variant's
    // shape from the base would draw the wrong kind (a beam as a cone). The base-code fallback in For() is
    // refused for these; families that are internally consistent (all stages of one mechanic) still fall back.
    private readonly HashSet<int> _ambiguousBases = new();

    public int SkillCount => _bySkill.Count;

    private ReplaySkillShapes()
    {
    }

    /// <summary>An empty catalog (no zones) — the graceful fallback.</summary>
    public static ReplaySkillShapes Empty { get; } = new();

    /// <summary>Load from <c>{baseDir}/json/skill-shapes.json</c>. Never throws.</summary>
    public static ReplaySkillShapes Load(string? baseDir = null)
    {
        string path = Path.Combine(baseDir ?? AppContext.BaseDirectory, "json", "skill-shapes.json");
        return File.Exists(path) ? Parse(File.ReadAllText(path)) : Empty;
    }

    /// <summary>Parse the catalog JSON (exposed for tests). Never throws — a malformed file yields an
    /// empty catalog rather than breaking the replay window.</summary>
    public static ReplaySkillShapes Parse(string json)
    {
        var catalog = new ReplaySkillShapes();
        try
        {
            Dictionary<string, Entry[]>? raw = JsonSerializer.Deserialize<Dictionary<string, Entry[]>>(json, Options);
            if (raw is null)
            {
                return catalog;
            }

            foreach ((string code, Entry[] entries) in raw)
            {
                if (!int.TryParse(code, out int skill) || entries.Length == 0)
                {
                    continue;
                }

                var zones = new List<ReplaySkillZone>(entries.Length);
                foreach (Entry e in entries.OrderBy(e => e.I))
                {
                    if (e.T is null || e.V is null || e.V.Length < 5)
                    {
                        continue;
                    }

                    zones.Add(new ReplaySkillZone(e.T, e.V, e.N, e.A switch
                    {
                        "target" => ZoneAnchor.Target,
                        "targetLoc" => ZoneAnchor.TargetLocation,
                        _ => ZoneAnchor.Caster,
                    }));
                }

                if (zones.Count > 0)
                {
                    catalog._bySkill[skill] = zones;
                }
            }
        }
        catch
        {
            // a corrupt catalog must never break opening a replay
        }

        // Flag base-code families whose trailing-digit variants disagree on shape kind, so For() won't guess an
        // absent variant's shape from a base that is actually a different mechanic (see _ambiguousBases).
        var sigByBase = new Dictionary<int, string>();
        foreach ((int code, IReadOnlyList<ReplaySkillZone> zones) in catalog._bySkill)
        {
            int baseCode = code - (code % 10);
            string sig = string.Join(",", zones.Select(z => z.Kind));
            if (sigByBase.TryGetValue(baseCode, out string? existing))
            {
                if (existing != sig)
                {
                    catalog._ambiguousBases.Add(baseCode);
                }
            }
            else
            {
                sigByBase[baseCode] = sig;
            }
        }

        return catalog;
    }

    /// <summary>
    /// The zones a skill paints, in cast order. Empty when the skill paints none — a plain swing, a self
    /// buff — or when the client defines no zone for it at all.
    /// <para>
    /// A cast often carries a VARIANT of a mechanic (a stage or level: 1803661 of 1803660, 1805713 of
    /// 1805710) and the client only defines the zone once, on the base. So an unknown code falls back to its
    /// base — the same code with the last digit cleared. Measured on a real 침식의 정화소 fight, that alone
    /// recovers 36 of the 52 casts that were drawing nothing. The fallback is REFUSED when the base's family
    /// disagrees on shape kind (see <see cref="_ambiguousBases"/>), so a beam is never guessed as a cone.
    /// </para>
    /// </summary>
    public IReadOnlyList<ReplaySkillZone> For(int skillCode)
    {
        if (_bySkill.TryGetValue(skillCode, out IReadOnlyList<ReplaySkillZone>? zones))
        {
            return zones;
        }

        int baseCode = skillCode - (skillCode % 10);
        return baseCode != skillCode
               && !_ambiguousBases.Contains(baseCode) // don't inherit a base that's a different-kind mechanic
               && _bySkill.TryGetValue(baseCode, out IReadOnlyList<ReplaySkillZone>? shared)
            ? shared
            : [];
    }

    private sealed class Entry
    {
        public int I { get; set; }
        public string? T { get; set; }
        public int[]? V { get; set; }
        public int N { get; set; }
        public string? A { get; set; }
    }
}

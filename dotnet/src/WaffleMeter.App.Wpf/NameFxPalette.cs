using System.Windows.Media;

namespace WaffleMeter.App.Wpf;

/// <summary>
/// One nickname effect's finished brush for one skin. Mirrors <see cref="TierBadge"/>: the row view-model
/// carries a single reference, so the per-tick row rebuild allocates nothing.
/// <para><see cref="None"/> is what every row gets when the feature is off or the character has no grant. Its
/// <see cref="NameFill"/> is never read — the row keeps its own <c>NameBrush</c>, so the meter stays
/// pixel-identical to a build without this feature.</para>
/// </summary>
public sealed class NameFxBadge
{
    public static readonly NameFxBadge None = new() { IsNone = true };

    public bool IsNone { get; init; }

    public string Id { get; init; } = string.Empty;

    /// <summary>Replaces the name's Foreground. The <c>[서버]</c> tag deliberately keeps the original brush, so
    /// an effect never erases the faction colour the rest of the row is read by.</summary>
    public Brush NameFill { get; init; } = Brushes.Transparent;

    /// <summary>True when <see cref="NameFxSheen"/> must keep this brush moving.</summary>
    public bool Animated { get; init; }
}

/// <summary>
/// The nickname effect catalogue, as code constants — same reasoning as <see cref="TierPalette"/>: no PNG
/// frames (the meter root carries a 75~130% LayoutTransform under <c>RenderMode.SoftwareOnly</c>, so bitmaps
/// blur and a missing asset fails silently) and no <c>Skin.*.xaml</c> keys (four palettes would have to carry
/// the same key set and one omission falls back silently).
/// <para><b>Two families, told apart twice over.</b> Supporter effects are warm and flow continuously; ranker
/// effects are cold metal and flash briefly with a long rest. Colour alone would not survive a colour-blind
/// viewer or a heavily tinted skin, so the motion differs too. A supporter can never select a ranker effect and
/// vice versa — the families are separate tables, not tiers of one scale.</para>
/// <para><b>Why no blur/glow.</b> The overlay is an <c>AllowsTransparency</c> layered window rendered in
/// software (<c>App.xaml.cs</c>), so <c>BlurEffect</c>/<c>DropShadowEffect</c> would be CPU convolution over a
/// running game. The original "glow" idea is split into brightness pulse (<c>butter</c>) and a travelling
/// highlight band (<c>syrup</c>), which cost a gradient each.</para>
/// </summary>
public static class NameFxPalette
{
    /// <summary>A catalogue entry: what the effect is called, which family it belongs to, and its stops.</summary>
    /// <param name="IsGauge">True for the DPS gauge skins. They live in the same table because they share every
    /// mechanism (shared animated brush, brightness, speed, the demand clock) and differ only in where they are
    /// painted — but they are never offered as a nickname effect, and vice versa.
    /// <para>Both entitlement families have gauge skins; <see cref="Effect.Kind"/> is what decides who may pick
    /// which, exactly as it does for the nickname effects.</para></param>
    /// <param name="Motion">Gauge skins only — how the brush moves. Ignored by nickname effects, which are
    /// drawn on text where anything but a soft sweep turns the glyphs into stripes.</param>
    /// <param name="SpeedScale">Multiplies this skin's own period. &gt;1 is slower. Part of the identity: two
    /// skins with the same texture at visibly different speeds still read as two effects.</param>
    /// <param name="Reverse">Travel right-to-left. Cheapest possible differentiator and surprisingly strong
    /// when two skins are on screen together.</param>
    /// <param name="Bands"><see cref="GaugeMotion.Shimmer"/> only — how many highlights share one tile.
    /// Density is what keeps two shimmer skins apart.</param>
    public sealed record Effect(
        string Id,
        string Name,
        NameFxKind Kind,
        bool Animated,
        string[] Dark,
        string[] Light,
        bool IsGauge = false,
        double[]? Offsets = null,
        GaugeMotion Motion = GaugeMotion.Sweep,
        double SpeedScale = 1.0,
        bool Reverse = false,
        int Bands = 3);

    public enum NameFxKind
    {
        Supporter,
        Ranker,
    }

    /// <summary>
    /// How a gauge skin MOVES. Colour was doing all the work: every skin was one soft band sliding left to
    /// right at one speed, so five skins read as one effect in five colours.
    /// <para>Everything here is still a single gradient brush translated by one animation — no blur, no glow,
    /// no bitmap frames. Those are ruled out by the overlay being a software-rendered layered window over a
    /// running game (see the type remarks), and nothing below costs more than the sweep it replaces.</para>
    /// </summary>
    public enum GaugeMotion
    {
        /// <summary>One soft highlight travelling along the bar. The original, kept for the skin whose
        /// identity is "smooth".</summary>
        Sweep,

        /// <summary>Hard-edged diagonal bands marching across — no gradient between colours, so it reads as
        /// cut glass rather than a glow.</summary>
        Chevron,

        /// <summary>Several narrow highlights per tile: the bar glitters instead of pulsing once.
        /// <para>Band placement is deliberately IRREGULAR. Evenly spaced bands make the tile N-fold symmetric,
        /// so its real period is 1/N and the pattern cycles N times per animation — which the preview harness
        /// catches as "does not travel": phase 0.25 of four even bands is pixel-identical to phase 0.</para>
        /// </summary>
        Shimmer,
    }

    // ⚠ A round travelling hotspot was tried and REMOVED, not forgotten. A RadialGradientBrush repeats
    // CONCENTRICALLY under SpreadMethod.Repeat — there is no horizontal lattice — so translating it by one
    // period does not reproduce the pattern, and the loop shows a seam (measured 47.7/255 mean channel
    // distance against a 4/255 budget). Forcing x-periodicity by making RadiusY huge only turns it back into
    // stripes. Every motion here has to be a translation of an x-periodic tile, and a radial one is not.

    /// <summary>
    /// <para><b>Light 변형은 어둡고 진하게.</b> 라이트 스킨의 행 배경은 거의 흰색이라, 다크용 색을 그대로
    /// 옮기거나 중간톤으로 두면 글자가 배경에 묻힌다 — 실제로 금박·백금·다이아몬드·바삭한 결이 그렇게
    /// 안 보였다. 라이트 쪽 스톱은 '밝은 하이라이트'가 아니라 '중간톤 하이라이트'로 잡는다.</para>
    /// Ordered for display. Ids are a wire contract — the roster artifact names them, so renaming one silently
    /// drops every grant that used it. Add, never rename.
    /// </summary>
    public static readonly Effect[] All =
    {
        // 애니메이션 계열의 대비는 '읽히는가'로 정했다. 첫 시도는 베이스와 하이라이트가 가까워
        // (최대 채널 변화 60/255) 띠가 지나가는 게 사실상 안 보였다 — 정지 그라디언트와 구분이 안 됐다.
        // 베이스를 깊게 내려 잡는 쪽으로 벌렸다: 하이라이트를 더 밝히면 흰색에 붙어 색 정체성이 날아간다.
        new("syrup", "시럽 흐름", NameFxKind.Supporter, true,
            new[] { "#FFD9791A", "#FFFFF0C2", "#FFD9791A" },
            new[] { "#FF7A3F00", "#FFC97A12", "#FF7A3F00" }),
        new("butter", "버터 글로우", NameFxKind.Supporter, true,
            new[] { "#FFE9A83A", "#FFFFFBEA", "#FFE9A83A" },
            new[] { "#FF6E4A05", "#FFB8862A", "#FF6E4A05" }),
        new("berry", "베리 드리즐", NameFxKind.Supporter, true,
            new[] { "#FFE0489C", "#FFCDBBFF", "#FFE0489C" },
            new[] { "#FF8E0A4E", "#FF4A2AA8", "#FF8E0A4E" }),
        new("crisp", "바삭한 결", NameFxKind.Supporter, false,
            new[] { "#FFFFC978", "#FFFFEFC8" },
            new[] { "#FF8A5000", "#FFB77A1E" }),

        // ── 랭커 계열 ─────────────────────────────────────────────────────────────
        // 다섯이 서로 '한눈에' 갈려야 한다. 금속 둘(금박·백금)만으로는 나머지가 전부 은빛으로 수렴하므로
        // (실제로 첫 판의 '각인'은 백금과 구분이 안 됐다) 축을 색상환으로 벌렸다 —
        // 따뜻한 금속 / 중성 금속 / 무지개 / 불 / 얼음. 백금은 파란 기를 빼 중성 회백으로 내려
        // 빙결과 겹치지 않게 했다.
        new("goldleaf", "금박", NameFxKind.Ranker, true,
            new[] { "#FFBFA24E", "#FFFFFDF0", "#FFBFA24E" },
            new[] { "#FF5A4A10", "#FFA38F42", "#FF5A4A10" }),
        new("platinum", "백금", NameFxKind.Ranker, true,
            new[] { "#FF8A9099", "#FFF2F5F8", "#FF8A9099" },
            new[] { "#FF3A3F47", "#FF6E747C", "#FF3A3F47" }),
        // 무지개 파편이 지나간다. 흰빛만으로는 백금과 또 겹치므로 시안·보라 프린지를 좁게 끼운다.
        new("diamond", "다이아몬드", NameFxKind.Ranker, true,
            new[] { "#FF7FD8F5", "#FFFFFFFF", "#FFD8BFFF", "#FFFFFFFF", "#FF7FD8F5" },
            new[] { "#FF1D6E8A", "#FF4A7E95", "#FF4A2E9E", "#FF4A7E95", "#FF1D6E8A" },
            Offsets: new[] { 0.0, 0.42, 0.5, 0.58, 1.0 }),
        new("flame", "화염", NameFxKind.Ranker, true,
            new[] { "#FFB3220E", "#FFFF7A1A", "#FFFFE08A", "#FFFF7A1A", "#FFB3220E" },
            new[] { "#FF8C1A08", "#FFDD5A05", "#FFF5AE3C", "#FFDD5A05", "#FF8C1A08" },
            Offsets: new[] { 0.0, 0.40, 0.5, 0.60, 1.0 }),
        new("glacier", "빙결", NameFxKind.Ranker, true,
            new[] { "#FF1B6FA8", "#FF5FD8F5", "#FFEAFBFF", "#FF5FD8F5", "#FF1B6FA8" },
            new[] { "#FF115882", "#FF2A9CC4", "#FF8FD6EC", "#FF2A9CC4", "#FF115882" },
            Offsets: new[] { 0.0, 0.40, 0.5, 0.60, 1.0 }),

        // ── 후원자 DPS 게이지 스킨 ─────────────────────────────────────────────────
        // 랭커 게이지 셋(프리즘=청록+보라 / 잔불=적주황 / 서리=파랑)이 이미 차 있어서, 새 둘은 **비어 있는
        // 색상 영역**으로 잡았다. 자홍과 녹색이 그 둘이다 — 특히 녹색은 닉네임 연출까지 통틀어 팔레트에
        // 한 번도 쓰인 적이 없어 한눈에 갈린다. 따뜻한 주황 쪽으로 잡고 싶은 유혹이 있었지만(후원자 연출이
        // 시럽·버터라) 0.45 불투명도 뒤에서는 잔불과 구분이 안 된다.
        //
        // 하이라이트 띠는 랭커보다 **조금 넓다**(타일의 32% 대 20%). 후원자 연출이 '흐른다', 랭커가
        // '번쩍인다'인 것과 같은 축이다 — 다만 폭을 균등 배치(50%)까지 벌리지는 않았다. 그건 이미 한 번
        // 실패한 값이고, 넓은 띠는 '지나간다'가 아니라 '막대가 그냥 밝다'로 읽힌다.
        //
        // ⚠️ 게이지는 계열이 **모션으로는 갈리지 않는다**(모든 게이지가 같은 주기·같은 속도를 쓴다).
        // 여기서 계열은 '누가 고를 수 있는가'라는 자격 경계이지, 보는 사람이 후원자와 랭커를 구분하라는
        // 신호가 아니다. 그 구분이 필요한 자리는 닉네임 쪽이고, 거기서는 모션이 실제로 다르다.
        // 베리 = 굵은 물방울 셋이 흐른다. 잔불과 같은 shimmer 지만 밀도(3 대 6)와 속도가 갈린다.
        new("berryglaze", "베리 글레이즈", NameFxKind.Supporter, true,
            new[] { "#FF831843", "#FFEC4899", "#FFFFE4F2", "#FFEC4899", "#FF831843" },
            new[] { "#FF7A1038", "#FFD32B7E", "#FFFBD6E8", "#FFD32B7E", "#FF7A1038" },
            IsGauge: true, Offsets: new[] { 0.0, 0.34, 0.5, 0.66, 1.0 },
            Motion: GaugeMotion.Shimmer, SpeedScale: 1.15, Bands: 3),
        // 말차 = 이름값대로 '흐른다'. 원래의 부드러운 쓸기를 유지하되 가장 느리게 — 대비군의 기준점.
        new("matcha", "말차 크림", NameFxKind.Supporter, true,
            new[] { "#FF14532D", "#FF4ADE80", "#FFF0FFF4", "#FF4ADE80", "#FF14532D" },
            new[] { "#FF10461F", "#FF2FA85B", "#FFDFF6E4", "#FF2FA85B", "#FF10461F" },
            IsGauge: true, Offsets: new[] { 0.0, 0.34, 0.5, 0.66, 1.0 },
            Motion: GaugeMotion.Sweep, SpeedScale: 1.4),

        // ── 랭커 DPS 게이지 스킨 ───────────────────────────────────────────────────
        // 첫 판은 채도를 낮추고 스톱을 균등 배치했다가 완전히 실패했다. 게이지는 채움 불투명도 0.3 뒤에
        // 깔리는데, 그 아래에서 옅은 색을 균등하게 펴 놓으면 '스킨을 받았다'가 아니라 '막대가 좀 탁하다'로
        // 보인다. 세 축을 같이 올렸다:
        //   ① 채도 — 기본 게이지(테마 그라디언트)도 0.3 뒤에서 읽히는 건 색이 진하기 때문이다.
        //   ② 하이라이트 폭 — 오프셋을 명시해 밝은 띠를 타일의 1/3 에서 ~1/6 로 좁혔다. 좁고 밝은 띠는
        //      '지나간다'로 읽히고, 넓고 밝은 띠는 그냥 배경이 밝아진 것으로 읽힌다.
        //   ③ 불투명도 — 스킨을 받은 행만 0.45 로 올린다(RowViewModel.GaugeOpacity). 기본 0.3 은 그대로다.
        // 딜 숫자는 이 위에 Skin.Fg(거의 흰색)로 그려지므로 좁은 띠가 지나가도 가독성은 유지된다.
        // 프리즘 = 각진 대각 띠가 행진한다. 색 사이 그라디언트가 없어 '유리 조각'으로 읽힌다.
        new("prism", "프리즘", NameFxKind.Ranker, true,
            new[] { "#FF0E7490", "#FF22D3EE", "#FFEAFBFF", "#FFA855F7", "#FF0E7490" },
            new[] { "#FF0B5F78", "#FF0EA5E9", "#FFD8F4FF", "#FF7C3AED", "#FF0B5F78" },
            IsGauge: true, Offsets: new[] { 0.0, 0.38, 0.5, 0.62, 1.0 },
            Motion: GaugeMotion.Chevron, SpeedScale: 0.85),
        // 잔불 = 불티 여럿이 빠르게 지나간다. 좁은 밴드 4개 + 가장 빠른 속도.
        new("ember", "잔불", NameFxKind.Ranker, true,
            new[] { "#FF7F1D1D", "#FFEA580C", "#FFFFEFC0", "#FFEA580C", "#FF7F1D1D" },
            new[] { "#FF8C2A12", "#FFDD4F09", "#FFFFE3A8", "#FFDD4F09", "#FF8C2A12" },
            IsGauge: true, Offsets: new[] { 0.0, 0.40, 0.5, 0.60, 1.0 },
            Motion: GaugeMotion.Shimmer, SpeedScale: 0.6, Bands: 6),
        // 서리 = 결이 반대로 천천히 흐른다. 텍스처는 말차와 같은 쓸기지만 방향과 속도가 반대라
        // 나란히 놓아도 같은 효과로 안 보인다.
        new("frost", "서리", NameFxKind.Ranker, true,
            new[] { "#FF1E3A8A", "#FF38BDF8", "#FFF2FCFF", "#FF38BDF8", "#FF1E3A8A" },
            new[] { "#FF1B347C", "#FF1D9BE0", "#FFDDF4FF", "#FF1D9BE0", "#FF1B347C" },
            // 좁은 글린트 + 역방향. 말차와 같은 쓸기지만 띠 폭이 1/3 이고 방향이 반대라 나란히 놓아도
            // 같은 효과로 안 보인다.
            IsGauge: true, Offsets: new[] { 0.0, 0.455, 0.5, 0.545, 1.0 },
            Motion: GaugeMotion.Sweep, SpeedScale: 1.25, Reverse: true),
    };

    /// <summary>Nickname effects only — what the picker and the settings preview strip offer.</summary>
    public static readonly Effect[] NameEffects = All.Where(e => !e.IsGauge).ToArray();

    /// <summary>DPS gauge skins, both families. Ordered 후원자 then 랭커, matching the nickname list.</summary>
    public static readonly Effect[] GaugeSkins = All.Where(e => e.IsGauge).ToArray();

    private static readonly Dictionary<string, Effect> ById =
        All.ToDictionary(e => e.Id, StringComparer.Ordinal);

    public static bool IsKnown(string? id) => id is not null && ById.ContainsKey(id);

    /// <summary>Accepts NICKNAME effect ids only — a gauge id here would paint a bar-sized gradient across a
    /// nickname, and the single "is it in the catalogue" check used to let exactly that through.</summary>
    public static bool IsKnownNameEffect(string? id) => Find(id) is { IsGauge: false };

    /// <summary>Accepts GAUGE skin ids only.</summary>
    public static bool IsKnownGauge(string? id) => Find(id) is { IsGauge: true };

    /// <summary>
    /// The nickname effects an entitlement may choose from.
    /// <para><paramref name="kind"/> is the roster entry's <c>k</c> — the server's word on what this character
    /// is entitled to, not a guess made here. <c>both</c> is the union: a supporter who is also a ranker picks
    /// from everything.</para>
    /// </summary>
    public static IReadOnlyList<Effect> ChoicesFor(string? kind) => kind switch
    {
        "supporter" => NameEffects.Where(e => e.Kind == NameFxKind.Supporter).ToArray(),
        "ranker" => NameEffects.Where(e => e.Kind == NameFxKind.Ranker).ToArray(),
        "both" => NameEffects,
        _ => Array.Empty<Effect>(),
    };

    /// <summary>The gauge skins an entitlement may choose from — the same rule as
    /// <see cref="ChoicesFor"/>, on the same <c>k</c> from the roster. Written as the same shape deliberately:
    /// while gauges were a ranker-only extra this read <c>kind is "ranker" or "both"</c>, and adding a
    /// supporter gauge to the table without changing it would have offered nobody the new skins while quietly
    /// letting a ranker pick them.</summary>
    public static IReadOnlyList<Effect> GaugeChoicesFor(string? kind) => kind switch
    {
        "supporter" => GaugeSkins.Where(e => e.Kind == NameFxKind.Supporter).ToArray(),
        "ranker" => GaugeSkins.Where(e => e.Kind == NameFxKind.Ranker).ToArray(),
        "both" => GaugeSkins,
        _ => Array.Empty<Effect>(),
    };

    /// <summary>A gauge skin's fill, or null when the id is not a gauge skin this build knows. Family-agnostic —
    /// the caller has already checked the grant; this only maps id to paint.</summary>
    public static Brush? GaugeBrush(string? id, bool isLight)
    {
        Effect? e = Find(id);
        return e is { IsGauge: true } ? For(e.Id, isLight).NameFill : null;
    }

    public static Effect? Find(string? id) => id is not null && ById.TryGetValue(id, out Effect? e) ? e : null;

    /// <summary>The finished badge for an effect id, or <see cref="NameFxBadge.None"/> when unknown. Static
    /// effects get a frozen brush; animated ones share <see cref="NameFxSheen"/>'s live instance.</summary>
    public static NameFxBadge For(string? id, bool isLight)
    {
        Effect? e = Find(id);
        if (e is null)
        {
            return NameFxBadge.None;
        }

        return isLight ? LightBadges.Value[e.Id] : DarkBadges.Value[e.Id];
    }

    /// <summary>
    /// The same effect with the motion taken out, for users who keep effects on but movement off.
    /// <para>Built from the effect's OWN colours rather than swapping the character onto a shared "still"
    /// entry. The first version mapped every supporter to <c>crisp</c> and every ranker to <c>edge</c>, which
    /// threw away the thing the grant is for — two rankers with visibly different marks both collapsed to the
    /// same silver, and that shared still entry then read as a near-duplicate of <c>platinum</c> in the picker.
    /// Keeping the colours means "색상만" is literally what it says.</para>
    /// </summary>
    public static NameFxBadge StillVariant(string? id, bool isLight)
    {
        Effect? e = Find(id);
        if (e is null)
        {
            return NameFxBadge.None;
        }

        if (!e.Animated)
        {
            return For(e.Id, isLight); // already still
        }

        return (isLight ? LightStills : DarkStills).Value[e.Id];
    }

    private static readonly Lazy<Dictionary<string, NameFxBadge>> DarkStills = new(() => BuildStills(light: false));
    private static readonly Lazy<Dictionary<string, NameFxBadge>> LightStills = new(() => BuildStills(light: true));

    private static Dictionary<string, NameFxBadge> BuildStills(bool light)
    {
        var map = new Dictionary<string, NameFxBadge>(StringComparer.Ordinal);
        foreach (Effect e in All.Where(x => x.Animated))
        {
            // Base colour to peak colour, once across the text. No repeat, no transform — a still gradient that
            // still says which effect this is.
            string[] hex = light ? e.Light : e.Dark;
            var b = new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(1, 1) };
            b.GradientStops.Add(new GradientStop(Parse(hex[0]), 0.0));
            b.GradientStops.Add(new GradientStop(Parse(hex[hex.Length / 2]), 1.0));
            b.Freeze();
            map[e.Id] = new NameFxBadge { Id = e.Id, Animated = false, NameFill = b };
        }

        return map;
    }

    private static readonly Lazy<Dictionary<string, NameFxBadge>> DarkBadges = new(() => Build(light: false));
    private static readonly Lazy<Dictionary<string, NameFxBadge>> LightBadges = new(() => Build(light: true));

    private static Dictionary<string, NameFxBadge> Build(bool light)
    {
        var map = new Dictionary<string, NameFxBadge>(StringComparer.Ordinal);
        foreach (Effect e in All)
        {
            map[e.Id] = new NameFxBadge
            {
                Id = e.Id,
                Animated = e.Animated,
                NameFill = e.Animated ? NameFxSheen.BrushFor(e.Id, light) : Frozen(e, light),
            };
        }

        return map;
    }

    private static Brush Frozen(Effect e, bool light)
    {
        var b = new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(1, 1) };
        AddStops(b, e, light);
        b.Freeze();
        return b;
    }

    internal static void AddStops(GradientBrush brush, Effect e, bool light)
    {
        string[] hex = light ? e.Light : e.Dark;
        Color[] c = new Color[hex.Length];
        for (int i = 0; i < hex.Length; i++)
        {
            c[i] = Parse(hex[i]);
        }

        double At(int i) => e.Offsets is { } o && o.Length == hex.Length
            ? o[i]
            : hex.Length == 1 ? 0 : i / (double)(hex.Length - 1);

        switch (e.IsGauge ? e.Motion : GaugeMotion.Sweep)
        {
            case GaugeMotion.Chevron:
            {
                // Two stops per colour, at the band's own edges: identical colour across the band and an
                // instant jump at the seam. A gradient between them would smear the edge back into a sweep.
                //
                // The effect's own Offsets are IGNORED here, and the colour sequence runs twice per tile.
                // Those offsets are shaped for a sweep — a wide base at each end and a narrow highlight in the
                // middle — which as hard bands means two thirds of the bar is one flat colour block. Even,
                // doubled bands are what makes it read as facets rather than as a bar someone painted purple.
                const int Repeats = 2;
                int bands = (c.Length - 1) * Repeats;
                for (int b = 0; b < bands; b++)
                {
                    Color band = c[b % (c.Length - 1)];
                    brush.GradientStops.Add(new GradientStop(band, b / (double)bands));
                    brush.GradientStops.Add(new GradientStop(band, (b + 1) / (double)bands));
                }

                break;
            }

            case GaugeMotion.Shimmer:
            {
                // Several narrow highlights instead of one wide one. Built from the effect's own base and peak
                // so the colour identity survives; the tile is what changes, not the palette.
                //
                // The centres are NOT evenly spaced — see the enum docs. The nudges are a fixed table rather
                // than Random because these brushes are rebuilt on every brightness change and have to come
                // out identical each time.
                int bands = Math.Clamp(e.Bands, 2, 8);
                const double Half = 0.035;
                double[] nudge = { 0.045, -0.030, 0.020, -0.045, 0.035, -0.015, 0.040, -0.025 };
                Color baseColor = c[0];
                Color peak = c[c.Length / 2];
                Color mid = c[Math.Max(0, (c.Length / 2) - 1)];

                brush.GradientStops.Add(new GradientStop(baseColor, 0.0));
                for (int b = 0; b < bands; b++)
                {
                    double centre = Math.Clamp(
                        ((b + 0.5) / bands) + nudge[b % nudge.Length], Half + 0.01, 1.0 - Half - 0.01);

                    // Alternating peak / mid brightness so the bands are not clones of one another either.
                    brush.GradientStops.Add(new GradientStop(baseColor, centre - Half));
                    brush.GradientStops.Add(new GradientStop(b % 2 == 0 ? peak : mid, centre));
                    brush.GradientStops.Add(new GradientStop(baseColor, centre + Half));
                }

                brush.GradientStops.Add(new GradientStop(baseColor, 1.0));
                break;
            }

            default:
                for (int i = 0; i < c.Length; i++)
                {
                    // Even spacing unless the effect asks otherwise. Spacing matters more than it sounds: evenly
                    // spread stops make the bright band a third of the tile wide, which reads as "the whole thing
                    // is pale" rather than "a highlight went past". The gauge skins therefore pin their offsets.
                    brush.GradientStops.Add(new GradientStop(c[i], At(i)));
                }

                break;
        }
    }

    /// <summary>
    /// Brightness is applied in HSL lightness, not by multiplying RGB. A channel multiply washes the hue out at
    /// the top of the range — the same slider on the meter we compared against turns its reds pink at 130%.
    /// </summary>
    internal static Color Scale(Color c, double factor)
    {
        if (Math.Abs(factor - 1.0) < 0.001)
        {
            return c;
        }

        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double l = (max + min) / 2.0;
        double target = Math.Clamp(l * factor, 0.0, 1.0);
        if (max - min < 0.0001)
        {
            byte v = (byte)Math.Round(target * 255);
            return Color.FromArgb(c.A, v, v, v);
        }

        // Move every channel toward white/black by the same lightness delta, which preserves the hue ratio.
        double t = target > l ? (target - l) / (1.0 - l) : (target - l) / l;
        byte Mix(double ch) => (byte)Math.Round(Math.Clamp(t > 0 ? ch + (1.0 - ch) * t : ch * (1.0 + t), 0.0, 1.0) * 255);
        return Color.FromArgb(c.A, Mix(r), Mix(g), Mix(b));
    }

    private static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex)!;
}

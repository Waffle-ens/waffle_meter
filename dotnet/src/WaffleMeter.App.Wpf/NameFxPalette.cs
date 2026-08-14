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
    /// <param name="IsGauge">True for the ranker-only DPS gauge skins. They live in the same table because they
    /// share every mechanism (shared animated brush, brightness, speed, the demand clock) and differ only in
    /// where they are painted — but they are never offered as a nickname effect, and vice versa.</param>
    public sealed record Effect(
        string Id,
        string Name,
        NameFxKind Kind,
        bool Animated,
        string[] Dark,
        string[] Light,
        bool IsGauge = false,
        double[]? Offsets = null);

    public enum NameFxKind
    {
        Supporter,
        Ranker,
    }

    /// <summary>
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
            new[] { "#FF8F5205", "#FFE7B45B", "#FF8F5205" }),
        new("butter", "버터 글로우", NameFxKind.Supporter, true,
            new[] { "#FFE9A83A", "#FFFFFBEA", "#FFE9A83A" },
            new[] { "#FF8A5C08", "#FFE0B25E", "#FF8A5C08" }),
        new("berry", "베리 드리즐", NameFxKind.Supporter, true,
            new[] { "#FFE0489C", "#FFCDBBFF", "#FFE0489C" },
            new[] { "#FF9B0E58", "#FF5B37C4", "#FF9B0E58" }),
        new("crisp", "바삭한 결", NameFxKind.Supporter, false,
            new[] { "#FFFFC978", "#FFFFEFC8" },
            new[] { "#FFA96A12", "#FFCE9A45" }),

        new("goldleaf", "금박", NameFxKind.Ranker, true,
            new[] { "#FFBFA24E", "#FFFFFDF0", "#FFBFA24E" },
            new[] { "#FF6B591A", "#FFCDB86A", "#FF6B591A" }),
        new("platinum", "백금", NameFxKind.Ranker, true,
            new[] { "#FF7FA3C0", "#FFF4FAFF", "#FF7FA3C0" },
            new[] { "#FF33526E", "#FF9DB8CE", "#FF33526E" }),
        // 기본 닉네임 색이 흰색 계열이라, 여기서 더 옅으면 '연출'이 아니라 그냥 흰 이름으로 읽힌다.
        new("edge", "각인", NameFxKind.Ranker, false,
            new[] { "#FF93A7BE", "#FFE2ECF8" },
            new[] { "#FF3B5470", "#FF7089A4" }),

        // ── 랭커 전용 DPS 게이지 스킨 ──────────────────────────────────────────────
        // 첫 판은 채도를 낮추고 스톱을 균등 배치했다가 완전히 실패했다. 게이지는 채움 불투명도 0.3 뒤에
        // 깔리는데, 그 아래에서 옅은 색을 균등하게 펴 놓으면 '스킨을 받았다'가 아니라 '막대가 좀 탁하다'로
        // 보인다. 세 축을 같이 올렸다:
        //   ① 채도 — 기본 게이지(테마 그라디언트)도 0.3 뒤에서 읽히는 건 색이 진하기 때문이다.
        //   ② 하이라이트 폭 — 오프셋을 명시해 밝은 띠를 타일의 1/3 에서 ~1/6 로 좁혔다. 좁고 밝은 띠는
        //      '지나간다'로 읽히고, 넓고 밝은 띠는 그냥 배경이 밝아진 것으로 읽힌다.
        //   ③ 불투명도 — 스킨을 받은 행만 0.45 로 올린다(RowViewModel.GaugeOpacity). 기본 0.3 은 그대로다.
        // 딜 숫자는 이 위에 Skin.Fg(거의 흰색)로 그려지므로 좁은 띠가 지나가도 가독성은 유지된다.
        new("prism", "프리즘", NameFxKind.Ranker, true,
            new[] { "#FF0E7490", "#FF22D3EE", "#FFEAFBFF", "#FFA855F7", "#FF0E7490" },
            new[] { "#FF0B5F78", "#FF0EA5E9", "#FFD8F4FF", "#FF7C3AED", "#FF0B5F78" },
            IsGauge: true, Offsets: new[] { 0.0, 0.38, 0.5, 0.62, 1.0 }),
        new("ember", "잔불", NameFxKind.Ranker, true,
            new[] { "#FF7F1D1D", "#FFEA580C", "#FFFFEFC0", "#FFEA580C", "#FF7F1D1D" },
            new[] { "#FF8C2A12", "#FFDD4F09", "#FFFFE3A8", "#FFDD4F09", "#FF8C2A12" },
            IsGauge: true, Offsets: new[] { 0.0, 0.40, 0.5, 0.60, 1.0 }),
        new("frost", "서리", NameFxKind.Ranker, true,
            new[] { "#FF1E3A8A", "#FF38BDF8", "#FFF2FCFF", "#FF38BDF8", "#FF1E3A8A" },
            new[] { "#FF1B347C", "#FF1D9BE0", "#FFDDF4FF", "#FF1D9BE0", "#FF1B347C" },
            IsGauge: true, Offsets: new[] { 0.0, 0.40, 0.5, 0.60, 1.0 }),
    };

    /// <summary>Nickname effects only — what the picker and the settings preview strip offer.</summary>
    public static readonly Effect[] NameEffects = All.Where(e => !e.IsGauge).ToArray();

    /// <summary>Ranker-only DPS gauge skins.</summary>
    public static readonly Effect[] GaugeSkins = All.Where(e => e.IsGauge).ToArray();

    private static readonly Dictionary<string, Effect> ById =
        All.ToDictionary(e => e.Id, StringComparer.Ordinal);

    public static bool IsKnown(string? id) => id is not null && ById.ContainsKey(id);

    /// <summary>Accepts NICKNAME effect ids only — a gauge id here would paint a bar-sized gradient across a
    /// nickname, and the single "is it in the catalogue" check used to let exactly that through.</summary>
    public static bool IsKnownNameEffect(string? id) => Find(id) is { IsGauge: false };

    /// <summary>Accepts GAUGE skin ids only.</summary>
    public static bool IsKnownGauge(string? id) => Find(id) is { IsGauge: true };

    /// <summary>A gauge skin's fill, or null when the id is not a gauge skin this build knows. Ranker-only —
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
    /// The still member of the same family, for users who keep effects on but motion off. Downgrading rather
    /// than switching off matters: the mark is the whole point of the grant, and someone who dislikes moving
    /// pixels should not have to make supporters invisible to see a calm meter.
    /// </summary>
    public static NameFxBadge StillVariant(string? id, bool isLight)
    {
        Effect? e = Find(id);
        if (e is null)
        {
            return NameFxBadge.None;
        }

        string stillId = e.Kind == NameFxKind.Ranker ? "edge" : "crisp";
        return For(stillId, isLight);
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

    internal static void AddStops(LinearGradientBrush brush, Effect e, bool light)
    {
        string[] hex = light ? e.Light : e.Dark;
        for (int i = 0; i < hex.Length; i++)
        {
            // Even spacing unless the effect asks otherwise. Spacing matters more than it sounds: evenly spread
            // stops make the bright band a third of the tile wide, which reads as "the whole thing is pale"
            // rather than "a highlight went past". The gauge skins therefore pin their own offsets.
            double offset = e.Offsets is { } o && o.Length == hex.Length
                ? o[i]
                : hex.Length == 1 ? 0 : i / (double)(hex.Length - 1);
            brush.GradientStops.Add(new GradientStop(Parse(hex[i]), offset));
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

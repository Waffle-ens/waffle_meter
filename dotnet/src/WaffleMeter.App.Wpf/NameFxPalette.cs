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
    public sealed record Effect(
        string Id,
        string Name,
        NameFxKind Kind,
        bool Animated,
        string[] Dark,
        string[] Light);

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
        new("syrup", "시럽 흐름", NameFxKind.Supporter, true,
            new[] { "#FFF6B24A", "#FFFFE3A3", "#FFF6B24A" },
            new[] { "#FFB4700C", "#FFE2A33A", "#FFB4700C" }),
        new("butter", "버터 글로우", NameFxKind.Supporter, true,
            new[] { "#FFFFD98A", "#FFFFF6DC", "#FFFFD98A" },
            new[] { "#FFA9761A", "#FFD9A94C", "#FFA9761A" }),
        new("berry", "베리 드리즐", NameFxKind.Supporter, true,
            new[] { "#FFF472B6", "#FFC4B5FD", "#FFF472B6" },
            new[] { "#FFB1236B", "#FF6D4FCF", "#FFB1236B" }),
        new("crisp", "바삭한 결", NameFxKind.Supporter, false,
            new[] { "#FFFFC978", "#FFFFEFC8" },
            new[] { "#FFA96A12", "#FFCE9A45" }),

        new("goldleaf", "금박", NameFxKind.Ranker, true,
            new[] { "#FFE8D9A0", "#FFFFFDF0", "#FFE8D9A0" },
            new[] { "#FF8A7628", "#FFC0A94F", "#FF8A7628" }),
        new("platinum", "백금", NameFxKind.Ranker, true,
            new[] { "#FFBFD4E6", "#FFF4FAFF", "#FFBFD4E6" },
            new[] { "#FF4E6C86", "#FF8AA6BC", "#FF4E6C86" }),
        new("edge", "각인", NameFxKind.Ranker, false,
            new[] { "#FFCBD5E1", "#FFEFF6FF" },
            new[] { "#FF44586E", "#FF7B90A6" }),
    };

    private static readonly Dictionary<string, Effect> ById =
        All.ToDictionary(e => e.Id, StringComparer.Ordinal);

    public static bool IsKnown(string? id) => id is not null && ById.ContainsKey(id);

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
            double offset = hex.Length == 1 ? 0 : i / (double)(hex.Length - 1);
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

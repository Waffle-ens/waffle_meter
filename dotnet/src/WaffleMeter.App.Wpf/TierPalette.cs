using System.Windows;
using System.Windows.Media;

namespace WaffleMeter.App.Wpf;

/// <summary>
/// One tier's finished visual resources for one skin family. Every brush is frozen and every instance is a
/// process-wide singleton (8 tiers × dark/light), so a row view-model carries a single reference and the
/// per-tick row rebuild allocates nothing.
/// <para>The <see cref="None"/> instance is what a row gets when the feature is off, the tier is unknown, or the
/// sample is insufficient. Its brushes are never read — the row template's <c>IsNone</c> trigger restores the
/// skin's own <c>DynamicResource</c> values instead, which is the only way to stay pixel-identical to today's
/// look across ALL FOUR palettes (dark/midnight/slate/light differ in StatBg/SoftBorder/IconRing).</para>
/// </summary>
public sealed class TierBadge
{
    /// <summary>No tier assigned. The template falls back to the skin brushes; nothing here is used.</summary>
    public static readonly TierBadge None = new() { IsNone = true };

    public bool IsNone { get; init; }

    /// <summary>1 = 챌린저 … 8 = 아이언. 0 for <see cref="None"/>.</summary>
    public int Rank { get; init; }

    public string Name { get; init; } = string.Empty;

    public Brush RankRing { get; init; } = Brushes.Transparent;

    public Brush RankBg { get; init; } = Brushes.Transparent;

    public Brush RankFg { get; init; } = Brushes.Transparent;

    public Brush IconRing { get; init; } = Brushes.Transparent;

    /// <summary>Second ring drawn INSIDE the job-icon badge for the top three tiers. It is a zero-size stretch
    /// child of a Grid, so it contributes nothing to DesiredSize — the badge keeps its exact dimensions and the
    /// window's SizeToContent height never recalculates.</summary>
    public Brush InnerRing { get; init; } = Brushes.Transparent;

    public Visibility InnerRingVisibility { get; init; } = Visibility.Collapsed;

    public Brush ChipBg { get; init; } = Brushes.Transparent;

    public Brush ChipFg { get; init; } = Brushes.Transparent;

    /// <summary>True for 챌린저/마스터 only — the two tiers whose inner ring is the shared animated brush.</summary>
    public bool Animated { get; init; }
}

/// <summary>
/// The eight tier looks, as code constants. Mirrors <see cref="JoinPanelPalette"/>: frozen brushes built once.
/// <para><b>No PNG frames.</b> The meter root carries a 75~130% LayoutTransform and the process runs
/// <c>RenderMode.SoftwareOnly</c> by default, so bitmaps blur at non-integer scales; and a missing/renamed asset
/// would fail silently (JoinIcons.TryLoad swallows it). Vector strokes cost ~400 rasterised pixels per badge.</para>
/// <para><b>No Skin.*.xaml keys either.</b> SkinManager's contract is "all four palettes carry the same key set";
/// 8 tiers × 8 brushes × 4 files is 256 entries and one omission makes DynamicResource fall back silently.</para>
/// </summary>
public static class TierPalette
{
    private static readonly TierBadge[] Dark = Build(light: false);
    private static readonly TierBadge[] Light = Build(light: true);

    /// <summary>Badge for a tier rank (1..8). Out-of-range → <see cref="TierBadge.None"/>.</summary>
    public static TierBadge For(int rank, bool isLight) =>
        rank >= 1 && rank <= 8 ? (isLight ? Light : Dark)[rank - 1] : TierBadge.None;

    private static TierBadge[] Build(bool light)
    {
        // (rank, name, dark stops, light stops). Two/three stops = gradient ring, one = solid.
        (int Rank, string Name, string[] DarkStops, string[] LightStops, bool Inner, bool Animated)[] defs =
        [
            (1, "챌린저",   ["#FF7DD3FC", "#FFC4B5FD", "#FFF0ABFC"], ["#FF0EA5E9", "#FF7C3AED", "#FFC026D3"], true,  true),
            (2, "마스터",   ["#FFE879F9", "#FFA855F7"],              ["#FFA21CAF", "#FF6D28D9"],              true,  true),
            (3, "다이아",   ["#FF38BDF8", "#FF22D3EE"],              ["#FF0284C7", "#FF0891B2"],              true,  false),
            (4, "플래티넘", ["#FF5EEAD4"],                            ["#FF0D9488"],                           false, false),
            (5, "골드",     ["#FFFBBF24"],                            ["#FFB45309"],                           false, false),
            (6, "실버",     ["#FFCBD5E1"],                            ["#FF64748B"],                           false, false),
            (7, "브론즈",   ["#FFD08B5B"],                            ["#FF92400E"],                           false, false),
            // 아이언 keeps the current unaccented look: a faint ring, no inner ring, muted number.
            (8, "아이언",   ["#6694A3B8"],                            ["#8894A3B8"],                           false, false),
        ];

        var badges = new TierBadge[defs.Length];
        for (int i = 0; i < defs.Length; i++)
        {
            (int rank, string name, string[] darkStops, string[] lightStops, bool inner, bool animated) = defs[i];
            string[] stops = light ? lightStops : darkStops;
            Brush ring = stops.Length == 1 ? Frozen(stops[0]) : Gradient(stops);
            // Tint at ~15% for fills and ~90% for text so a 20px badge reads at a glance without a second colour.
            Brush fill = Frozen(WithAlpha(stops[0], 0x26));
            Brush text = Frozen(WithAlpha(stops[^1], light ? (byte)0xFF : (byte)0xF2));

            badges[i] = new TierBadge
            {
                Rank = rank,
                Name = name,
                RankRing = ring,
                RankBg = fill,
                RankFg = text,
                IconRing = ring,
                InnerRing = inner
                    ? (animated ? TierSheen.BrushFor(rank) : Frozen(WithAlpha(stops[^1], 0x8C)))
                    : Brushes.Transparent,
                InnerRingVisibility = inner ? Visibility.Visible : Visibility.Collapsed,
                ChipBg = fill,
                ChipFg = text,
                Animated = animated,
            };
        }

        return badges;
    }

    private static SolidColorBrush Frozen(string hex)
    {
        var brush = new SolidColorBrush(Parse(hex));
        brush.Freeze();
        return brush;
    }

    private static LinearGradientBrush Gradient(string[] hexStops)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        for (int i = 0; i < hexStops.Length; i++)
        {
            brush.GradientStops.Add(new GradientStop(Parse(hexStops[i]), i / (double)(hexStops.Length - 1)));
        }

        brush.Freeze();
        return brush;
    }

    private static string WithAlpha(string hex, byte alpha)
    {
        Color c = Parse(hex);
        return $"#{alpha:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
    }

    private static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex)!;
}

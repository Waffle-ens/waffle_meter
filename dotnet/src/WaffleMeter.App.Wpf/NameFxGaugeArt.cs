using System.Windows;
using System.Windows.Media;

namespace WaffleMeter.App.Wpf;

/// <summary>
/// Shape-based gauge skins: the ones whose identity is a THING (flames, frost) rather than a colour ramp.
///
/// <para><b>Why a DrawingBrush and not another gradient.</b> A translated gradient can only ever be a band of
/// colour sliding past. Five of them differ by hue and by how wide the band is, which is exactly the "same
/// effect in five colours" this replaces. A <see cref="DrawingBrush"/> tiles real geometry, so 잔불 can be
/// tongues of fire and 서리 can be crystal shards — the shapes the names promise.</para>
///
/// <para><b>Cost.</b> The tile is built once per (skin, theme, brightness) and only the transform animates, so
/// nothing re-tessellates per frame; <c>CachingHint.Cache</c> then lets WPF re-blit the rasterised tile instead
/// of re-drawing the paths. That matters because this runs at 30 fps on an <c>AllowsTransparency</c> layered
/// window over a game — the same reason <see cref="NameFxPalette"/> rules out blur and drop shadows. The
/// preview harness measures the frame cost; if a shape ever gets expensive it shows up there.</para>
///
/// <para><b>Seamlessness is a hard requirement, not a nicety.</b> The tile repeats every <c>TileFraction</c> of
/// the bar and the animation translates by exactly that, so the pattern lands on itself. Any shape that
/// crosses a tile edge has to be drawn TWICE — once at each side — or the seam shows as a stutter every
/// cycle. <see cref="Wrapped"/> exists for precisely that and every builder here goes through it.</para>
/// </summary>
internal static class NameFxGaugeArt
{
    /// <summary>Tile width as a fraction of the bar. One bar therefore shows ~1/0.28 ≈ 3.5 tiles, which is
    /// enough repetition to read as a texture without the shapes turning into noise on a short row.</summary>
    internal const double TileFraction = 0.28;

    /// <summary>Whether this skin is drawn as geometry rather than as a gradient. Every gauge skin is —
    /// the ramp path survives only for the NICKNAME effects, which are painted on glyphs where shapes would
    /// shred the letterforms.</summary>
    internal static bool IsArt(NameFxPalette.GaugeMotion motion) =>
        motion != NameFxPalette.GaugeMotion.None;

    /// <summary>Build the tiling brush for one art skin. <paramref name="hex"/> is the skin's own palette, so
    /// the colours stay the property of <see cref="NameFxPalette"/> and only the SHAPE lives here.</summary>
    internal static DrawingBrush Build(NameFxPalette.GaugeMotion motion, string[] hex, double brightness)
    {
        Color[] c = new Color[hex.Length];
        for (int i = 0; i < hex.Length; i++)
        {
            c[i] = NameFxPalette.Scale((Color)ColorConverter.ConvertFromString(hex[i])!, brightness);
        }

        var group = new DrawingGroup();
        switch (motion)
        {
            case NameFxPalette.GaugeMotion.Flame:
                BuildFlame(group, c);
                break;
            case NameFxPalette.GaugeMotion.Prism:
                BuildPrism(group, c);
                break;
            case NameFxPalette.GaugeMotion.Glaze:
                BuildGlaze(group, c);
                break;
            case NameFxPalette.GaugeMotion.Foam:
                BuildFoam(group, c);
                break;
            default:
                BuildFrost(group, c);
                break;
        }

        group.Freeze();

        var brush = new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            ViewportUnits = BrushMappingMode.RelativeToBoundingBox,
            Viewport = new Rect(0, 0, TileFraction, 1),
            // The drawing is authored in a 0..1 x 0..1 box; the viewport maps it onto the tile.
            ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,
            Viewbox = new Rect(0, 0, 1, 1),
            Stretch = Stretch.Fill,
        };

        // Rasterise the tile once and re-blit it while the transform animates.
        RenderOptions.SetCachingHint(brush, CachingHint.Cache);
        RenderOptions.SetCacheInvalidationThresholdMinimum(brush, 0.5);
        RenderOptions.SetCacheInvalidationThresholdMaximum(brush, 2.0);
        return brush;
    }

    /// <summary>
    /// 잔불 — a bed of embers with tongues licking up out of it.
    /// <para>Read from the bottom: a dark base, a hot band along the bottom edge where the coals sit, then
    /// tongues of decreasing width. The tongues are deliberately of THREE different heights and not evenly
    /// spaced; equal flames read as a decorative border, uneven ones read as fire.</para>
    /// </summary>
    private static void BuildFlame(DrawingGroup group, Color[] c)
    {
        Color deep = c[0];                       // 어두운 재
        Color mid = c[Math.Min(1, c.Length - 1)]; // 주황 불꽃
        Color hot = c[c.Length / 2];             // 가장 밝은 심지

        Add(group, new RectangleGeometry(new Rect(0, 0, 1, 1)), deep);

        // 아래쪽 잉걸: 뜨거운 색이 바닥에 깔린다. 위로 갈수록 투명해져 불꽃과 이어진다.
        var bed = new LinearGradientBrush
        {
            StartPoint = new Point(0, 1),
            EndPoint = new Point(0, 0.35),
            GradientStops =
            {
                new GradientStop(mid, 0.0),
                new GradientStop(Transparent(mid), 1.0),
            },
        };
        bed.Freeze();
        group.Children.Add(new GeometryDrawing(bed, null, new RectangleGeometry(new Rect(0, 0, 1, 1))));

        // 혀 셋. (중심 x, 높이, 폭) — 높이·폭·간격을 모두 다르게 둔다.
        (double X, double H, double W)[] tongues =
        {
            (0.18, 0.92, 0.20),
            (0.52, 0.62, 0.15),
            (0.80, 0.78, 0.17),
        };

        foreach ((double x, double h, double w) in tongues)
        {
            foreach (double cx in Wrapped(x, w))
            {
                Add(group, Tongue(cx, h, w), mid);
                Add(group, Tongue(cx, h * 0.62, w * 0.5), hot);
            }
        }
    }

    /// <summary>
    /// 서리 — cracked ice plates, the way a frozen surface splits into facets.
    /// <para>Two earlier attempts are worth recording because both failed for the same reason: the bar is only
    /// about 26 px tall, so anything fine disappears or turns into a pattern. Large hexagonal crystals read as
    /// GEMS, and a row of thin needles read as a COMB (a regular sawtooth, like a heart-rate trace). What is
    /// left is the shape that survives at that height — a few big angular plates at different leans, with thin
    /// bright slivers where the light catches an edge.</para>
    /// </summary>
    private static void BuildFrost(DrawingGroup group, Color[] c)
    {
        Color deep = c[0];                        // 짙은 남색
        Color ice = c[Math.Min(1, c.Length - 1)]; // 얼음
        Color rime = c[c.Length / 2];             // 서리 흰빛

        Add(group, new RectangleGeometry(new Rect(0, 0, 1, 1)), deep);

        // 판 넷. 서로 다른 방향으로 기울여 '갈라진 얼음'을 만든다 — 같은 방향으로 늘어놓으면 다시
        // 규칙적인 줄무늬가 된다. (좌x, 우x, 왼쪽기울기, 오른쪽기울기, 위, 아래)
        (double L, double R, double LeanL, double LeanR, double Top, double Bottom)[] plates =
        {
            (-0.06, 0.30, 0.22, -0.14, 0.00, 1.00),
            (0.26, 0.58, -0.18, 0.20, 0.00, 0.66),
            (0.30, 0.66, 0.16, -0.10, 0.58, 1.00),
            (0.62, 1.06, -0.20, 0.12, 0.00, 1.00),
        };

        foreach ((double l, double r, double leanL, double leanR, double top, double bottom) in plates)
        {
            foreach (double dx in WrappedSpan(l, r))
            {
                Add(group, Plate(l + dx, r + dx, leanL, leanR, top, bottom), ice);
            }
        }

        // 빛이 걸린 모서리. 아주 가늘고 밝게 — 이게 없으면 판이 그냥 파란 다각형으로 보인다.
        (double X, double Top, double Bottom, double Lean)[] glints =
        {
            (0.29, 0.06, 0.94, -0.16),
            (0.64, 0.10, 0.72, 0.18),
        };

        foreach ((double x, double top, double bottom, double lean) in glints)
        {
            foreach (double dx in WrappedSpan(x - 0.02, x + 0.02))
            {
                Add(group, Plate(x - 0.016 + dx, x + 0.016 + dx, lean, lean, top, bottom), rime);
            }
        }
    }

    /// <summary>A four-sided plate whose left and right edges lean independently.</summary>
    private static Geometry Plate(double l, double r, double leanL, double leanR, double top, double bottom)
    {
        var figure = new PathFigure { StartPoint = new Point(l, top), IsClosed = true, IsFilled = true };
        figure.Segments.Add(new LineSegment(new Point(r, top), isStroked: false));
        figure.Segments.Add(new LineSegment(new Point(r + leanR, bottom), isStroked: false));
        figure.Segments.Add(new LineSegment(new Point(l + leanL, bottom), isStroked: false));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();
        return geometry;
    }

    /// <summary>As <see cref="Wrapped"/>, but for a shape given by its span rather than its centre: yields the
    /// x OFFSETS it has to be drawn at so nothing is clipped at a tile edge.</summary>
    private static IEnumerable<double> WrappedSpan(double left, double right)
    {
        yield return 0;
        if (left < 0)
        {
            yield return 1;
        }
        else if (right > 1)
        {
            yield return -1;
        }
    }

    /// <summary>
    /// 프리즘 — a beam split into its spectrum.
    /// <para>The first attempt put thin coloured slivers on a plain cyan ground and came out looking exactly
    /// like the frost plates beside it: same hue, same angular shapes, and the slivers too fine to register.
    /// A prism is only legible when the SPECTRUM is the subject, so the tile is now a full sweep through
    /// cyan → white → violet, and the plain ground is gone.</para>
    /// </summary>
    private static void BuildPrism(DrawingGroup group, Color[] c)
    {
        Color deep = c[0];                        // 짙은 청록
        Color beam = c[Math.Min(1, c.Length - 1)]; // 시안
        Color white = c[c.Length / 2];            // 흰 심
        Color violet = c[Math.Min(3, c.Length - 1)];

        Add(group, new RectangleGeometry(new Rect(0, 0, 1, 1)), deep);

        // 분광: 한 방향으로 기운 넓은 띠들이 색을 갈아입으며 지나간다. 흰 심을 가운데 두어
        // '갈라지는 빛'의 축을 만든다.
        const double Lean = -0.42;
        (double L, double R, Color Tint)[] bands =
        {
            (0.02, 0.26, beam),
            (0.26, 0.40, white),
            (0.40, 0.62, violet),
            (0.62, 0.80, beam),
        };

        foreach ((double l, double r, Color tint) in bands)
        {
            foreach (double dx in WrappedSpan(l + Math.Min(0, Lean), r + Math.Max(0, Lean)))
            {
                Add(group, Plate(l + dx, r + dx, Lean, Lean, 0.0, 1.0), tint);
            }
        }

        // 굴절면의 날. 아주 밝고 가늘게 — 유리 모서리에 빛이 걸린 자리.
        foreach (double dx in WrappedSpan(0.24 + Lean, 0.28))
        {
            Add(group, Plate(0.245 + dx, 0.268 + dx, Lean, Lean, 0.0, 1.0), white);
        }
    }

    /// <summary>
    /// 베리 글레이즈 — a poured glaze with a scalloped edge and a couple of runs, the way icing sits on a donut.
    /// <para>Two earlier attempts failed the same way at this size: a thin band with narrow vertical runs reads
    /// as TALLY MARKS on a 26 px bar, not as something poured. What carries at that height is the big rounded
    /// EDGE of the pour — overlapping bulges along the bottom of the layer — with only one or two long runs for
    /// movement.</para>
    /// </summary>
    private static void BuildGlaze(DrawingGroup group, Color[] c)
    {
        Color deep = c[0];                         // 짙은 자주
        Color glaze = c[Math.Min(1, c.Length - 1)]; // 분홍
        Color gloss = c[c.Length / 2];             // 하이라이트

        Add(group, new RectangleGeometry(new Rect(0, 0, 1, 1)), deep);

        // 부어진 층 + 가리비 모양 아랫단. 원을 겹쳐 놓아 흘러내린 가장자리를 만든다.
        Add(group, new RectangleGeometry(new Rect(0, 0, 1, 0.44)), glaze);

        (double X, double R)[] scallops = { (0.05, 0.15), (0.28, 0.20), (0.52, 0.14), (0.74, 0.22), (0.96, 0.16) };
        foreach ((double x, double r) in scallops)
        {
            foreach (double cx in Wrapped(x, r * 2))
            {
                Add(group, new EllipseGeometry(new Point(cx, 0.44), r, r * 1.5), glaze);
            }
        }

        // 길게 흘러내린 줄기 하나 + 곧 떨어질 방울. 하나면 충분하다 — 여럿이면 다시 빗살이 된다.
        foreach (double cx in Wrapped(0.30, 0.10))
        {
            Add(group, Drip(cx, 0.10, 0.94), glaze);
        }

        foreach (double cx in Wrapped(0.74, 0.09))
        {
            Add(group, new EllipseGeometry(new Point(cx, 0.86), 0.038, 0.10), glaze);
        }

        // 광택: 층의 윗면을 따라 길게 한 줄. 표면이 젖어 보이게 하는 건 결국 이것 하나다.
        Add(group, new RectangleGeometry(new Rect(0, 0.06, 1, 0.07)), gloss);
    }

    /// <summary>
    /// 말차 크림 — foam sitting on the tea.
    /// <para>Overlapping circles with no straight edge anywhere, which is what keeps it from reading as a paler
    /// version of the frost plates. The first attempt drew the blobs in the palette's brightest stop, which for
    /// this skin is very nearly white — over a dark row at 0.58 that came out grey, like stones. The foam is
    /// now built from the TEA colour with only the crests in cream.</para>
    /// </summary>
    private static void BuildFoam(DrawingGroup group, Color[] c)
    {
        Color deep = c[0];                        // 짙은 녹
        Color tea = c[Math.Min(1, c.Length - 1)];  // 말차
        Color cream = c[c.Length / 2];            // 크림

        Add(group, new RectangleGeometry(new Rect(0, 0, 1, 1)), deep);
        Add(group, new RectangleGeometry(new Rect(0, 0.34, 1, 0.66)), tea);

        // 거품 덩어리. 큰 것 위주로 적게 — 작은 것을 많이 놓으면 물방울무늬가 된다.
        (double X, double Y, double Rx, double Ry)[] foam =
        {
            (0.10, 0.34, 0.19, 0.34),
            (0.40, 0.26, 0.15, 0.27),
            (0.68, 0.36, 0.20, 0.36),
            (0.93, 0.24, 0.13, 0.25),
        };

        foreach ((double x, double y, double rx, double ry) in foam)
        {
            foreach (double cx in Wrapped(x, rx * 2))
            {
                Add(group, new EllipseGeometry(new Point(cx, y), rx, ry), tea);
            }
        }

        // 크림 마루. 거품 위쪽에만 얹어 빛이 닿은 면을 만든다.
        foreach ((double x, double y, double rx, double ry) in foam)
        {
            foreach (double cx in Wrapped(x, rx * 2))
            {
                Add(group, new EllipseGeometry(new Point(cx, y - (ry * 0.34)), rx * 0.62, ry * 0.42), cream);
            }
        }
    }

    /// <summary>A run of glaze: straight sides from the top edge, rounded off at the bottom.</summary>
    private static Geometry Drip(double cx, double width, double length)
    {
        double half = width / 2;
        var figure = new PathFigure { StartPoint = new Point(cx - half, 0), IsClosed = true, IsFilled = true };
        figure.Segments.Add(new LineSegment(new Point(cx - half, length - half), isStroked: false));
        figure.Segments.Add(new ArcSegment(
            new Point(cx + half, length - half), new Size(half, half), 0, false, SweepDirection.Counterclockwise, false));
        figure.Segments.Add(new LineSegment(new Point(cx + half, 0), isStroked: false));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();
        return geometry;
    }

    /// <summary>A flame tongue: a teardrop rising from the bottom edge, widest low down and pinched at the tip.</summary>
    private static Geometry Tongue(double cx, double height, double width)
    {
        double half = width / 2;
        double baseY = 1.02;          // 바닥 밖에서 시작해 잉걸과 이어 붙인다
        double tipY = 1.0 - height;

        var figure = new PathFigure { StartPoint = new Point(cx - half, baseY), IsClosed = true, IsFilled = true };
        figure.Segments.Add(new BezierSegment(
            new Point(cx - half * 0.95, tipY + (height * 0.55)),
            new Point(cx - half * 0.30, tipY + (height * 0.18)),
            new Point(cx, tipY),
            isStroked: false));
        figure.Segments.Add(new BezierSegment(
            new Point(cx + half * 0.30, tipY + (height * 0.18)),
            new Point(cx + half * 0.95, tipY + (height * 0.55)),
            new Point(cx + half, baseY),
            isStroked: false));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();
        return geometry;
    }

    /// <summary>
    /// The x positions a shape must be drawn at so the tile stays seamless: its own, plus a copy on the far
    /// side whenever it overhangs an edge.
    /// <para>Without this every shape that touches an edge is clipped on one side and missing on the other, and
    /// the join walks past once per cycle as a visible stutter — which is the failure the preview harness
    /// measures as a seam.</para>
    /// </summary>
    private static IEnumerable<double> Wrapped(double cx, double width)
    {
        yield return cx;
        double half = width / 2;
        if (cx - half < 0)
        {
            yield return cx + 1;
        }
        else if (cx + half > 1)
        {
            yield return cx - 1;
        }
    }

    private static void Add(DrawingGroup group, Geometry geometry, Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        group.Children.Add(new GeometryDrawing(brush, null, geometry));
    }

    private static Color Transparent(Color c) => Color.FromArgb(0, c.R, c.G, c.B);
}

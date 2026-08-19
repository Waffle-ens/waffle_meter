using System.Windows;
using System.Windows.Media;

namespace WaffleMeter.App.Wpf;

/// <summary>
/// What each gauge skin's decoration actually draws, ported from the motion study.
///
/// <para><b>Every particle is a pure function of (time, index, row seed).</b> There is no simulation state to
/// keep, which is what lets a row be rebuilt several times a second — as
/// <c>OverlayViewModel.Update</c> does — without any particle restarting. <see cref="Hash"/> stands in for the
/// random number generator a stateful system would need.</para>
///
/// <para><b>Allocation discipline.</b> Pens and brushes are static and frozen; alpha is applied with
/// <c>PushOpacity</c> rather than by making a brush per particle. Nothing here allocates a geometry, a
/// collection or a transform per frame, because at 24 fps × up to ten rows that is thousands of objects a
/// second for marks a few pixels across.</para>
///
/// <para><b>Why lines and ellipses rather than polygons.</b> At 2~6 DIP a round-capped thick line and a rhombus
/// are the same handful of pixels, and the line costs no geometry. The one shape that genuinely needs corners —
/// the frost crystal — is drawn as its arms, which are lines too.</para>
/// </summary>
internal static class GaugeFxArt
{
    internal static bool Knows(string? id) => Find(id) is not null;

    internal static Art? Find(string? id) => id switch
    {
        "berryglaze" => Berry,
        "matcha" => Matcha,
        "prism" => Prism,
        "ember" => Ember,
        "frost" => Frost,
        _ => null,
    };

    /// <summary>One skin's renderer. <paramref name="area"/> is the part of the bar it may paint — already
    /// excludes the strip reserved for the rail, rank chip and job icon.</summary>
    internal delegate void Draw(DrawingContext dc, Rect area, double t, int seed);

    internal sealed record Art(Draw Draw);

    // ── 색 ──────────────────────────────────────────────────────────────────────────────────────────
    // 장식은 채움 위에 얹히므로 색은 '더해지는 빛'이다. 스킨 팔레트를 다시 칠하는 게 아니라 그 위에서
    // 반짝이는 것이라, 채도보다 명도가 중요하다.
    private static readonly Brush BerryGloss = Frozen("#FFF0F7");
    private static readonly Brush BerryRim = Frozen("#FF9BCB");
    private static readonly Brush BerryBody = Frozen("#F0529A");
    private static readonly Pen BerryRibbon = FrozenPen("#FFDFF1", 2.2);
    private static readonly Pen BerryRibbonLow = FrozenPen("#FF74BB", 3.2);
    private static readonly Pen BerryDropEdge = FrozenPen("#FFD2EB", 0.7);

    private static readonly Brush MatchaFoam = Frozen("#EAF4D1");
    private static readonly Brush MatchaCream = Frozen("#FFF3D2");
    private static readonly Pen MatchaRing = FrozenPen("#F6FAE4", 0.55);

    private static readonly Brush PrismCyan = Frozen("#A0F7FF");
    private static readonly Brush PrismViolet = Frozen("#E5BAFF");
    private static readonly Pen PrismEdgeCyan = FrozenPen("#75EEFF", 1.6);
    private static readonly Pen PrismEdgeViolet = FrozenPen("#D989FF", 1.6);
    private static readonly Pen PrismSweep = FrozenPen("#EAFBFF", 2.0);

    private static readonly Brush EmberCore = Frozen("#FFEFA4");
    private static readonly Brush EmberGlow = Frozen("#FF5E12");
    private static readonly Pen EmberTrail = FrozenPen("#FF741E", 0.8);

    private static readonly Pen FrostArm = FrozenPen("#DAFAFF", 0.85);
    private static readonly Pen FrostArmThin = FrozenPen("#DAFAFF", 0.65);
    private static readonly Pen FrostSpark = FrozenPen("#EBFEFF", 0.65);

    private static readonly Art Berry = new(DrawBerry);
    private static readonly Art Matcha = new(DrawMatcha);
    private static readonly Art Prism = new(DrawPrism);
    private static readonly Art Ember = new(DrawEmber);
    private static readonly Art Frost = new(DrawFrost);

    /// <summary>
    /// 베리 글레이즈 — a wet lacquer: two slow gloss ribbons across the upper half, and droplets that swell and
    /// run down. Nothing sparkles; if it reads as sparks it has become 잔불.
    /// </summary>
    private static void DrawBerry(DrawingContext dc, Rect a, double t, int seed)
    {
        // 광택 리본. 베지어 대신 짧은 선분으로 그린다 — 이 폭에서는 구분이 안 되고, 선분은 할당이 없다.
        for (int layer = 0; layer < 2; layer++)
        {
            double y = a.Y + (a.Height * (0.34 + (layer * 0.23)));
            double offset = ((t * 14.0) + (layer * 31)) % 56.0;
            Pen pen = layer == 0 ? BerryRibbon : BerryRibbonLow;
            // 26 DIP 간격. 이 폭에서 곡선은 이미 부드럽고, 간격을 좁히면 프레임당 선 개수만 배로 는다 —
            // 처음 14 로 잡았을 때 이 리본 하나가 오버레이 프레임 비용의 절반 가까이를 먹었다.
            const double Step = 26;
            dc.PushOpacity(layer == 0 ? 0.19 : 0.18);
            for (double x = a.X - Step + offset - Step; x < a.Right + Step; x += Step)
            {
                double x0 = Math.Max(a.X, x);
                double x1 = Math.Min(a.Right, x + Step);
                if (x1 <= x0)
                {
                    continue;
                }

                double y0 = y + (Math.Sin((x0 - a.X) * 0.11) * (1.6 + layer));
                double y1 = y + (Math.Sin((x1 - a.X) * 0.11) * (1.6 + layer));
                dc.DrawLine(pen, new Point(x0, y0), new Point(x1, y1));
            }

            dc.Pop();
        }

        // 방울: 부풀었다가 아래로 흘러 사라진다.
        for (int i = 0; i < 6; i++)
        {
            double life = 3.4 + (Hash(i + seed + 8) * 2.4);
            double phase = Phase(t, i + seed + 17, life);
            double x = a.X + 6 + (Hash(i + seed + 4) * Math.Max(1, a.Width - 12));
            double run = phase * (8 + (Hash(i + seed + 21) * 8));
            double y = a.Y + 4 + (Hash(i + seed + 11) * Math.Max(1, a.Height - 12)) + run;
            double rx = 1.6 + (Hash(i + seed + 13) * 1.1);
            double ry = rx * (1.4 + (Math.Sin(Math.PI * phase) * 0.9));
            double alpha = Math.Sin(Math.PI * phase) * (0.22 + (Hash(i + seed + 22) * 0.30));
            if (alpha <= 0.01 || y - ry > a.Bottom)
            {
                continue;
            }

            dc.PushOpacity(alpha);
            dc.DrawEllipse(BerryBody, BerryDropEdge, new Point(x, y), rx, ry);
            dc.DrawEllipse(BerryGloss, null, new Point(x - (rx * 0.34), y - (ry * 0.42)), rx * 0.30, ry * 0.22);
            dc.Pop();
        }

        // 가장자리에 맺힌 큰 방울 하나 — 리본만 있으면 '줄무늬'로 읽힌다.
        double bigPhase = Phase(t, seed + 51, 5.8);
        double bigAlpha = Math.Sin(Math.PI * bigPhase) * 0.30;
        if (bigAlpha > 0.01)
        {
            double bx = a.X + (a.Width * (0.18 + (Hash(seed + 61) * 0.6)));
            dc.PushOpacity(bigAlpha);
            dc.DrawEllipse(BerryRim, null, new Point(bx, a.Y + (a.Height * 0.72) + (bigPhase * 4)), 2.6, 3.4);
            dc.Pop();
        }
    }

    /// <summary>
    /// 말차 크림 — cream folding across matte tea, right to left, plus a few foam rings rising. Rounded
    /// everywhere: it has to be tellable from 서리 with the colour removed.
    /// </summary>
    private static void DrawMatcha(DrawingContext dc, Rect a, double t, int seed)
    {
        // 크림 마블 두 층. 서로 다른 속도로 흘러 접히는 인상을 만든다.
        for (int i = 0; i < 7; i++)
        {
            double life = 9.6 + (Hash(i + seed + 9) * 3.6);
            double phase = Phase(t, i + seed + 19, life);
            double x = a.Right + 8 - (phase * (a.Width + 16));
            double y = a.Y + 5 + (Hash(i + seed + 3) * Math.Max(1, a.Height - 10))
                + (Math.Sin((phase * 7) + i) * 1.8);
            double rx = 6.0 + (Hash(i + seed + 12) * 10.0);
            double ry = 3.2 + (Hash(i + seed + 20) * 2.6);
            double alpha = Math.Sin(Math.PI * phase) * (0.14 + (Hash(i + seed + 28) * 0.24));
            if (alpha <= 0.01)
            {
                continue;
            }

            dc.PushOpacity(alpha);
            dc.DrawEllipse(i % 3 == 0 ? MatchaCream : MatchaFoam, null, new Point(x, y), rx, ry);
            dc.Pop();
        }

        // 폼 링: 외곽선만. 떠오르면서 옆으로 흐른다.
        for (int i = 0; i < 4; i++)
        {
            double life = 4.8 + (Hash(i + seed + 41) * 2.8);
            double phase = Phase(t, i + seed + 52, life);
            double x = a.X + 8 + (Hash(i + seed + 33) * Math.Max(1, a.Width - 16)) + (phase * 6);
            double y = a.Bottom - 3 - (phase * (a.Height * 0.55));
            double r = 1.5 + (Hash(i + seed + 37) * 1.5);
            double alpha = Math.Sin(Math.PI * phase) * 0.34;
            if (alpha <= 0.01)
            {
                continue;
            }

            dc.PushOpacity(alpha);
            dc.DrawEllipse(null, MatchaRing, new Point(x, y), r, r);
            dc.Pop();
        }
    }

    /// <summary>
    /// 프리즘 — ordered refraction: one diagonal band crossing, and slow-turning glass slivers. Faster and more
    /// structured than 서리, and the bar as a whole never pulses.
    /// </summary>
    private static void DrawPrism(DrawingContext dc, Rect a, double t, int seed)
    {
        // 굴절띠 하나가 대각으로 지난다.
        double sweep = Phase(t, seed + 2, 3.8);
        double sx = a.X - 20 + (sweep * (a.Width + 40));
        dc.PushOpacity(0.30 * Math.Sin(Math.PI * sweep));
        dc.DrawLine(PrismSweep, new Point(sx, a.Y - 2), new Point(sx + 9, a.Bottom + 2));
        dc.Pop();

        // 유리 파편. 짧고 굵은 선 = 이 크기에서는 마름모와 같다.
        for (int i = 0; i < 6; i++)
        {
            double life = 5.4 + (Hash(i + seed + 20) * 2.6);
            double phase = Phase(t, i + seed + 5, life);
            double x = a.X - 12 + (phase * (a.Width + 24));
            double y = a.Y + 4 + (Hash(i + seed + 1) * Math.Max(1, a.Height - 8));
            double size = 1.8 + (Hash(i + seed + 3) * 2.6);
            double spin = (t * (0.35 + (Hash(i + seed + 7) * 0.4))) + (Hash(i + seed + 9) * 6.28);
            double alpha = 0.18 + (Math.Sin(phase * Math.PI) * 0.30);
            if (alpha <= 0.01)
            {
                continue;
            }

            double dx = Math.Cos(spin) * size;
            double dy = Math.Sin(spin) * size;
            dc.PushOpacity(alpha);
            dc.DrawLine(i % 2 == 0 ? PrismEdgeCyan : PrismEdgeViolet,
                new Point(x - dx, y - dy), new Point(x + dx, y + dy));
            dc.DrawEllipse(i % 2 == 0 ? PrismCyan : PrismViolet, null, new Point(x, y), 0.7, 0.7);
            dc.Pop();
        }
    }

    /// <summary>
    /// 잔불 — sparks rising off a bed of coals: bright cores with short trails, swaying as they climb and
    /// shrinking out. The bar is never filled with a picture of fire and its opacity never pulses as a whole.
    /// </summary>
    private static void DrawEmber(DrawingContext dc, Rect a, double t, int seed)
    {
        for (int i = 0; i < 10; i++)
        {
            double life = 1.4 + (Hash(i + seed + 4) * 1.4);
            double phase = Phase(t, i + seed + 15, life);
            double startX = a.X + 6 + (Hash(i + seed + 2) * Math.Max(4, a.Width - 12));
            double sway = Math.Sin((phase * Math.PI * (1.4 + Hash(i + seed + 5))) + (Hash(i + seed + 8) * 6))
                * (2 + (Hash(i + seed + 11) * 4));
            double x = startX + sway + ((Hash(i + seed + 17) - 0.5) * 14 * phase);
            double y = a.Bottom + 2 - (phase * ((a.Height * 0.92) + 6 + (Hash(i + seed + 23) * 8)));
            double envelope = Math.Sin(Math.PI * phase);
            double alpha = Math.Max(0, envelope) * (0.34 + (Hash(i + seed + 31) * 0.5));
            if (alpha <= 0.02)
            {
                continue;
            }

            double radius = 0.55 + (Hash(i + seed + 12) * 0.95);
            double trail = 2 + (Hash(i + seed + 22) * 5);

            dc.PushOpacity(alpha);
            dc.DrawLine(EmberTrail, new Point(x, y + trail), new Point(x - (sway * 0.14), y + radius));

            // 두 겹으로 흉내낸 발광 — 넓고 흐린 것 + 작고 밝은 심. BlurEffect 는 쓸 수 없다.
            dc.PushOpacity(0.34);
            dc.DrawEllipse(EmberGlow, null, new Point(x, y), radius * 3.0, radius * 3.0);
            dc.Pop();
            dc.DrawEllipse(EmberCore, null, new Point(x, y), Math.Max(0.45, radius * 0.62), Math.Max(0.45, radius * 0.62));
            dc.Pop();
        }
    }

    /// <summary>
    /// 서리 — crystals and shards drifting down at an angle, bigger and slower than 잔불. At most one or two
    /// full six-armed crystals at a time; the rest are plain shards, because a field of detailed snowflakes at
    /// this size turns into a repeating pattern.
    /// </summary>
    private static void DrawFrost(DrawingContext dc, Rect a, double t, int seed)
    {
        for (int i = 0; i < 7; i++)
        {
            double life = 3.6 + (Hash(i + seed + 12) * 3.2);
            double phase = Phase(t, i + seed + 29, life);
            double x = a.X + (Hash(i + seed + 4) * (a.Width + 20)) - 10
                + (phase * (8 + (Hash(i + seed + 18) * 16)));
            double y = a.Y - 4 + (phase * (a.Height + 8));
            double size = 1.7 + (Hash(i + seed + 7) * 2.6);
            double alpha = Math.Sin(Math.PI * phase) * (0.30 + (Hash(i + seed + 14) * 0.45));
            if (alpha <= 0.02 || x < a.X - 6 || x > a.Right + 6)
            {
                continue;
            }

            double rotation = (Hash(i + seed + 9) * Math.PI)
                + (t * (Hash(i + seed + 20) > 0.5 ? 0.36 : -0.30));

            dc.PushOpacity(alpha);
            bool detailed = size > 3.0 && i % 4 == 0; // 큰 6갈래는 동시에 하나둘만
            int arms = detailed ? 3 : 2;
            Pen pen = detailed ? FrostArm : FrostArmThin;
            for (int arm = 0; arm < arms; arm++)
            {
                double angle = rotation + (arm * Math.PI / arms);
                double dx = Math.Cos(angle) * size;
                double dy = Math.Sin(angle) * size;
                dc.DrawLine(pen, new Point(x - dx, y - dy), new Point(x + dx, y + dy));
            }

            dc.Pop();
        }

        // 이따금 반짝이는 서릿발. Math.Pow 로 좁은 봉우리를 만들어 '깜빡'이 되게 한다.
        for (int i = 0; i < 3; i++)
        {
            double blink = Math.Pow(Math.Max(0, Math.Sin((t * 2.3) + (i * 2.1) + (seed * 0.7))), 8);
            if (blink < 0.03)
            {
                continue;
            }

            double x = a.X + 10 + (Hash(i + seed + 51) * Math.Max(4, a.Width - 20));
            double y = a.Y + 5 + (Hash(i + seed + 61) * Math.Max(4, a.Height - 10));
            dc.PushOpacity(blink * 0.72);
            dc.DrawLine(FrostSpark, new Point(x - 3.4, y), new Point(x + 3.4, y));
            dc.DrawLine(FrostSpark, new Point(x, y - 3.4), new Point(x, y + 3.4));
            dc.Pop();
        }
    }

    /// <summary>Where a particle is in its own life, from absolute time. The per-particle offset is what stops
    /// them all being born on the same frame.</summary>
    private static double Phase(double t, int index, double life)
    {
        double v = (t + (Hash(index) * life)) % life;
        return (v < 0 ? v + life : v) / life;
    }

    /// <summary>Deterministic pseudo-random in [0,1). Stands in for the RNG a stateful particle system would
    /// need — the whole point is that frame N can be drawn without having drawn frame N-1.</summary>
    private static double Hash(int value)
    {
        double x = Math.Sin((value * 127.1) + 311.7) * 43758.5453123;
        return x - Math.Floor(x);
    }

    private static Brush Frozen(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
        b.Freeze();
        return b;
    }

    private static Pen FrozenPen(string hex, double thickness)
    {
        var p = new Pen(Frozen(hex), thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        p.Freeze();
        return p;
    }
}

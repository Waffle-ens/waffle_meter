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
        "voltage" => Voltage,
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

    // 전류는 흰색에 아주 약간 푸른 기운 — 노랑/금색은 잔불(스파크)로 읽힌다.
    // 같은 경로를 굵기·명도가 벌어진 네 겹으로 긋는다: 발광이 채널 '뒤'가 아니라 채널 '안'에 있어야
    // 낙서가 아니라 방전으로 보인다. 예전 VoltHalo(둥근 노란 blob)가 얼룩의 주범이었다.
    private static readonly Pen VoltCorona = FrozenPen("#6E8CFF", 4.4);
    private static readonly Pen VoltGlow = FrozenPen("#8FA9FF", 2.6);
    private static readonly Pen VoltChannel = FrozenPen("#C8DAFF", 1.5);
    private static readonly Pen VoltCore = FrozenPen("#F2F7FF", 0.9);
    private static readonly Pen VoltForkChannel = FrozenPen("#9FBAFF", 1.1);
    private static readonly Pen VoltForkCore = FrozenPen("#E8F0FF", 0.6);
    private static readonly Brush VoltMote = Halo("#6E8CFF", "#DCE6FF");
    private static readonly Brush VoltMoteCore = Frozen("#F2F7FF");

    private static readonly Brush MatchaFoam = Frozen("#EAF4D1");
    private static readonly Brush MatchaCream = Frozen("#FFF3D2");
    private static readonly Pen MatchaRing = FrozenPen("#F6FAE4", 0.55);

    private static readonly Brush PrismCyan = Frozen("#A0F7FF");
    private static readonly Brush PrismViolet = Frozen("#E5BAFF");
    private static readonly Pen PrismEdgeCyan = FrozenPen("#75EEFF", 1.6);
    private static readonly Pen PrismEdgeViolet = FrozenPen("#D989FF", 1.6);
    private static readonly Pen PrismSweep = FrozenPen("#EAFBFF", 2.0);

    private static readonly Brush EmberCore = Frozen("#FFEFA4");
    private static readonly Brush EmberGlow = Halo("#FF5E12", "#FFEFA4");
    private static readonly Pen EmberTrail = FrozenPen("#FF741E", 0.8);

    private static readonly Pen FrostArm = FrozenPen("#DAFAFF", 0.85);
    private static readonly Pen FrostArmThin = FrozenPen("#DAFAFF", 0.65);
    private static readonly Pen FrostBranch = FrozenPen("#DAFAFF", 0.6);
    private static readonly Pen FrostSpark = FrozenPen("#EBFEFF", 0.65);
    private static readonly Brush FrostHalo = Halo("#53D0FF", "#DAFAFF");
    private static readonly Brush PrismHaloCyan = Halo("#75EEFF", "#EAFBFF");
    private static readonly Brush PrismHaloViolet = Halo("#D989FF", "#F6EBFF");

    private static readonly Art Berry = new(DrawBerry);
    private static readonly Art Voltage = new(DrawVoltage);
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
        for (int i = 0; i < 9; i++)
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
    /// 전류 — a forked bolt striking DOWN through the bar, lit from inside its own channel.
    ///
    /// <para><b>The bolt is vertical, and that is the whole trick.</b> The first two attempts ran a zigzag along
    /// the bar, and on a row that is ~260 DIP wide but only ~26 tall a six-segment horizontal zigzag has segments
    /// about 25 DIP long and 9 tall — 20° from horizontal, which the eye resolves as a waveform or a mountain
    /// ridge, not as lightning. Real lightning is near-vertical, so the channel here descends the FULL height and
    /// wanders only a few DIP sideways: the lateral reach is deliberately smaller than the vertical step, which
    /// is what puts every segment past 45° and makes the kinks read as kinks.</para>
    ///
    /// <para><b>The glow runs along the path, never behind it.</b> The original drew a round halo brush behind
    /// each squiggle, and a round glow behind a jagged line reads as a scribble with a lamp behind it — light and
    /// line are visibly different shapes, so the eye resolves a smudge. Here the same path is walked four times
    /// with pens that step hard in both width and brightness: a dim corona, a mid channel, then a thin near-white
    /// core. Node positions are a pure function of the strike key and the node index, so each pass RECOMPUTES
    /// them rather than storing a point list — that is what lets a four-pass stroke stay allocation-free.</para>
    ///
    /// <para><b>Forking is the cue that says lightning.</b> A bolt that never branches reads as a crack or a
    /// scratch however bright it is. One branch peels off a middle node to the side the channel is NOT about to
    /// take, runs two shortening segments and dies in mid-air. It is thinner and has no corona pass, so it can
    /// never out-shout its parent.</para>
    ///
    /// <para><b>Both colour directions are drawn, so neither has to be predicted.</b> This skin's fill is an
    /// indigo ramp with a pale <c>#E0E7FF</c> band in it, and that band MOVES — a repeating tile that scrolls,
    /// over a bar whose width tracks the row's DPS share — so compensating by position would be guessing. The
    /// corona is a mid-blue that separates against pale fill and the core is near-white and separates against
    /// dark fill; one of the two always carries the shape, wherever the band happens to be.</para>
    ///
    /// <para><b>Mostly off, and nothing at all in between.</b> A strike occupies the first fifth of a bolt's life
    /// and the method leaves before drawing anything outside that window, so the ordinary frame costs only the
    /// three static motes. Lightning is absent most of the time — that is what it IS — but a skin that renders
    /// NOTHING for two frames in three reads as broken rather than as restraint, which is what the motes are
    /// for. A high-power peak
    /// fires the core for about a tenth of a second and a short exponential tail lets the channel cool — without
    /// the tail it vanishes like a decal being switched off. The strike ordinal is folded into the hash key, so
    /// no two strikes trace the same path; a fixed shape that merely blinks is the other way this fails.</para>
    /// </summary>
    private static void DrawVoltage(DrawingContext dc, Rect a, double t, int seed)
    {
        // 정전기 모트 — 방전 사이를 잇는 상시 요소.
        //  ⚠ 점이어야 한다. 앞선 판에서는 짧은 대각 대시였는데, 확대하면 전류가 아니라 막대에 난 **긁힌
        //    자국**으로 읽힌다. 점은 방향이 없어서 그렇게 안 보인다.
        //  ⚠ 위치가 사인파여야 한다. 수명으로 순환시키면 경계에서 좌표가 순간이동하는데, 상시 요소는
        //    알파가 0 이 되는 순간이 없으니 그 순간이동이 그대로 보인다.
        for (int i = 0; i < 3; i++)
        {
            double sx = a.X + (a.Width
                * (0.5 + (0.46 * Math.Sin((t * (0.31 + (Hash(seed + i + 41) * 0.22))) + (i * 2.1) + seed))));
            double sy = a.Y + (a.Height
                * (0.5 + (0.34 * Math.Sin((t * (0.19 + (Hash(seed + i + 43) * 0.17))) + (i * 1.7)))));
            double pop = Math.Pow(Math.Max(0.0, Math.Sin((t * 2.6) + (i * 2.3) + (seed * 0.5))), 8);

            dc.PushOpacity(0.16 + (pop * 0.5));
            dc.DrawEllipse(VoltMote, null, new Point(sx, sy), 2.4, 2.4);
            dc.DrawEllipse(VoltMoteCore, null, new Point(sx, sy), 0.6, 0.6);
            dc.Pop();
        }

        for (int bolt = 0; bolt < 3; bolt++)
        {
            double life = 1.5 + (Hash(bolt + seed + 71) * 1.4);
            double phase = Phase(t, bolt + seed + 83, life);

            // ⚠ 이 스킨의 핵심: 사건은 수명의 앞 20% 안에서만 일어나고 나머지 프레임은 여기서 끝난다.
            const double Burst = 0.20;
            double u = phase / Burst;
            if (u >= 1.0)
            {
                continue;
            }

            double envelope = Math.Pow(Math.Sin(Math.PI * u), 10);
            if (u > 0.5)
            {
                envelope = Math.Max(envelope, Math.Exp((0.5 - u) * 9.0) * 0.45); // 식는 채널
            }

            if (envelope < 0.05)
            {
                continue;
            }

            // 몇 번째 방전인지를 키에 섞는다 — 안 섞으면 한 모양이 깜빡이는 데칼이 된다.
            int strike = (int)Math.Floor((t + (Hash(bolt + seed + 83) * life)) / life);
            int key = (seed * 13) + (bolt * 31) + (strike * 977);

            // 🔑 가로 흔들림(reach)이 세로 걸음보다 작아야 마디가 45°를 넘는다. 이 부등식이 깨지는 순간
            // 번개가 아니라 파형으로 읽힌다 — 앞선 두 시도가 정확히 그렇게 실패했다.
            double step = (a.Height - 2.0) / Segs;
            double reach = Math.Min(step * 0.72, a.Height * 0.16);
            double lean = (Hash(key + 13) - 0.5) * a.Height * 0.55; // 번개는 수직이되 조금 기운다
            double headX = a.X + 4 + (Hash(key + 5) * Math.Max(1.0, a.Width - 8.0 - Math.Abs(lean)));

            // 겹이 넷인 이유: 굵기 하나로는 falloff 가 안 생겨 '균일한 선'이 되고, 그건 방전이 아니라
            // 낙서로 읽힌다. 넓고 흐린 것에서 얇고 밝은 것까지 굵기·명도가 함께 계단을 이뤄야 빛으로 보인다.
            for (int pass = 0; pass < 4; pass++)
            {
                Pen pen = pass == 0 ? VoltCorona : pass == 1 ? VoltGlow : pass == 2 ? VoltChannel : VoltCore;
                double alpha = pass == 0 ? 0.22 : pass == 1 ? 0.40 : pass == 2 ? 0.70 : 1.0;
                dc.PushOpacity(Math.Min(1.0, envelope * alpha));

                double px = NodeX(key, 0, headX, reach, lean);
                double py = a.Y + 1;
                for (int k = 1; k <= Segs; k++)
                {
                    double nx = NodeX(key, k, headX, reach, lean);
                    double ny = a.Y + 1 + (step * k);
                    dc.DrawLine(pen, new Point(px, py), new Point(nx, ny));
                    px = nx;
                    py = ny;
                }

                dc.Pop();
            }

            // 갈래 하나. 채널이 다음에 갈 쪽의 반대로 튀어야 각이 열린다.
            int at = 1 + (int)(Hash(key + 11) * (Segs - 2));
            double fx = NodeX(key, at, headX, reach, lean);
            double fy = a.Y + 1 + (step * at);
            double away = NodeX(key, at + 1, headX, reach, lean) > fx ? -1.0 : 1.0;
            double bx = fx + (away * reach * 2.1);
            double by = Math.Min(a.Bottom - 1, fy + (step * 0.9));
            double cx = bx + (away * reach * 1.1);
            double cy = Math.Min(a.Bottom - 1, by + (step * 0.5));

            for (int pass = 0; pass < 2; pass++)
            {
                dc.PushOpacity(Math.Min(1.0, envelope * (pass == 0 ? 0.5 : 0.85)));
                Pen pen = pass == 0 ? VoltForkChannel : VoltForkCore;
                dc.DrawLine(pen, new Point(fx, fy), new Point(bx, by));

                // 두 번째 마디는 짧고 다시 꺾인다 — 한 마디짜리 갈래는 그냥 삐침으로 보인다.
                dc.DrawLine(pen, new Point(bx, by), new Point(cx, cy));
                dc.Pop();
            }
        }
    }

    /// <summary>How many segments a bolt's channel has. Shared by the stroke passes and the fork so the two
    /// cannot drift apart — a fork hung on a node the channel does not have is the obvious way this breaks.
    /// </summary>
    private const int Segs = 5;

    /// <summary>A channel node's x: the bolt's lean applied evenly down the descent, plus a per-node sideways
    /// kick that alternates so every kink is a hard reversal rather than a meander.</summary>
    private static double NodeX(int key, int k, double headX, double reach, double lean) =>
        headX + (lean * k / Segs)
        + ((((k & 1) == 0) ? -1.0 : 1.0) * reach * (0.30 + (Hash(key + (k * 17)) * 1.25)));

    /// <summary>
    /// 말차 크림 — cream folding across matte tea, right to left, plus a few foam rings rising. Rounded
    /// everywhere: it has to be tellable from 서리 with the colour removed.
    /// </summary>
    private static void DrawMatcha(DrawingContext dc, Rect a, double t, int seed)
    {
        // 크림 마블 두 층. 서로 다른 속도로 흘러 접히는 인상을 만든다.
        for (int i = 0; i < 9; i++)
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
        for (int i = 0; i < 6; i++)
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
        for (int i = 0; i < 9; i++)
        {
            double life = 5.4 + (Hash(i + seed + 20) * 2.6);
            double phase = Phase(t, i + seed + 5, life);
            double x = a.X - 12 + (phase * (a.Width + 24));
            double y = a.Y + 4 + (Hash(i + seed + 1) * Math.Max(1, a.Height - 8));
            double size = 2.4 + (Hash(i + seed + 3) * 3.4);
            double spin = (t * (0.35 + (Hash(i + seed + 7) * 0.4))) + (Hash(i + seed + 9) * 6.28);
            double alpha = 0.18 + (Math.Sin(phase * Math.PI) * 0.30);
            if (alpha <= 0.01)
            {
                continue;
            }

            double dx = Math.Cos(spin) * size;
            double dy = Math.Sin(spin) * size;
            dc.PushOpacity(alpha);
            dc.PushOpacity(0.45);
            dc.DrawEllipse(i % 2 == 0 ? PrismHaloCyan : PrismHaloViolet, null, new Point(x, y), size * 2.0, size * 2.0);
            dc.Pop();

            // 파편은 두 획이 만나 각을 이룬다 — 한 획이면 그냥 빗금이고, 회전해도 삼각형으로 안 읽힌다.
            Pen edge = i % 2 == 0 ? PrismEdgeCyan : PrismEdgeViolet;
            double ox = Math.Cos(spin + 2.1) * size * 0.8;
            double oy = Math.Sin(spin + 2.1) * size * 0.8;
            dc.DrawLine(edge, new Point(x - dx, y - dy), new Point(x + dx, y + dy));
            dc.DrawLine(edge, new Point(x + dx, y + dy), new Point(x + ox, y + oy));
            dc.DrawEllipse(i % 2 == 0 ? PrismCyan : PrismViolet, null, new Point(x, y), 0.8, 0.8);
            dc.Pop();
        }
    }

    /// <summary>
    /// 잔불 — sparks rising off a bed of coals: bright cores with short trails, swaying as they climb and
    /// shrinking out. The bar is never filled with a picture of fire and its opacity never pulses as a whole.
    /// </summary>
    private static void DrawEmber(DrawingContext dc, Rect a, double t, int seed)
    {
        for (int i = 0; i < 14; i++)
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

            // 발광은 방사형 그라디언트 브러시 한 장 — 캔버스의 shadowBlur 에 해당한다. BlurEffect 는
            // 게임 위 레이어드 윈도에서 CPU 컨볼루션이라 쓸 수 없고, 평평한 원 두 겹은 '동그라미'로 보였다.
            dc.PushOpacity(0.55);
            dc.DrawEllipse(EmberGlow, null, new Point(x, y), radius * 3.2, radius * 3.2);
            dc.Pop();
            dc.DrawEllipse(EmberCore, null, new Point(x, y), Math.Max(0.5, radius * 0.62), Math.Max(0.5, radius * 0.62));
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
        for (int i = 0; i < 11; i++)
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

            // 🔑 6갈래여야 결정으로 읽힌다. 처음엔 2~3갈래로 줄여 그렸는데 그건 눈송이가 아니라 **짧은
            // 빗금**이고, 시안과 나란히 놓으면 그 차이가 가장 먼저 눈에 띈다. 큰 것에는 가지도 친다.
            bool detailed = size > 2.9 && i % 3 == 0; // 온전한 눈송이는 동시에 하나둘만
            dc.PushOpacity(alpha);

            if (detailed)
            {
                dc.PushOpacity(0.5);
                dc.DrawEllipse(FrostHalo, null, new Point(x, y), size * 2.2, size * 2.2);
                dc.Pop();
            }

            Pen pen = detailed ? FrostArm : FrostArmThin;
            for (int arm = 0; arm < 3; arm++)
            {
                double angle = rotation + (arm * Math.PI / 3);
                double dx = Math.Cos(angle) * size;
                double dy = Math.Sin(angle) * size;
                dc.DrawLine(pen, new Point(x - dx, y - dy), new Point(x + dx, y + dy));

                if (!detailed)
                {
                    continue;
                }

                // 가지: 팔 끝 조금 앞에서 양쪽으로. 이게 있어야 '별표'가 아니라 눈송이가 된다.
                for (int side = -1; side <= 1; side += 2)
                {
                    for (int end = -1; end <= 1; end += 2)
                    {
                        double bx = x + (dx * 0.58 * end);
                        double by = y + (dy * 0.58 * end);
                        double ba = angle + (side * 2.38);
                        dc.DrawLine(FrostBranch, new Point(bx, by),
                            new Point(bx + (Math.Cos(ba) * size * 0.30), by + (Math.Sin(ba) * size * 0.30)));
                    }
                }
            }

            dc.Pop();
        }

        // 이따금 반짝이는 서릿발. Math.Pow 로 좁은 봉우리를 만들어 '깜빡'이 되게 한다.
        for (int i = 0; i < 5; i++)
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

    /// <summary>A soft round glow: bright core fading to nothing at the rim. This is the stand-in for the
    /// study's canvas <c>shadowBlur</c> — a real <c>BlurEffect</c> is CPU convolution on a software-rendered
    /// layered window over a game, and flat concentric circles read as circles rather than as light.</summary>
    private static Brush Halo(string outerHex, string coreHex)
    {
        var b = new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop((Color)ColorConverter.ConvertFromString(coreHex)!, 0.0),
                new GradientStop((Color)ColorConverter.ConvertFromString(outerHex)!, 0.34),
                new GradientStop(Colors.Transparent, 1.0),
            },
        };
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

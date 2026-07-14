using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using WaffleMeter.Replay;

namespace WaffleMeter.App.Wpf;

/// <summary>
/// WCL-style positional replay player: a flat 2D map of where each participant stood during a battle,
/// with play/pause + a scrub bar (30 fps interpolated playback), height-on-hover, and click-to-disambiguate
/// for stacked characters. A normal interactive window (it needs focus), unlike the game-overlay panels.
/// Consumes a <see cref="ReplayRecording"/> from the live recorder, saved history, or a .json file.
/// </summary>
public partial class ReplayWindow : Window
{
    // World-Z is shown as a height; the world->meter scale is not yet calibrated (pending a scripted
    // capture), so this is a raw display for now. Adjust ZScale once the unit is known.
    private const double ZScale = 1.0;
    private const double DotRadius = 6;
    private const double HitRadius = 16;

    /// <summary>The boss badge is drawn bigger than a player dot so the fight reads at a glance.</summary>
    private const double BossIconSize = 30;

    /// <summary>How far from the boss's centre its head / back chevrons sit (px).</summary>
    private const double FacingMarkerOffset = 22;

    /// <summary>How much room the auto-frame leaves around the party's spread. Fitting tightly to the dots
    /// read as over-zoomed in practice (the room the fight happens in is the context you want), so this is
    /// set where a viewer stopped scrolling out: ~2.4x the p90 spread.</summary>
    private const double FramePadding = 2.4;
    // With the dense 0x371D delta stream decoded, in-battle position points arrive ~10Hz (measured p90 gap
    // ~0.3s), so a real move never has a multi-second gap. HOLD (don't fake-glide) only across the much
    // larger gaps that mean the entity left capture range (AoI) or phased/teleported — measured at tens of
    // seconds. This threshold sits well above the normal ~0.3s cadence, so real movement glides while the
    // cross-map straight-line teleport artifact is still suppressed.
    private const double MaxInterpGapMs = 1500;
    // Teleport guard (a recall/blink lands two samples far apart while close in time, which would otherwise
    // draw a straight line racing across the map): the time-normalized predicate lives in
    // ReplayGeometry.IsTeleport (shared with the web player, unit-tested) — see Teleport() below.

    private static readonly ReplayMapCatalog MapCatalog = ReplayMapCatalog.Load();

    // The client's boss-mechanic zone catalog (skill code -> the circle/donut/cone/line it paints, and how
    // long the floor telegraphs first). Absent catalog = no zones drawn; everything else still works.
    private static readonly ReplaySkillShapes SkillShapes = ReplaySkillShapes.Load();

    private readonly ReplayRecording _rec;
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _stopwatch = new();
    private readonly List<TrackVisual> _visuals = new();

    // Mechanic zones are rebuilt each frame (a handful on screen at a time) and live UNDER the dots, so a
    // character standing in a zone is still readable.
    private readonly List<System.Windows.Shapes.Path> _zoneShapes = new();

    private ReplayMapInfo? _map;
    private Image? _mapImage;
    private Polygon? _bossFront;
    private Polygon? _bossBack;

    // The auto-framed fight (see FocusOnTheFight) plus the user's zoom/pan on top of it.
    private double _focusX;
    private double _focusY;
    private double _focusRadius = 2000;
    private double _zoom = 1;
    private double _panX;
    private double _panY;
    private Point? _dragFrom;

    private double _currentMs;
    private double _speed = 1.0;
    private bool _playing;
    private bool _suppressScrub;
    private long _lastElapsedMs;

    private readonly bool _autoPlay;
    private readonly double _startMs;

    public ReplayWindow(ReplayRecording recording) : this(recording, autoPlay: true, startMs: 0)
    {
    }

    /// <param name="autoPlay">Start playing immediately (false = paused, e.g. for a static preview).</param>
    /// <param name="startMs">Initial playhead position in ms.</param>
    public ReplayWindow(ReplayRecording recording, bool autoPlay, double startMs)
    {
        _rec = recording;
        _autoPlay = autoPlay;
        _startMs = startMs;
        InitializeComponent();

        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += OnTick;

        Loaded += (_, _) => BuildScene();
        SizeChanged += (_, _) => Render();
        Closed += (_, _) => { _timer.Stop(); _stopwatch.Stop(); };
    }

    /// <summary>Open a replay from a serialized .json recording (shared / web-exported / history file).</summary>
    public static ReplayWindow FromFile(string path)
        => new(ReplaySerializer.Deserialize(File.ReadAllText(path)));

    private void BuildScene()
    {
        // Boss-tagged dungeon map only. Coordinate containment cannot identify the map (instanced maps
        // overlap in world space — see ReplayMapCatalog.ForBoss), so an untagged target (field boss,
        // nightmare arena) gets the relative plot rather than a wrong background.
        _map = MapCatalog.ForBoss(_rec.TargetCode);

        // Header carries the matched map's name so a wrong/missing background is visible at a glance
        // (no map matched = relative plot, no dungeon name shown).
        string? title = _rec.TargetName is { Length: > 0 } name ? name : _map?.NameKo;
        if (title is { Length: > 0 } && _map is { } mapInfo && title != mapInfo.NameKo)
        {
            title = $"{title} · {mapInfo.NameKo}";
        }

        HeaderText.Text = title is { Length: > 0 } ? $"전투 리플레이 — {title}" : "전투 리플레이";

        // How many of the boss's casts actually paint a zone we can draw (the rest are plain swings).
        int mechanics = _rec.Casts.Count(c => SkillShapes.For(c.SkillCode).Count > 0);
        string badge = _rec.BossDefeated ? "처치 완료" : "직전 전투 (미처치)";
        BossBadge.Text = mechanics > 0 ? $"{badge} · 기믹 {mechanics}회" : badge;
        ScrubSlider.Maximum = Math.Max(1, _rec.DurationMs);

        bool anyPoints = _rec.PointCount > 0;
        EmptyNotice.Visibility = anyPoints ? Visibility.Collapsed : Visibility.Visible;

        MapCanvas.Children.Clear();
        LegendPanel.Children.Clear();
        _visuals.Clear();
        _zoneShapes.Clear(); // the canvas was just emptied; drop the stale references with it
        _bossFront = _bossBack = null;
        _mapImage = LoadMapImage();
        if (_mapImage != null)
        {
            MapCanvas.Children.Add(_mapImage); // bottom of the z-order; dots/trails are added on top below
        }

        int colorIdx = 0;
        foreach (ReplayTrack t in VisibleTracks().OrderByDescending(t => t.Points.Count))
        {
            Brush color = t.IsTarget ? Brushes.OrangeRed
                : t.IsSelf ? Brushes.Gold
                : JobBrush(t.Job, colorIdx++);

            // The boss wears the meter's own boss badge (bigger than a player dot, so it reads at a glance);
            // everyone else is a coloured dot.
            FrameworkElement marker = t.IsTarget && JoinIcons.BossIcon is { } icon
                ? new Image
                {
                    Source = icon,
                    Width = BossIconSize,
                    Height = BossIconSize,
                    IsHitTestVisible = false,
                    Visibility = Visibility.Collapsed,
                }
                : new Ellipse
                {
                    Width = (t.IsTarget ? BossIconSize / 2 : DotRadius) * 2,
                    Height = (t.IsTarget ? BossIconSize / 2 : DotRadius) * 2,
                    Fill = color,
                    Stroke = t.IsSelf || t.IsTarget ? Brushes.White : Brushes.Black,
                    StrokeThickness = t.IsSelf || t.IsTarget ? 2 : 1,
                    IsHitTestVisible = false,
                    Visibility = Visibility.Collapsed,
                };

            MapCanvas.Children.Add(marker);

            long[] times = t.Points.Select(p => (long)p.TMs).ToArray();
            _visuals.Add(new TrackVisual(t, color, marker, times));

            AddLegendRow(t, color);
        }

        BuildBossFacingMarkers();
        FocusOnTheFight();

        _currentMs = Math.Clamp(_startMs, 0, _rec.DurationMs);
        SetPlaying(_autoPlay && anyPoints); // autoplay if enabled and there is anything to show
        Render();
    }

    /// <summary>The tracks worth drawing. An entity id can be recycled across a session, so the same
    /// character sometimes shows up twice: once with the path, once as an empty leftover. Drop the empty
    /// twin — it only ever rendered as a "(위치 없음)" legend row.</summary>
    private IEnumerable<ReplayTrack> VisibleTracks()
    {
        var named = _rec.Tracks
            .Where(t => t.Points.Count > 0 && !string.IsNullOrEmpty(t.Nickname))
            .Select(t => (t.Nickname, t.Server))
            .ToHashSet();

        return _rec.Tracks.Where(t =>
            t.Points.Count > 0 || string.IsNullOrEmpty(t.Nickname) || !named.Contains((t.Nickname, t.Server)));
    }

    // The boss's facing: a chevron at its head and one at its back (the back-attack side), pointing the way
    // it was turned. Direction comes from its casts (every cast states the caster's facing), so the markers
    // are hidden whenever the boss hasn't cast anywhere near the current moment rather than pointing stale.
    private void BuildBossFacingMarkers()
    {
        if (_rec.Casts.Count == 0 || !_visuals.Any(v => v.Track.IsTarget))
        {
            return;
        }

        _bossFront = FacingChevron(Color.FromRgb(0xFF, 0xD1, 0x54)); // head
        _bossBack = FacingChevron(Color.FromRgb(0x4F, 0xC3, 0xF7));  // back — the back-attack side
        MapCanvas.Children.Add(_bossFront);
        MapCanvas.Children.Add(_bossBack);
    }

    private static Polygon FacingChevron(Color color)
        => new()
        {
            // A triangle pointing along +X; rotated into place per frame.
            Points = [new Point(9, 0), new Point(-4, -6), new Point(-4, 6)],
            Fill = new SolidColorBrush(color),
            Stroke = Brushes.Black,
            StrokeThickness = 1,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };

    private void AddLegendRow(ReplayTrack t, Brush color)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        row.Children.Add(new Ellipse { Width = 10, Height = 10, Fill = color, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        string label = string.IsNullOrEmpty(t.Nickname) ? (t.IsTarget ? "보스" : $"#{t.Uid}") : t.Nickname!;
        string suffix = t.IsSelf ? " (나)" : !string.IsNullOrEmpty(t.Job) ? $" · {t.Job}" : "";
        string dim = t.Points.Count == 0 ? "  (위치 없음)" : "";
        row.Children.Add(new TextBlock
        {
            Text = label + suffix + dim,
            Foreground = t.Points.Count == 0 ? Brushes.Gray : new SolidColorBrush(Color.FromRgb(0xC8, 0xCC, 0xD2)),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        });
        LegendPanel.Children.Add(row);
    }

    // The world->canvas projection, centred on the fight: world (Cx, Cy) lands on the middle of the canvas
    // and one world unit spans Scale pixels. FlipY=false in map mode (the dungeon art runs world +Y DOWN
    // the image); FlipY=true for the background-less relative plot, where +Y should read as up.
    private readonly record struct Proj(double Scale, double Cx, double Cy, double CanvasCx, double CanvasCy, bool FlipY);

    private Proj Transform()
    {
        const double pad = 28;
        double cw = MapCanvas.ActualWidth > 0 ? MapCanvas.ActualWidth : 800;
        double ch = MapCanvas.ActualHeight > 0 ? MapCanvas.ActualHeight : 480;

        // Half-extent of the view in world units: the fight's own radius, then whatever the user zoomed to.
        double half = Math.Max(200, _focusRadius / _zoom);
        double scale = Math.Min(cw - 2 * pad, ch - 2 * pad) / (2 * half);
        if (scale <= 0 || double.IsInfinity(scale))
        {
            scale = 1;
        }

        return new Proj(scale, _focusX + _panX, _focusY + _panY, cw / 2, ch / 2, FlipY: _map is null);
    }

    private static Point ToScreen(double wx, double wy, Proj p)
        => new(
            p.CanvasCx + (wx - p.Cx) * p.Scale,
            p.CanvasCy + (p.FlipY ? p.Cy - wy : wy - p.Cy) * p.Scale);

    private static (double X, double Y) ToWorld(Point screen, Proj p)
    {
        double dx = (screen.X - p.CanvasCx) / p.Scale;
        double dy = (screen.Y - p.CanvasCy) / p.Scale;
        return (p.Cx + dx, p.FlipY ? p.Cy - dy : p.Cy + dy);
    }

    /// <summary>
    /// Frame the view on the FIGHT, not on every coordinate in the recording. A dungeon map dwarfs the room
    /// a boss is pulled in, and one stray point — a phase teleport, a decode artifact — would zoom the whole
    /// map out until the fight is a speck. That is exactly what a plain bounds-fit did.
    /// <para>
    /// Centre = where the boss was (its MEDIAN position, so its dashing around doesn't drag the frame);
    /// radius = the p90 distance of everyone's positions from that centre, so the party's real spread sets
    /// the frame while the outlying 10 % cannot stretch it — then <see cref="FramePadding"/> of room around
    /// it, because a fight is easier to read with the surrounding room in view than cropped to the dots.
    /// Clamped so a stationary fight still gets a sane view. The user can zoom/pan from there; double-click
    /// restores this.
    /// </para>
    /// </summary>
    private void FocusOnTheFight()
    {
        _zoom = 1;
        _panX = _panY = 0;

        List<ReplayPoint> boss = _rec.Tracks.FirstOrDefault(t => t.IsTarget)?.Points.ToList() ?? [];
        List<ReplayPoint> all = _rec.Tracks.SelectMany(t => t.Points).ToList();
        if (all.Count == 0)
        {
            (_focusX, _focusY, _focusRadius) = (0, 0, 2000);
            return;
        }

        List<ReplayPoint> centreOn = boss.Count > 0 ? boss : all;
        _focusX = Median(centreOn.Select(p => (double)p.X));
        _focusY = Median(centreOn.Select(p => (double)p.Y));

        double[] dists = all
            .Select(p => Math.Sqrt(Sq(p.X - _focusX) + Sq(p.Y - _focusY)))
            .OrderBy(d => d)
            .ToArray();

        _focusRadius = Math.Clamp(dists[(int)((dists.Length - 1) * 0.9)] * FramePadding, 2500, 30000);
    }

    private static double Sq(double v) => v * v;

    private static double Median(IEnumerable<double> values)
    {
        double[] sorted = values.OrderBy(v => v).ToArray();
        return sorted.Length == 0 ? 0 : sorted[sorted.Length / 2];
    }

    // Build the map background element (bottom of the canvas). Returns null when there is no matched map or
    // the image can't be loaded — the replay then falls back to the relative plot with no background.
    private Image? LoadMapImage()
    {
        if (_map is not { } map)
        {
            return null;
        }

        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(map.ImagePath, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();
            return new Image { Source = bmp, Opacity = 0.92, IsHitTestVisible = false, Stretch = Stretch.Fill };
        }
        catch
        {
            _map = null; // don't try to project against a map we couldn't draw
            return null;
        }
    }

    // Position the map image so its full world extent aligns with the current projection (it overhangs the
    // canvas and is clipped). world (min,min) -> image top-left because +Y maps down (FlipY=false here).
    private void PlaceMapImage(Proj tf)
    {
        if (_mapImage is null || _map is not { } map)
        {
            return;
        }

        Point tl = ToScreen(map.WorldMinX, map.WorldMinY, tf);
        Canvas.SetLeft(_mapImage, tl.X);
        Canvas.SetTop(_mapImage, tl.Y);
        _mapImage.Width = Math.Max(1, (map.WorldMaxX - map.WorldMinX) * tf.Scale);
        _mapImage.Height = Math.Max(1, (map.WorldMaxY - map.WorldMinY) * tf.Scale);
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (!_playing)
        {
            return;
        }

        long now = _stopwatch.ElapsedMilliseconds;
        double dt = now - _lastElapsedMs;
        _lastElapsedMs = now;

        _currentMs += dt * _speed;
        if (_currentMs >= _rec.DurationMs)
        {
            _currentMs = 0; // loop
        }

        Render();
    }

    private void Render()
    {
        if (_visuals.Count == 0)
        {
            return;
        }

        Proj tf = Transform();
        PlaceMapImage(tf);
        RenderZones(tf);

        foreach (TrackVisual v in _visuals)
        {
            if (!TryInterpolate(v, _currentMs, out double wx, out double wy, out double wz, out bool stale))
            {
                v.Dot.Visibility = Visibility.Collapsed;
                v.HasScreen = false;
                if (v.Track.IsTarget)
                {
                    HideBossFacing();
                }

                continue;
            }

            // Just the moving marker (no trailing path) — the boss + characters glide linearly between samples.
            Point p = ToScreen(wx, wy, tf);
            double half = (v.Dot.Width > 0 ? v.Dot.Width : DotRadius * 2) / 2;
            Canvas.SetLeft(v.Dot, p.X - half);
            Canvas.SetTop(v.Dot, p.Y - half);
            v.Dot.Visibility = Visibility.Visible;
            v.Dot.Opacity = stale ? 0.5 : 1.0; // slight dim while holding a stale position (out of range / gap)
            v.Screen = p;
            v.CurrentZ = wz;

            if (v.Track.IsTarget)
            {
                PlaceBossFacing(p, tf);
            }
            v.HasScreen = true;
        }

        if (!_suppressScrub)
        {
            _suppressScrub = true;
            ScrubSlider.Value = _currentMs;
            _suppressScrub = false;
        }

        TimeText.Text = $"{Fmt(_currentMs)} / {Fmt(_rec.DurationMs)}";
    }

    // The boss's mechanics: draw every zone that is on screen right now — the floor telegraph before the
    // hit (amber) and the hit itself (red) — under the character dots. Shapes are rebuilt per frame rather
    // than pooled: only a handful overlap at once, and the alternative (retained visuals with animated
    // transforms) buys nothing on a software-rendered canvas.
    private void RenderZones(Proj tf)
    {
        foreach (System.Windows.Shapes.Path p in _zoneShapes)
        {
            MapCanvas.Children.Remove(p);
        }

        _zoneShapes.Clear();
        if (_rec.Casts.Count == 0)
        {
            return;
        }

        List<ActiveZone> active = ReplayZones.ActiveAt(_rec.Casts, SkillShapes, _currentMs, PositionAt, BossUid);

        // Zones go directly above the map image (index 0) so the dots and trails stay on top.
        int insertAt = _mapImage is null ? 0 : 1;
        foreach (ActiveZone zone in active)
        {
            var geometry = new PathGeometry { FillRule = FillRule.EvenOdd }; // a donut's hole reads as a hole
            foreach (List<(double X, double Y)> loop in ReplayZones.Outline(zone))
            {
                if (loop.Count < 3)
                {
                    continue;
                }

                Point start = ToScreen(loop[0].X, loop[0].Y, tf);
                var figure = new PathFigure { StartPoint = start, IsClosed = true, IsFilled = true };
                for (int i = 1; i < loop.Count; i++)
                {
                    figure.Segments.Add(new LineSegment(ToScreen(loop[i].X, loop[i].Y, tf), isStroked: true));
                }

                geometry.Figures.Add(figure);
            }

            if (geometry.Figures.Count == 0)
            {
                continue;
            }

            geometry.Freeze();
            (Brush fill, Brush stroke, double thickness) = ReplayZoneRenderer.Paint(zone.Telegraphing);
            var path = new System.Windows.Shapes.Path
            {
                Data = geometry,
                Fill = fill,
                Stroke = stroke,
                StrokeThickness = thickness,
                IsHitTestVisible = false,
            };

            MapCanvas.Children.Insert(insertAt++, path);
            _zoneShapes.Add(path);
        }
    }

    /// <summary>The battle's boss — caster-anchored zones sit on it (0 when the recording has no target).</summary>
    private int BossUid => _rec.Tracks.FirstOrDefault(t => t.IsTarget)?.Uid ?? 0;

    // Put the head (gold) and back (blue = the back-attack side) chevrons on either side of the boss,
    // pointing the way it was turned at this moment.
    private void PlaceBossFacing(Point boss, Proj tf)
    {
        if (_bossFront is null || _bossBack is null)
        {
            return;
        }

        if (ReplayZones.FacingAt(_rec.Casts, _currentMs) is not { } facingDeg)
        {
            HideBossFacing(); // no cast anywhere near now — don't point a stale direction
            return;
        }

        // World facing -> screen angle. In map mode world +Y runs DOWN the image; in the relative plot it
        // runs UP, and the marker has to follow the same flip the positions do.
        double rad = facingDeg * Math.PI / 180.0;
        double screenRad = Math.Atan2((tf.FlipY ? -1 : 1) * Math.Sin(rad), Math.Cos(rad));
        double deg = screenRad * 180.0 / Math.PI;

        Place(_bossFront, boss, screenRad, deg, +1);
        Place(_bossBack, boss, screenRad, deg, -1);

        static void Place(Polygon chevron, Point boss, double screenRad, double deg, int sign)
        {
            double x = boss.X + sign * FacingMarkerOffset * Math.Cos(screenRad);
            double y = boss.Y + sign * FacingMarkerOffset * Math.Sin(screenRad);
            chevron.RenderTransform = new RotateTransform(sign > 0 ? deg : deg + 180);
            Canvas.SetLeft(chevron, x);
            Canvas.SetTop(chevron, y);
            chevron.Visibility = Visibility.Visible;
        }
    }

    private void HideBossFacing()
    {
        if (_bossFront is not null)
        {
            _bossFront.Visibility = Visibility.Collapsed;
        }

        if (_bossBack is not null)
        {
            _bossBack.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>An entity's interpolated position at a playback time, for a zone that is stuck to the
    /// player it marked (it follows them). Null when that entity has no track / no sample there.</summary>
    private (double X, double Y)? PositionAt(int uid, double ms)
    {
        foreach (TrackVisual v in _visuals)
        {
            if (v.Track.Uid == uid && TryInterpolate(v, ms, out double x, out double y, out _, out _))
            {
                return (x, y);
            }
        }

        return null;
    }

    // Position at ms. Interpolates between two samples only when they are close in time; across a large
    // gap (out-of-range / phase / teleport) it HOLDS the earlier known position and reports stale=true,
    // rather than gliding along a fake straight line. Clamps to the first/last sample at the ends.
    private static bool TryInterpolate(TrackVisual v, double ms, out double x, out double y, out double z, out bool stale)
    {
        x = y = z = 0;
        stale = false;
        IReadOnlyList<ReplayPoint> pts = v.Track.Points;
        if (pts.Count == 0)
        {
            return false;
        }

        if (ms <= pts[0].TMs)
        {
            x = pts[0].X; y = pts[0].Y; z = pts[0].Z; return true;
        }

        if (ms >= pts[^1].TMs)
        {
            x = pts[^1].X; y = pts[^1].Y; z = pts[^1].Z;
            stale = ms - pts[^1].TMs > MaxInterpGapMs; // held past the last known sample
            return true;
        }

        int lo = 0, hi = v.Times.Length - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) >> 1;
            if (v.Times[mid] <= ms)
            {
                lo = mid;
            }
            else
            {
                hi = mid;
            }
        }

        ReplayPoint a = pts[lo], b = pts[hi];
        double span = b.TMs - a.TMs;
        if (span > MaxInterpGapMs || Teleport(a, b))
        {
            x = a.X; y = a.Y; z = a.Z; // hold the last known position through the gap/teleport
            stale = true;
            return true;
        }

        double f = span <= 0 ? 0 : (ms - a.TMs) / span;
        x = a.X + (b.X - a.X) * f;
        y = a.Y + (b.Y - a.Y) * f;
        z = a.Z + (b.Z - a.Z) * f;
        return true;
    }

    // A recall/blink: two consecutive samples whose implied speed exceeds any legit locomotion. Snap
    // across it (hold, then jump) instead of gliding a straight line over the map. See ReplayGeometry.
    private static bool Teleport(ReplayPoint a, ReplayPoint b) => ReplayGeometry.IsTeleport(a, b);

    // ---- controls ----
    private void PlayButton_Click(object sender, RoutedEventArgs e) => SetPlaying(!_playing);

    private void SetPlaying(bool play)
    {
        _playing = play;
        PlayButton.Content = play ? "❚❚" : "▶";
        if (play)
        {
            _lastElapsedMs = _stopwatch.ElapsedMilliseconds;
            if (!_stopwatch.IsRunning)
            {
                _stopwatch.Start();
            }

            _timer.Start();
        }
        else
        {
            _timer.Stop();
        }
    }

    private void Speed_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string s } && double.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, out double sp))
        {
            _speed = sp;
        }
    }

    private void ScrubSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressScrub)
        {
            return;
        }

        _currentMs = e.NewValue;
        Render();
    }

    // ---- zoom / pan (the fight frames itself; this is for a closer look) ----
    private void MapCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Zoom about the cursor: whatever world point is under it must stay under it.
        Point m = e.GetPosition(MapCanvas);
        (double wx, double wy) = ToWorld(m, Transform());

        _zoom = Math.Clamp(_zoom * (e.Delta > 0 ? 1.2 : 1 / 1.2), 0.15, 20);

        (double wx2, double wy2) = ToWorld(m, Transform());
        _panX += wx - wx2;
        _panY += wy - wy2;
        Render();
    }

    private void MapCanvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragFrom = e.GetPosition(MapCanvas);
        MapCanvas.CaptureMouse();
    }

    private void MapCanvas_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        _dragFrom = null;
        MapCanvas.ReleaseMouseCapture();
    }

    // ---- hover (height) + click (disambiguate stacked) ----
    private void MapCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        Point m = e.GetPosition(MapCanvas);

        if (_dragFrom is { } from && e.RightButton == MouseButtonState.Pressed)
        {
            Proj tf = Transform();
            _panX -= (m.X - from.X) / tf.Scale;
            _panY += (tf.FlipY ? 1 : -1) * (m.Y - from.Y) / tf.Scale;
            _dragFrom = m;
            Render();
            return;
        }

        TrackVisual? hit = Nearest(m, HitRadius);
        if (hit is null)
        {
            HoverInfo.Text = "";
            return;
        }

        HoverInfo.Text = $"{TrackName(hit.Track)}{JobSuffix(hit.Track)} · 고도 {hit.CurrentZ * ZScale:F0} m";
    }

    private void MapCanvas_MouseLeave(object sender, MouseEventArgs e) => HoverInfo.Text = "";

    private void MapCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            _zoom = 1; // double-click: back to the auto-framed fight
            _panX = _panY = 0;
            StackPopup.IsOpen = false;
            Render();
            return;
        }

        Point m = e.GetPosition(MapCanvas);
        List<TrackVisual> near = _visuals
            .Where(v => v.HasScreen && (v.Screen - m).Length <= HitRadius)
            .OrderBy(v => (v.Screen - m).Length)
            .ToList();

        if (near.Count < 2)
        {
            StackPopup.IsOpen = false;
            return;
        }

        StackList.Children.Clear();
        StackList.Children.Add(new TextBlock
        {
            Text = "겹친 캐릭터 고도",
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 4),
        });
        foreach (TrackVisual v in near)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
            row.Children.Add(new Ellipse { Width = 9, Height = 9, Fill = v.Color, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            row.Children.Add(new TextBlock
            {
                Text = $"{TrackName(v.Track)}{JobSuffix(v.Track)} — {v.CurrentZ * ZScale:F0} m",
                Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0xCC, 0xD2)),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
            });
            StackList.Children.Add(row);
        }

        Point screen = MapCanvas.PointToScreen(m);
        StackPopup.HorizontalOffset = screen.X + 12;
        StackPopup.VerticalOffset = screen.Y + 12;
        StackPopup.IsOpen = true;
    }

    private TrackVisual? Nearest(Point m, double radius)
    {
        TrackVisual? best = null;
        double bestD = radius;
        foreach (TrackVisual v in _visuals)
        {
            if (!v.HasScreen)
            {
                continue;
            }

            double d = (v.Screen - m).Length;
            if (d <= bestD)
            {
                bestD = d;
                best = v;
            }
        }

        return best;
    }

    private static string TrackName(ReplayTrack t) => string.IsNullOrEmpty(t.Nickname) ? (t.IsTarget ? "보스" : $"#{t.Uid}") : t.Nickname!;

    private static string JobSuffix(ReplayTrack t) => t.IsSelf ? " (나)" : !string.IsNullOrEmpty(t.Job) ? $" · {t.Job}" : "";

    private static string Fmt(double ms)
    {
        int total = (int)(ms / 1000);
        return $"{total / 60}:{total % 60:00}";
    }

    private static readonly Color[] Palette =
    [
        Color.FromRgb(0x4F, 0xC3, 0xF7), Color.FromRgb(0x81, 0xC7, 0x84), Color.FromRgb(0xFF, 0xB7, 0x4D),
        Color.FromRgb(0xBA, 0x68, 0xC8), Color.FromRgb(0x4D, 0xB6, 0xAC), Color.FromRgb(0xF0, 0x62, 0x92),
        Color.FromRgb(0x9C, 0xCC, 0x65), Color.FromRgb(0x7E, 0x91, 0xF5), Color.FromRgb(0xFF, 0x8A, 0x65),
        Color.FromRgb(0x4D, 0xD0, 0xE1), Color.FromRgb(0xA1, 0x88, 0x7F), Color.FromRgb(0xDC, 0xE7, 0x75),
    ];

    private static Brush JobBrush(string? job, int idx) => new SolidColorBrush(Palette[idx % Palette.Length]);

    private sealed class TrackVisual(ReplayTrack track, Brush color, FrameworkElement dot, long[] times)
    {
        public ReplayTrack Track { get; } = track;
        public Brush Color { get; } = color;

        /// <summary>The marker on the map: a coloured dot for a player, the boss badge for the target.</summary>
        public FrameworkElement Dot { get; } = dot;
        public long[] Times { get; } = times;
        public Point Screen { get; set; }
        public bool HasScreen { get; set; }
        public double CurrentZ { get; set; }
    }
}

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
    private const double TrailMs = 6000;
    private const double HitRadius = 16;
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

    private readonly ReplayRecording _rec;
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _stopwatch = new();
    private readonly List<TrackVisual> _visuals = new();

    private ReplayMapInfo? _map;
    private Image? _mapImage;

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
        _map = MapCatalog.ForBoss(_rec.TargetCode);
        string? title = _rec.TargetName is { Length: > 0 } name ? name : _map?.NameKo;
        HeaderText.Text = title is { Length: > 0 } ? $"전투 리플레이 — {title}" : "전투 리플레이";
        BossBadge.Text = _rec.BossDefeated ? "처치 완료" : "직전 전투 (미처치)";
        ScrubSlider.Maximum = Math.Max(1, _rec.DurationMs);

        bool anyPoints = _rec.PointCount > 0;
        EmptyNotice.Visibility = anyPoints ? Visibility.Collapsed : Visibility.Visible;

        MapCanvas.Children.Clear();
        LegendPanel.Children.Clear();
        _visuals.Clear();
        _mapImage = LoadMapImage();
        if (_mapImage != null)
        {
            MapCanvas.Children.Add(_mapImage); // bottom of the z-order; dots/trails are added on top below
        }

        int colorIdx = 0;
        foreach (ReplayTrack t in _rec.Tracks.OrderByDescending(t => t.Points.Count))
        {
            Brush color = t.IsTarget ? Brushes.OrangeRed
                : t.IsSelf ? Brushes.Gold
                : JobBrush(t.Job, colorIdx++);

            var trail = new Polyline { Stroke = color, StrokeThickness = 1.6, Opacity = 0.5, IsHitTestVisible = false };
            var dot = new Ellipse
            {
                Width = DotRadius * 2,
                Height = DotRadius * 2,
                Fill = color,
                Stroke = t.IsSelf ? Brushes.White : t.IsTarget ? Brushes.White : Brushes.Black,
                StrokeThickness = t.IsSelf || t.IsTarget ? 2 : 1,
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed,
            };

            MapCanvas.Children.Add(trail);
            MapCanvas.Children.Add(dot);

            long[] times = t.Points.Select(p => (long)p.TMs).ToArray();
            _visuals.Add(new TrackVisual(t, color, dot, trail, times));

            AddLegendRow(t, color);
        }

        _currentMs = Math.Clamp(_startMs, 0, _rec.DurationMs);
        SetPlaying(_autoPlay && anyPoints); // autoplay if enabled and there is anything to show
        Render();
    }

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

    // The world->canvas projection. Two modes:
    //  - Map mode (a dungeon map matched the boss): keep the map's own coordinate frame — world +Y goes
    //    DOWN the image (FlipY=false) — and fit the *action* (recording bounds, padded) into the canvas so
    //    the background aligns under the dots. Vx/Vy is the top-left world corner of the view.
    //  - Relative mode (no map): auto-fit the point cloud with world +Y UP (FlipY=true), Vx=minX, Vy=maxY.
    private readonly record struct Proj(double Scale, double Vx, double Vy, double OffX, double OffY, bool FlipY);

    private Proj Transform()
    {
        const double pad = 28;
        double cw = MapCanvas.ActualWidth > 0 ? MapCanvas.ActualWidth : 800;
        double ch = MapCanvas.ActualHeight > 0 ? MapCanvas.ActualHeight : 480;
        (float MinX, float MinY, float MaxX, float MaxY)? b = _rec.Bounds();
        if (b is not { } bb)
        {
            return new Proj(1, 0, 0, cw / 2, ch / 2, FlipY: true);
        }

        if (_map is { } map)
        {
            // View = the action, padded for context, clamped to the map's world extent.
            double padW = Math.Max(0.15 * Math.Max(bb.MaxX - bb.MinX, bb.MaxY - bb.MinY), 1500);
            double vMinX = Math.Max(map.WorldMinX, bb.MinX - padW);
            double vMaxX = Math.Min(map.WorldMaxX, bb.MaxX + padW);
            double vMinY = Math.Max(map.WorldMinY, bb.MinY - padW);
            double vMaxY = Math.Min(map.WorldMaxY, bb.MaxY + padW);
            double vw = Math.Max(1, vMaxX - vMinX);
            double vh = Math.Max(1, vMaxY - vMinY);
            double s = Math.Min((cw - 2 * pad) / vw, (ch - 2 * pad) / vh);
            if (s <= 0 || double.IsInfinity(s))
            {
                s = 1;
            }

            return new Proj(s, vMinX, vMinY, (cw - vw * s) / 2, (ch - vh * s) / 2, FlipY: false);
        }

        double w = Math.Max(1, bb.MaxX - bb.MinX);
        double h = Math.Max(1, bb.MaxY - bb.MinY);
        double scale = Math.Min((cw - 2 * pad) / w, (ch - 2 * pad) / h);
        if (scale <= 0 || double.IsInfinity(scale))
        {
            scale = 1;
        }

        return new Proj(scale, bb.MinX, bb.MaxY, (cw - w * scale) / 2, (ch - h * scale) / 2, FlipY: true);
    }

    private static Point ToScreen(double wx, double wy, Proj p)
        => p.FlipY
            ? new Point(p.OffX + (wx - p.Vx) * p.Scale, p.OffY + (p.Vy - wy) * p.Scale)
            : new Point(p.OffX + (wx - p.Vx) * p.Scale, p.OffY + (wy - p.Vy) * p.Scale);

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

        foreach (TrackVisual v in _visuals)
        {
            if (!TryInterpolate(v, _currentMs, out double wx, out double wy, out double wz, out bool stale))
            {
                v.Dot.Visibility = Visibility.Collapsed;
                v.Trail.Points = new PointCollection();
                v.HasScreen = false;
                continue;
            }

            Point p = ToScreen(wx, wy, tf);
            Canvas.SetLeft(v.Dot, p.X - DotRadius);
            Canvas.SetTop(v.Dot, p.Y - DotRadius);
            v.Dot.Visibility = Visibility.Visible;
            v.Dot.Opacity = stale ? 0.35 : 1.0; // dim when we're holding a stale position (out of range / gap)
            v.Screen = p;
            v.CurrentZ = wz;
            v.HasScreen = true;

            // Trail: the contiguous recent run ending at the playhead, broken at any gap > MaxInterpGapMs
            // (so a glide line is never drawn across a gap). Walk back from the latest point <= currentMs.
            IReadOnlyList<ReplayPoint> tp = v.Track.Points;
            int end = -1;
            for (int i = 0; i < tp.Count && tp[i].TMs <= _currentMs; i++)
            {
                end = i;
            }

            var pts = new PointCollection();
            if (end >= 0)
            {
                var rev = new List<Point>();
                for (int i = end; i >= 0; i--)
                {
                    if (i < end && (tp[i + 1].TMs - tp[i].TMs > MaxInterpGapMs || _currentMs - tp[i].TMs > TrailMs
                                    || Teleport(tp[i], tp[i + 1])))
                    {
                        break;
                    }

                    rev.Add(ToScreen(tp[i].X, tp[i].Y, tf));
                }

                for (int i = rev.Count - 1; i >= 0; i--)
                {
                    pts.Add(rev[i]);
                }
            }

            if (!stale)
            {
                pts.Add(p); // connect the trail to the live interpolated dot only when not in a gap
            }

            v.Trail.Points = pts;
        }

        if (!_suppressScrub)
        {
            _suppressScrub = true;
            ScrubSlider.Value = _currentMs;
            _suppressScrub = false;
        }

        TimeText.Text = $"{Fmt(_currentMs)} / {Fmt(_rec.DurationMs)}";
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

    // ---- hover (height) + click (disambiguate stacked) ----
    private void MapCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        Point m = e.GetPosition(MapCanvas);
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

    private sealed class TrackVisual(ReplayTrack track, Brush color, Ellipse dot, Polyline trail, long[] times)
    {
        public ReplayTrack Track { get; } = track;
        public Brush Color { get; } = color;
        public Ellipse Dot { get; } = dot;
        public Polyline Trail { get; } = trail;
        public long[] Times { get; } = times;
        public Point Screen { get; set; }
        public bool HasScreen { get; set; }
        public double CurrentZ { get; set; }
    }
}

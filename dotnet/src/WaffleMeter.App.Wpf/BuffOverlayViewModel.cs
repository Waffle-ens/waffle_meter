using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace WaffleMeter.App.Wpf;

/// <summary>View model for the combat-assist overlay: the local player's active buff slots, refreshed on a
/// timer from the data layer. Slots are reconciled in place (by code) so the icons don't flicker.</summary>
public sealed class BuffOverlayViewModel : INotifyPropertyChanged
{
    public ObservableCollection<BuffSlotVM> Slots { get; } = new();

    private Visibility _emptyVisibility = Visibility.Visible;
    /// <summary>Shown ("버프 없음" placeholder) when there are no active slots.</summary>
    public Visibility EmptyVisibility { get => _emptyVisibility; private set => Set(ref _emptyVisibility, value); }

    /// <summary>Replace the slot list from a fresh snapshot, reusing existing rows by code so only the
    /// countdown text + ring progress change on a normal tick.</summary>
    public void Update(IReadOnlyList<(int Code, string Name, long RemainingMs, long DurationMs, bool ByOther)> buffs)
    {
        // remove slots no longer present
        for (int i = Slots.Count - 1; i >= 0; i--)
        {
            if (!buffs.Any(b => b.Code == Slots[i].Code))
            {
                Slots.RemoveAt(i);
            }
        }

        foreach ((int code, string name, long remainingMs, long durationMs, bool byOther) in buffs)
        {
            BuffSlotVM? existing = Slots.FirstOrDefault(s => s.Code == code);
            if (existing is null)
            {
                Slots.Add(new BuffSlotVM(code, name, remainingMs, durationMs, byOther));
            }
            else
            {
                existing.SetRemaining(remainingMs, durationMs);
            }
        }

        EmptyVisibility = Slots.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

/// <summary>One buff slot: icon + a live remaining-time countdown + a border ring that shrinks with the
/// remaining time (a visual cooldown/duration helper).</summary>
public sealed class BuffSlotVM : INotifyPropertyChanged
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private const double RingRadius = 18; // matches the 40px icon slot in XAML

    public BuffSlotVM(int code, string name, long remainingMs, long durationMs, bool byOther)
    {
        Code = code;
        Name = name;
        IconSource = JoinIcons.Skill(code);
        ByOther = byOther;
        SetRemaining(remainingMs, durationMs);
    }

    public int Code { get; }
    public string Name { get; }
    public ImageSource? IconSource { get; }
    public bool ByOther { get; }

    private string _remainingText = string.Empty;
    public string RemainingText { get => _remainingText; private set => Set(ref _remainingText, value); }

    private Geometry? _ring;
    /// <summary>The ring arc drawn around the icon; sweeps a shorter arc as the buff runs down, so the border
    /// visually "disappears" toward expiry. Null (no ring) when the duration is unknown.</summary>
    public Geometry? Ring { get => _ring; private set => Set(ref _ring, value); }

    public void SetRemaining(long remainingMs, long durationMs)
    {
        long s = Math.Max(0, remainingMs) / 1000;
        RemainingText = s >= 60 ? $"{s / 60}:{s % 60:D2}" : s.ToString(Inv) + "s";
        Ring = BuildRing(durationMs > 0 ? Math.Clamp((double)remainingMs / durationMs, 0, 1) : 0);
    }

    // A clockwise arc from 12 o'clock spanning 360°·progress, around the icon's center. Progress 1 = full
    // ring, →0 = no ring. Returned as a frozen geometry for cheap software rendering.
    private static Geometry? BuildRing(double progress)
    {
        if (progress <= 0.001)
        {
            return null;
        }

        const double cx = RingRadius, cy = RingRadius;
        if (progress >= 0.999)
        {
            var full = new EllipseGeometry(new Point(cx, cy), RingRadius, RingRadius);
            full.Freeze();
            return full;
        }

        double sweep = 360.0 * progress;
        double a0 = -90 * Math.PI / 180.0;                 // start at 12 o'clock
        double a1 = (-90 + sweep) * Math.PI / 180.0;       // clockwise
        var start = new Point(cx + RingRadius * Math.Cos(a0), cy + RingRadius * Math.Sin(a0));
        var end = new Point(cx + RingRadius * Math.Cos(a1), cy + RingRadius * Math.Sin(a1));
        var fig = new PathFigure { StartPoint = start, IsClosed = false, IsFilled = false };
        fig.Segments.Add(new ArcSegment(end, new Size(RingRadius, RingRadius), 0, sweep > 180, SweepDirection.Clockwise, true));
        var geo = new PathGeometry();
        geo.Figures.Add(fig);
        geo.Freeze();
        return geo;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

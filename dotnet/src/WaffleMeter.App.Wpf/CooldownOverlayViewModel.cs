using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using WaffleMeter.Data;

namespace WaffleMeter.App.Wpf;

/// <summary>View model for the skill-cooldown overlay: one square slot per skill of the recognised character's
/// job, with a dark pie that wipes away as the cooldown runs out. Slots are reconciled in place (by
/// shared-cooldown group) so icons never jump under the cursor.
/// <para>Visually deliberately unlike the buff overlay — square icons and a filling/emptying pie, against that
/// overlay's circular icons and outline ring — because the two windows sit side by side and mean different
/// things: one says "this is active on me", the other "this is ready to press".</para></summary>
public sealed class CooldownOverlayViewModel : INotifyPropertyChanged
{
    private static readonly Brush PanelBg = Freeze(new SolidColorBrush(Color.FromArgb(0xCC, 0x14, 0x18, 0x21)));
    private static readonly Brush PanelBorder = Freeze(new SolidColorBrush(Color.FromArgb(0x99, 0x78, 0x84, 0x9B)));

    public ObservableCollection<CooldownSlotVM> Slots { get; } = new();

    private double _iconScale = 1.0;
    /// <summary>Uniform scale for the whole slot. The native design is the 42px icon on a 46px cell, driven off
    /// the same 40px = 100% convention the buff overlay uses so both sliders mean the same thing.</summary>
    public double IconScale { get => _iconScale; private set => Set(ref _iconScale, value); }

    /// <summary>Set the icon size in px. The clamp must match <c>MeterSettings.CooldownUiIconSize</c> — if the
    /// two disagree the saved value and the screen drift apart with nothing to show for it.</summary>
    public void SetIconSize(int px) => IconScale = Math.Clamp(px, 32, 80) / 40.0;

    private Brush _textBrush = Brushes.White;
    /// <summary>Countdown-text color (from the setting).</summary>
    public Brush TextBrush { get => _textBrush; private set => Set(ref _textBrush, value); }

    private string _textColorHex = string.Empty;
    /// <summary>Set the countdown-text color from a hex string; falls back to white on a bad value.</summary>
    public void SetTextColor(string hex)
    {
        if (_textColorHex == hex)
        {
            return;
        }

        _textColorHex = hex;
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(string.IsNullOrWhiteSpace(hex) ? "#FFFFFF" : hex)!;
            var b = new SolidColorBrush(c);
            b.Freeze();
            TextBrush = b;
        }
        catch
        {
            TextBrush = Brushes.White;
        }
    }

    private bool _showBackground;
    /// <summary>When true, draw a panel background + border + placeholder so the (possibly empty) window can be
    /// found and dragged before any skill has been used.</summary>
    public bool ShowBackground
    {
        get => _showBackground;
        set
        {
            if (_showBackground == value)
            {
                return;
            }

            _showBackground = value;
            PanelBackground = value ? PanelBg : Brushes.Transparent;
            PanelBorderBrush = value ? PanelBorder : Brushes.Transparent;
            PanelBorderThickness = value ? new Thickness(1) : new Thickness(0);
            RecomputePlaceholder();
        }
    }

    private Brush _panelBackground = Brushes.Transparent;
    public Brush PanelBackground { get => _panelBackground; private set => Set(ref _panelBackground, value); }

    private Brush _panelBorderBrush = Brushes.Transparent;
    public Brush PanelBorderBrush { get => _panelBorderBrush; private set => Set(ref _panelBorderBrush, value); }

    private Thickness _panelBorderThickness;
    public Thickness PanelBorderThickness { get => _panelBorderThickness; private set => Set(ref _panelBorderThickness, value); }

    private Visibility _emptyVisibility = Visibility.Collapsed;
    /// <summary>Placeholder shown only with the background on and no slots — i.e. before the character has been
    /// recognised, or with every skill unticked in the picker. Once the job is known the grid fills on its own,
    /// so this is a short-lived state rather than the normal one.</summary>
    public Visibility EmptyVisibility { get => _emptyVisibility; private set => Set(ref _emptyVisibility, value); }

    private void RecomputePlaceholder() => EmptyVisibility = _showBackground && Slots.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private static Brush Freeze(Brush b) { b.Freeze(); return b; }

    /// <summary>Replace the slot list from a fresh snapshot, reusing rows by group id so a normal tick only
    /// changes the pie and the countdown text. Rows are moved only when their position actually differs —
    /// unconditional moves make the icons shiver.</summary>
    public void Update(IReadOnlyList<SkillCooldownView> rows)
    {
        for (int i = Slots.Count - 1; i >= 0; i--)
        {
            if (!rows.Any(r => r.GroupId == Slots[i].GroupId))
            {
                Slots.RemoveAt(i);
            }
        }

        for (int target = 0; target < rows.Count; target++)
        {
            SkillCooldownView r = rows[target];

            int current = -1;
            for (int i = 0; i < Slots.Count; i++)
            {
                if (Slots[i].GroupId == r.GroupId)
                {
                    current = i;
                    break;
                }
            }

            if (current < 0)
            {
                Slots.Insert(Math.Min(target, Slots.Count), new CooldownSlotVM(r));
                continue;
            }

            Slots[current].Update(r);
            int destination = Math.Min(target, Slots.Count - 1);
            if (current != destination)
            {
                Slots.Move(current, destination);
            }
        }

        RecomputePlaceholder();
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

/// <summary>One cooldown slot: a square skill icon, a dark pie covering the part of the cooldown still to
/// run, and the remaining seconds over it.</summary>
public sealed class CooldownSlotVM : INotifyPropertyChanged
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // The pie is drawn on the icon's own 42x42 box in absolute coordinates. Its radius reaches past the
    // corners (half-diagonal is 29.7) so a square icon is covered corner to corner; the XAML clips it back to
    // the rounded square. A radius that only reached the edge midpoint would leave four bright corners.
    private const double Box = 42;
    private const double Center = Box / 2;
    private const double PieRadius = 31;

    public CooldownSlotVM(SkillCooldownView row)
    {
        GroupId = row.GroupId;
        Name = row.Name;
        IconSource = JoinIcons.Skill(row.DisplayCode);
        Update(row);
    }

    public int GroupId { get; }

    private string _name = string.Empty;
    /// <summary>Tooltip text. Kept updatable because a shared-cooldown group can report under either of its
    /// skills' codes.</summary>
    public string Name { get => _name; private set => Set(ref _name, value); }

    public ImageSource? IconSource { get; }

    private string _remainingText = string.Empty;
    public string RemainingText { get => _remainingText; private set => Set(ref _remainingText, value); }

    private Visibility _textVisibility = Visibility.Collapsed;
    /// <summary>The countdown is drawn only while the skill is actually cooling — a "0" on every ready slot is
    /// noise on a window that is mostly ready slots.</summary>
    public Visibility TextVisibility { get => _textVisibility; private set => Set(ref _textVisibility, value); }

    private Geometry? _wipe;
    /// <summary>The dark pie over the icon: a clockwise wedge from 12 o'clock spanning the fraction of the
    /// cooldown still to run, so it sweeps away to nothing as the skill becomes usable. Null when ready.</summary>
    public Geometry? Wipe { get => _wipe; private set => Set(ref _wipe, value); }

    private double _readyOpacity = 1.0;
    /// <summary>Slight lift for a ready skill so a full row of them still reads as "all up" at a glance without
    /// spending an alpha-composited surface per icon.</summary>
    public double ReadyOpacity { get => _readyOpacity; private set => Set(ref _readyOpacity, value); }

    public void Update(SkillCooldownView row)
    {
        Name = row.Name;
        if (row.IsReady || row.RemainingMs <= 0)
        {
            RemainingText = string.Empty;
            TextVisibility = Visibility.Collapsed;
            Wipe = null;
            ReadyOpacity = 1.0;
            return;
        }

        long s = (row.RemainingMs + 999) / 1000; // round up: "1s" must not read as ready
        RemainingText = s >= 60 ? $"{s / 60}:{s % 60:D2}" : s.ToString(Inv);
        TextVisibility = Visibility.Visible;
        ReadyOpacity = 0.85;

        // The denominator can be unknown (a correction arrived before any cast of this skill was seen). Draw a
        // full wipe then — "on cooldown, share unknown" — rather than guessing a total and animating a lie.
        double progress = row.TotalMs > 0 ? Math.Clamp((double)row.RemainingMs / row.TotalMs, 0, 1) : 1;
        Wipe = BuildWipe(progress);
    }

    // A clockwise pie wedge from 12 o'clock spanning 360°·progress. Progress 1 = the whole icon covered,
    // →0 = nothing left. Frozen: the panel re-renders in software, so an unfrozen geometry per slot per tick is
    // the one cost worth removing.
    private static Geometry? BuildWipe(double progress)
    {
        if (progress <= 0.002)
        {
            return null;
        }

        if (progress >= 0.998)
        {
            var full = new EllipseGeometry(new Point(Center, Center), PieRadius, PieRadius);
            full.Freeze();
            return full;
        }

        double sweep = 360.0 * progress;
        double a0 = -90 * Math.PI / 180.0;
        double a1 = (-90 + sweep) * Math.PI / 180.0;
        var start = new Point(Center + PieRadius * Math.Cos(a0), Center + PieRadius * Math.Sin(a0));
        var end = new Point(Center + PieRadius * Math.Cos(a1), Center + PieRadius * Math.Sin(a1));

        var fig = new PathFigure { StartPoint = new Point(Center, Center), IsClosed = true, IsFilled = true };
        fig.Segments.Add(new LineSegment(start, false));
        fig.Segments.Add(new ArcSegment(end, new Size(PieRadius, PieRadius), 0, sweep > 180, SweepDirection.Clockwise, false));
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

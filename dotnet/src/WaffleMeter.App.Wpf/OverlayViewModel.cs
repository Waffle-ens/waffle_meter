using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using WaffleMeter.App.Core;
using WaffleMeter.Data;

namespace WaffleMeter.App.Wpf;

/// <summary>
/// Overlay view model. <see cref="Update"/> runs on the dispatcher and reconciles the ranked row
/// list from the live DPS report, mirroring the React MeterList/MeterRow: sort by metric desc, top 8,
/// always append self, per-row progress ratio + fill color by contribution tier, masked name + power
/// badge. Row clicks raise <see cref="SelectionToggled"/> (App opens/closes the detail window).
/// </summary>
public sealed class OverlayViewModel : INotifyPropertyChanged
{
    private static readonly Brush UserBar = Gradient("#15c98f", "#0b8f72");
    private static readonly Brush NormalBar = Gradient("#f6c65b", "#d68a21");
    private static readonly Brush WarningBar = Gradient("#ff9f45", "#d96d19");
    private static readonly Brush ErrorBar = Gradient("#ef4444", "#991b1b");
    private static readonly Brush NameDefault = Solid("#ffffff");
    private static readonly Brush NameServerA = Solid("#7dd3fc");
    private static readonly Brush NameServerB = Solid("#f0abfc");

    public OverlayViewModel(string version)
    {
        _status = $"waffle_meter {version}";
    }

    public ObservableCollection<RowViewModel> Rows { get; } = new();

    /// <summary>Raised when a row is clicked (App toggles the detail window for that uid).</summary>
    public event Action<int>? SelectionToggled;

    public void ToggleSelection(int uid) => SelectionToggled?.Invoke(uid);

    private string _targetName = "-";
    public string TargetName { get => _targetName; private set => Set(ref _targetName, value); }

    private string _duration = "0.0s";
    public string Duration { get => _duration; private set => Set(ref _duration, value); }

    private string _status;
    public string Status { get => _status; set => Set(ref _status, value); }

    private Visibility _placeholderVisibility = Visibility.Visible;
    public Visibility PlaceholderVisibility { get => _placeholderVisibility; private set => Set(ref _placeholderVisibility, value); }

    public void Update(DpsReport report)
    {
        TargetName = report.Target?.Mob.Name ?? "-";
        long durationMs = Math.Max(report.BattleEnd - report.BattleStart, 0);
        Duration = $"{durationMs / 1000.0:F1}s";

        // metric = dps (default damageValueMode). Sort desc, take 8, always include self.
        List<Entry> entries = report.Information
            .Select(kv => new Entry(kv.Key, kv.Value, report.Contributors.FirstOrDefault(c => c.Id == kv.Key)))
            .OrderByDescending(e => e.Info.Dps)
            .ToList();

        var display = entries.Take(8).ToList();
        int selfIndex = entries.FindIndex(e => e.User?.IsExecutor == true);
        if (selfIndex >= 0 && !display.Contains(entries[selfIndex]))
        {
            display.Add(entries[selfIndex]); // always show self, even outside top 8
        }

        double topMetric = Math.Max(display.Count > 0 ? display.Max(e => e.Info.Dps) : 0.0, 1.0);

        for (int i = 0; i < display.Count; i++)
        {
            Entry e = display[i];
            bool isUser = e.User?.IsExecutor == true;
            double contribution = e.Info.Contribution;
            int power = e.User?.Power ?? 0;
            int server = e.User?.Server ?? 0;
            double ratio = Math.Clamp(e.Info.Dps / topMetric, 0.0, 1.0);

            var row = new RowViewModel(
                Id: e.Uid,
                Rank: i + 1,
                Name: MeterFormat.DisplayName(e.User?.Nickname, NameDisplay.All, isUser),
                PowerText: power > 0 ? MeterFormat.FormatPower(power) : string.Empty,
                PowerVisible: power > 0 ? Visibility.Visible : Visibility.Collapsed,
                DamageText: MeterFormat.FormatDps(e.Info.Dps),
                PercentText: MeterFormat.FormatPercent(contribution),
                BarRatio: ratio,
                BarRest: 1.0 - ratio,
                FillBrush: isUser ? UserBar : contribution < 3 ? ErrorBar : contribution < 5 ? WarningBar : NormalBar,
                NameBrush: MeterFormat.ServerTier(server) switch
                {
                    ServerColorTier.A => NameServerA,
                    ServerColorTier.B => NameServerB,
                    _ => NameDefault,
                },
                IsUser: isUser);

            if (i < Rows.Count)
            {
                Rows[i] = row;
            }
            else
            {
                Rows.Add(row);
            }
        }

        for (int i = Rows.Count - 1; i >= display.Count; i--)
        {
            Rows.RemoveAt(i);
        }

        PlaceholderVisibility = Rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private readonly record struct Entry(int Uid, DpsInformation Info, User? User);

    private static Brush Gradient(string from, string to)
    {
        var brush = new LinearGradientBrush(ParseColor(from), ParseColor(to), 0.0);
        brush.Freeze();
        return brush;
    }

    private static Brush Solid(string hex)
    {
        var brush = new SolidColorBrush(ParseColor(hex));
        brush.Freeze();
        return brush;
    }

    private static Color ParseColor(string hex) => (Color)ColorConverter.ConvertFromString(hex)!;

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

public sealed record RowViewModel(
    int Id,
    int Rank,
    string Name,
    string PowerText,
    Visibility PowerVisible,
    string DamageText,
    string PercentText,
    double BarRatio,
    double BarRest,
    Brush FillBrush,
    Brush NameBrush,
    bool IsUser);

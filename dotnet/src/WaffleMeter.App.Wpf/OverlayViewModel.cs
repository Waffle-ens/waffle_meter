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
    private readonly MeterSettings _settings;
    private readonly MeterColorTheme _theme;

    // Rebuilt from the theme whenever a color changes (MeterColorTheme.Changed); rows are records that
    // bake in the brush references, so a theme change re-runs Update on the last report.
    private Brush _userBar = null!, _normalBar = null!, _warningBar = null!, _errorBar = null!;
    private Brush _nameDefault = null!, _nameServerA = null!, _nameServerB = null!;
    private Brush _amountBrush = null!, _dpsBrush = null!, _percentBrush = null!;
    private DpsReport? _lastReport;

    public OverlayViewModel(string version, MeterSettings settings, MeterColorTheme theme)
    {
        _settings = settings;
        Settings = settings;
        _theme = theme;
        Theme = theme;
        RebuildBrushes();
        theme.Changed += (_, _) =>
        {
            RebuildBrushes();
            if (_lastReport is { } report)
            {
                Update(report); // repaint rows with the new colors
            }
        };
        _status = $"waffle_meter {version}";
    }

    /// <summary>Exposed for the overlay to bind opacity/font/etc. directly.</summary>
    public MeterSettings Settings { get; }

    /// <summary>Exposed so XAML can bind theme-driven chrome (e.g. combat-time color) directly.</summary>
    public MeterColorTheme Theme { get; }

    private Brush _combatTimeBrush = null!;
    public Brush CombatTimeBrush { get => _combatTimeBrush; private set => Set(ref _combatTimeBrush, value); }

    private void RebuildBrushes()
    {
        _userBar = ThemeGradient(_theme.UserBarFrom, _theme.UserBarTo);
        _normalBar = ThemeGradient(_theme.NormalBarFrom, _theme.NormalBarTo);
        _warningBar = ThemeGradient(_theme.WarningBarFrom, _theme.WarningBarTo);
        _errorBar = ThemeGradient(_theme.ErrorBarFrom, _theme.ErrorBarTo);
        _nameDefault = ThemeSolid(_theme.ServerDefaultColor);
        _nameServerA = ThemeSolid(_theme.ServerAColor);
        _nameServerB = ThemeSolid(_theme.ServerBColor);
        _amountBrush = ThemeSolid(_theme.MeterStatAmount);  // power badge (React MeterRow.tsx:207)
        _dpsBrush = ThemeSolid(_theme.MeterStatDps);        // damage value (MeterRow.tsx:126)
        _percentBrush = ThemeSolid(_theme.MeterStatPercent); // percent (MeterRow.tsx:119/127)
        CombatTimeBrush = ThemeSolid(_theme.CombatTimeColor);
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
        _lastReport = report;
        TargetName = report.Target?.Mob.Name ?? "-";
        long durationMs = Math.Max(report.BattleEnd - report.BattleStart, 0);
        Duration = $"{durationMs / 1000.0:F1}s";

        // metric = amount (total mode) or dps. Sort desc, take 8, always include self.
        bool total = _settings.UseTotalDamage;
        bool entire = _settings.UseEntireContribution;
        NameDisplay nameMode = _settings.NameDisplayMode;
        double rowHeight = _settings.RowHeight;
        double Metric(DpsInformation info) => total ? info.Amount : info.Dps;

        List<Entry> entries = report.Information
            .Select(kv => new Entry(kv.Key, kv.Value, report.Contributors.FirstOrDefault(c => c.Id == kv.Key)))
            .OrderByDescending(e => Metric(e.Info))
            .ToList();

        var display = entries.Take(8).ToList();
        int selfIndex = entries.FindIndex(e => e.User?.IsExecutor == true);
        if (selfIndex >= 0 && !display.Contains(entries[selfIndex]))
        {
            display.Add(entries[selfIndex]); // always show self, even outside top 8
        }

        double topMetric = Math.Max(display.Count > 0 ? display.Max(e => Metric(e.Info)) : 0.0, 1.0);

        for (int i = 0; i < display.Count; i++)
        {
            Entry e = display[i];
            bool isUser = e.User?.IsExecutor == true;
            double contribution = e.Info.Contribution; // fill tier always uses party contribution
            int power = e.User?.Power ?? 0;
            int server = e.User?.Server ?? 0;
            double ratio = Math.Clamp(Metric(e.Info) / topMetric, 0.0, 1.0);

            var row = new RowViewModel(
                Id: e.Uid,
                Rank: i + 1,
                Name: MeterFormat.DisplayName(e.User?.Nickname, nameMode, isUser),
                PowerText: power > 0 ? MeterFormat.FormatPower(power) : string.Empty,
                PowerVisible: power > 0 ? Visibility.Visible : Visibility.Collapsed,
                DamageText: total ? MeterFormat.FormatAmount(e.Info.Amount) : MeterFormat.FormatDps(e.Info.Dps),
                PercentText: MeterFormat.FormatPercent(entire ? e.Info.EntireContribution : contribution),
                BarRatio: ratio,
                BarRest: 1.0 - ratio,
                FillBrush: isUser ? _userBar : contribution < 3 ? _errorBar : contribution < 5 ? _warningBar : _normalBar,
                NameBrush: MeterFormat.ServerTier(server) switch
                {
                    ServerColorTier.A => _nameServerA,
                    ServerColorTier.B => _nameServerB,
                    _ => _nameDefault,
                },
                PowerBrush: _amountBrush,
                DamageBrush: _dpsBrush,
                PercentBrush: _percentBrush,
                IsUser: isUser,
                RowHeight: rowHeight);

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

    // angle 0 = left->right, matching the React `linear-gradient(to right, ...)` bars.
    private static Brush ThemeGradient(string from, string to)
    {
        var brush = new LinearGradientBrush(ToColor(from), ToColor(to), 0.0);
        brush.Freeze();
        return brush;
    }

    private static Brush ThemeSolid(string value)
    {
        var brush = new SolidColorBrush(ToColor(value));
        brush.Freeze();
        return brush;
    }

    // ColorString handles hex AND rgba(...) (ColorConverter.ConvertFromString cannot parse rgba()).
    private static Color ToColor(string value) =>
        ColorString.TryParse(value, out ColorRgba c) ? Color.FromArgb(c.A, c.R, c.G, c.B) : Colors.White;

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
    Brush PowerBrush,
    Brush DamageBrush,
    Brush PercentBrush,
    bool IsUser,
    double RowHeight);

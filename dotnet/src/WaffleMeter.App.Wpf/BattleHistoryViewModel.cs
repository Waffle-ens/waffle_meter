using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WaffleMeter.App.Core;
using WaffleMeter.Data;

namespace WaffleMeter.App.Wpf;

/// <summary>
/// View model for the battle-history panel (port of React HistoryPanel + useHistory). Rows are built
/// from a saved-battle snapshot captured on the consumer thread; each row keeps its frozen DpsReport so
/// a click can replay it without re-reading the data layer. Newest first. UI-thread only.
/// </summary>
public sealed class BattleHistoryViewModel : INotifyPropertyChanged
{
    private readonly MeterColorTheme _theme;
    private List<(int Index, DpsReport Report)> _battles = [];

    public BattleHistoryViewModel(MeterColorTheme theme, MeterSettings settings)
    {
        _theme = theme;
        Settings = settings;
        theme.Changed += (_, _) => Rebuild();
    }

    /// <summary>Exposed so the panel can bind the user's overlay font.</summary>
    public MeterSettings Settings { get; }

    public ObservableCollection<BattleHistoryRowViewModel> Rows { get; } = new();

    /// <summary>Raised when a row is clicked (App shows that battle in the overlay).</summary>
    public event Action<DpsReport>? BattleSelected;

    public void SelectBattle(DpsReport report) => BattleSelected?.Invoke(report);

    /// <summary>Raised when a row's ▶ is clicked (App opens the positional replay for that battle).</summary>
    public event Action<DpsReport>? ReplayRequested;

    public void RequestReplay(DpsReport report) => ReplayRequested?.Invoke(report);

    /// <summary>Does this battle have a replay to play? Set by App (it owns the engine + the saved-recording
    /// folder). Null = the replay feature is off / unavailable, and no row shows a ▶.</summary>
    public Func<DpsReport, bool>? HasReplay { get; set; }

    private Visibility _emptyVisibility = Visibility.Visible;
    public Visibility EmptyVisibility { get => _emptyVisibility; private set => Set(ref _emptyVisibility, value); }

    /// <summary>Replace the row list from a fresh snapshot (marshalled from the consumer thread).</summary>
    public void SetBattles(List<(int Index, DpsReport Report)> battles)
    {
        _battles = battles;
        Rebuild();
    }

    private void Rebuild()
    {
        var reportByIndex = _battles.ToDictionary(b => b.Index, b => b.Report);
        Color from = ToColor(_theme.BossBarFrom);
        Color to = ToColor(_theme.BossBarTo);
        Brush durationBrush = FrozenSolid(ToColor(_theme.BossRightValue));

        Rows.Clear();
        foreach (BattleHistoryItem item in BattleHistory.Build(_battles))
        {
            if (!reportByIndex.TryGetValue(item.Index, out DpsReport? report))
            {
                continue;
            }

            Rows.Add(new BattleHistoryRowViewModel(item, report, from, to, durationBrush, HasReplay?.Invoke(report) == true));
        }

        EmptyVisibility = Rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static Color ToColor(string value) =>
        ColorString.TryParse(value, out ColorRgba c) ? Color.FromArgb(c.A, c.R, c.G, c.B) : Colors.White;

    private static Brush FrozenSolid(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
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

/// <summary>One saved-battle row. Carries the frozen report for replay on click.</summary>
public sealed class BattleHistoryRowViewModel
{
    public BattleHistoryRowViewModel(
        BattleHistoryItem item, DpsReport report, Color barFrom, Color barTo, Brush durationBrush, bool hasReplay)
    {
        Report = report;
        ReplayVisibility = hasReplay ? Visibility.Visible : Visibility.Collapsed;
        MobName = item.MobName;
        DateTimeText = FormatDateTime(item.BattleStartMs);
        DurationText = MeterFormat.FormatBattleTime(item.BattleTimeMs);
        IsBoss = item.IsBoss;
        BossIcon = JoinIcons.BossIcon;
        IconOpacity = item.IsBoss ? 1.0 : 0.4;
        DurationBrush = durationBrush;

        var gradient = new LinearGradientBrush(barFrom, barTo, 0.0) { Opacity = item.IsBoss ? 0.8 : 0.2 };
        gradient.Freeze();
        BackgroundBrush = gradient;
    }

    public DpsReport Report { get; }
    public string MobName { get; }
    public string DateTimeText { get; }
    public string DurationText { get; }
    public bool IsBoss { get; }
    public BitmapImage? BossIcon { get; }
    public double IconOpacity { get; }
    public Brush BackgroundBrush { get; }
    public Brush DurationBrush { get; }

    /// <summary>The ▶ shows only when this battle actually has a recording to open.</summary>
    public Visibility ReplayVisibility { get; }

    private static string FormatDateTime(long ms)
    {
        if (ms <= 0)
        {
            return string.Empty;
        }

        return DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
    }
}

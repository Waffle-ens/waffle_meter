using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using WaffleMeter.App.Core;

namespace WaffleMeter.App.Wpf;

/// <summary>
/// View model for the 오드 목록 panel — every character this install has seen, with the 오드 it was last
/// holding. Rows come from <see cref="AetherRoster"/> (pure); this type only turns them into bindable
/// strings. UI-thread only; rebuilt each time the panel is opened and whenever the active character's
/// balance changes while it is on screen.
/// </summary>
public sealed class AetherPanelViewModel : INotifyPropertyChanged
{
    public AetherPanelViewModel(MeterSettings settings) => Settings = settings;

    /// <summary>Exposed so the panel can bind the user's overlay font, like the other panels.</summary>
    public MeterSettings Settings { get; }

    public ObservableCollection<AetherRowViewModel> Rows { get; } = new();

    private Visibility _emptyVisibility = Visibility.Visible;
    public Visibility EmptyVisibility { get => _emptyVisibility; private set => Set(ref _emptyVisibility, value); }

    private string _summaryText = string.Empty;
    public string SummaryText { get => _summaryText; private set => Set(ref _summaryText, value); }

    public void SetRows(IReadOnlyList<AetherRosterRow> rows)
    {
        Rows.Clear();
        foreach (AetherRosterRow row in rows)
        {
            Rows.Add(new AetherRowViewModel(row));
        }

        EmptyVisibility = Rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SummaryText = Rows.Count == 0
            ? string.Empty
            : string.Format(
                CultureInfo.InvariantCulture,
                "캐릭터 {0}명 · 합계 {1:N0}",
                Rows.Count,
                rows.Sum(r => (long)r.Total));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

/// <summary>One character row in the 오드 목록.</summary>
public sealed class AetherRowViewModel
{
    public AetherRowViewModel(AetherRosterRow row)
    {
        Label = row.Label;
        JobText = row.SubLabel;
        JobVisibility = row.SubLabel.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        BaseText = row.Base.ToString("N0", CultureInfo.InvariantCulture);
        BonusText = row.Bonus > 0 ? "+" + row.Bonus.ToString("N0", CultureInfo.InvariantCulture) : string.Empty;
        BonusVisibility = row.Bonus > 0 ? Visibility.Visible : Visibility.Collapsed;
        TotalText = row.Total.ToString("N0", CultureInfo.InvariantCulture);
        CurrentBadgeVisibility = row.IsCurrent ? Visibility.Visible : Visibility.Collapsed;
        SeenText = FormatSeen(row.SavedAtMs);
    }

    public string Label { get; }
    public string JobText { get; }
    public Visibility JobVisibility { get; }
    public string BaseText { get; }
    public string BonusText { get; }
    public Visibility BonusVisibility { get; }
    public string TotalText { get; }
    public Visibility CurrentBadgeVisibility { get; }
    public string SeenText { get; }

    /// <summary>How stale this balance is. The packet only ever carries the ACTIVE character's 오드, so every
    /// row but the current one is a memory — saying how old it is, is the whole point.</summary>
    private static string FormatSeen(long savedAtMs)
    {
        // The store parses any long that TryParse accepts, so a hand-edited settings file can carry a value
        // outside DateTimeOffset's range — which would throw here and take the whole list down.
        if (savedAtMs <= 0
            || savedAtMs < DateTimeOffset.MinValue.ToUnixTimeMilliseconds()
            || savedAtMs > DateTimeOffset.MaxValue.ToUnixTimeMilliseconds())
        {
            return string.Empty;
        }

        TimeSpan age = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeMilliseconds(savedAtMs);
        if (age < TimeSpan.Zero)
        {
            return "방금";
        }

        return age.TotalMinutes < 1 ? "방금"
            : age.TotalHours < 1 ? $"{(int)age.TotalMinutes}분 전"
            : age.TotalDays < 1 ? $"{(int)age.TotalHours}시간 전"
            : age.TotalDays < 30 ? $"{(int)age.TotalDays}일 전"
            : DateTimeOffset.FromUnixTimeMilliseconds(savedAtMs).ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}

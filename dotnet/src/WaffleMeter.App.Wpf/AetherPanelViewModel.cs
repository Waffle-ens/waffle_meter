using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using WaffleMeter.App.Core;

namespace WaffleMeter.App.Wpf;

/// <summary>
/// View model for the 컨텐츠 관리 panel — every character this install has seen, with the 오드 it was last
/// holding and its weekly 성역 clears. Rows come from <see cref="AetherRoster"/> (pure); this type only turns
/// them into bindable strings. UI-thread only; rebuilt each time the panel is opened and whenever the active
/// character's balance or a weekly counter changes while it is on screen.
/// </summary>
public sealed class AetherPanelViewModel : INotifyPropertyChanged
{
    public AetherPanelViewModel(MeterSettings settings) => Settings = settings;

    /// <summary>Exposed so the panel can bind the user's overlay font, like the other panels.</summary>
    public MeterSettings Settings { get; }

    public ObservableCollection<AetherRowViewModel> Rows { get; } = new();

    /// <summary>Raised when a row's ✕ is clicked, with that character's identity hash (App forgets it and
    /// refreshes). The list is the only place a remembered character can be dropped — a renamed character
    /// keeps its old hash forever otherwise, since the key is a hash of (server, nickname).</summary>
    public event Action<string>? RemoveRequested;

    public void RequestRemove(string identityHash)
    {
        if (!string.IsNullOrWhiteSpace(identityHash))
        {
            RemoveRequested?.Invoke(identityHash);
        }
    }

    /// <summary>Raised when a weekly counter chip is clicked, with <c>(identityHash, slug)</c>. The counter is
    /// normally the server's own value, but the meter only hears it while it is running — a raid cleared with
    /// the meter closed, or before it was installed, would read as un-cleared until that character next logs
    /// in. Flipping it by hand is the escape hatch; the next broadcast still wins.</summary>
    public event Action<string, string>? WeeklyToggleRequested;

    public void RequestWeeklyToggle(string identityHash, string slug)
    {
        if (!string.IsNullOrWhiteSpace(identityHash) && !string.IsNullOrWhiteSpace(slug))
        {
            WeeklyToggleRequested?.Invoke(identityHash, slug);
        }
    }

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

/// <summary>One weekly 성역 chip on a character row: the raid's icon and "남은/주간 지급" (1/1 → 0/1).</summary>
public sealed class WeeklyContentCellViewModel
{
    public WeeklyContentCellViewModel(string identityHash, WeeklyContentCell cell)
    {
        IdentityHash = identityHash;
        Slug = cell.Content.Slug;
        IconSource = "pack://application:,,,/WaffleMeter.App.Wpf;component/Icons/" + cell.Content.IconFile;
        CountText = string.Concat(
            cell.Remaining.ToString(CultureInfo.InvariantCulture), "/",
            cell.Grant.ToString(CultureInfo.InvariantCulture));

        Cleared = cell.Remaining <= 0;

        // Only the ICON recedes when a raid is done — the count stays fully legible. Dimming the whole chip
        // (as this did at first) makes a character who has cleared all three render as an empty row, which
        // reads as a bug rather than as the best possible state.
        IconOpacity = Cleared ? 0.5 : 1.0;

        string state = Cleared ? "이번 주 클리어함" : "이번 주 아직 안 잡음";
        string source = cell.Known ? string.Empty : "\n(기록 없음 — 이 캐릭터로 접속하면 실제 값으로 채워집니다)";
        ToolTip = $"{cell.Content.Name} · {state}{source}\n클릭: 클리어 여부 직접 변경";
    }

    public string IdentityHash { get; }
    public string Slug { get; }
    public string IconSource { get; }
    public string CountText { get; }
    public bool Cleared { get; }
    public double IconOpacity { get; }
    public string ToolTip { get; }
}

/// <summary>One character row in the 컨텐츠 관리 목록.</summary>
public sealed class AetherRowViewModel
{
    public AetherRowViewModel(AetherRosterRow row)
    {
        IdentityHash = row.IdentityHash;
        Weekly = row.WeeklyCells
            .Select(c => new WeeklyContentCellViewModel(row.IdentityHash, c))
            .ToList();
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

    public string IdentityHash { get; }
    public IReadOnlyList<WeeklyContentCellViewModel> Weekly { get; }
    public string Label { get; }
    public string JobText { get; }
    public string RemoveTooltip => $"{Label} 기록 삭제";
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

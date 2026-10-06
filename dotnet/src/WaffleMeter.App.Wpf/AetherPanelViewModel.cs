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

    /// <summary>One 총 키나 line per server (see <see cref="CurrencyRoster.ServerKina"/>), shown under the list.</summary>
    public ObservableCollection<ServerKinaViewModel> ServerKina { get; } = new();

    private Visibility _serverKinaVisibility = Visibility.Collapsed;
    public Visibility ServerKinaVisibility { get => _serverKinaVisibility; private set => Set(ref _serverKinaVisibility, value); }

    /// <summary>Replace the 총 키나 lines. Kept apart from <see cref="SetRows"/> because the corridor clock rebuilds
    /// the rows once a second while a visit runs, and that has nothing to do with kinah.</summary>
    public void SetServerKina(IReadOnlyList<ServerKinaLine> lines)
    {
        ServerKina.Clear();
        foreach (ServerKinaLine line in lines)
        {
            ServerKina.Add(new ServerKinaViewModel(line));
        }

        ServerKinaVisibility = ServerKina.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

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

    /// <summary>Advance only the corridor clocks, leaving the row objects (and therefore the scroll position,
    /// hover state and any open tooltip) alone. Falls back to a full rebuild the moment the shape of the list
    /// stops matching — a corridor that ran out, or a character that appeared.</summary>
    public void UpdateCorridorTimes(IReadOnlyList<AetherRosterRow> rows)
    {
        if (rows.Count != Rows.Count)
        {
            SetRows(rows);
            return;
        }

        for (int i = 0; i < rows.Count; i++)
        {
            AetherRowViewModel row = Rows[i];
            IReadOnlyList<AbyssCorridorCell> cells = rows[i].CorridorCells;
            if (!string.Equals(row.IdentityHash, rows[i].IdentityHash, StringComparison.Ordinal)
                || cells.Count != row.Corridors.Count)
            {
                SetRows(rows);
                return;
            }

            for (int c = 0; c < cells.Count; c++)
            {
                if (!row.Corridors[c].TryAdvance(cells[c]))
                {
                    SetRows(rows);
                    return;
                }
            }
        }
    }

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

/// <summary>One 어비스 회랑 chip on a character row: the corridor's name and its remaining 이용 시간 as "m:ss".
/// <para>Read-only, unlike the weekly chips. There is no hand-toggle because there is nothing sensible to toggle
/// to — the value is a clock the server stocks at 점령전, not a yes/no the user can restate.</para></summary>
public sealed class AbyssCorridorCellViewModel : INotifyPropertyChanged
{
    public AbyssCorridorCellViewModel(AbyssCorridorCell cell)
    {
        TicketId = cell.Corridor.TicketId;
        Name = cell.Corridor.ShortName;
        TierText = cell.Corridor.Tier == AbyssCorridorTier.Lower ? "하층"
            : cell.Corridor.Tier == AbyssCorridorTier.Middle ? "중층"
            : "거점";
        Spent = cell.Spent;
        Ticking = cell.Ticking;
        Inferred = cell.Inferred;

        // "~2:10". The tilde is the ONLY thing separating a corridor whose time was measured on this character
        // from one assumed full because a character beside it on the same server holds the artifact. It has to
        // live in the text: the three colour states are taken (measured / 진행 중 / 소진), and dimming a guess
        // would read as "spent", which is the opposite of what it says. Without any mark the panel can show a
        // 0:00 on the character that walked the corridor and 2:10 on the one beside it — true, but unreadable
        // as anything but a bug.
        TimeText = (cell.Inferred ? "~" : string.Empty) + FormatTime(cell.RemainingMs);

        // Only the label recedes when a corridor is used up — the clock stays legible, the same treatment the
        // weekly chips use so a character who has spent everything doesn't render as an empty row.
        NameOpacity = Spent ? 0.5 : 1.0;

        string state = Spent
            ? "이용 시간 모두 사용"
            : Ticking
                ? "지금 입장 중 — 남은 시간이 흐르는 중입니다"
                : cell.Inferred
                    ? $"남은 이용 시간 {FormatTime(cell.RemainingMs)} (추정)"
                    : $"남은 이용 시간 {TimeText}";

        // The tooltip says which way an inferred number can be wrong — only a corridor this character burned
        // while the meter was closed makes it too high — and what it takes to enter, since the side holding an
        // artifact says nothing about whether THIS character is geared for that layer.
        string source = cell.Inferred
            ? "\n이번 점령 주기에 우리 진영이 점령한 회랑입니다.\n이 캐릭터로는 아직 들어간 적이 없어 이용 시간이 그대로 남아 있는 것으로 보고 있습니다."
                + (cell.Corridor.Tier == AbyssCorridorTier.Middle
                    ? "\n입장 조건: 아이템 레벨 3000"
                    : cell.Corridor.Tier == AbyssCorridorTier.Lower ? "\n입장 조건: 아이템 레벨 1000" : string.Empty)
            : "\n(이 캐릭터가 실제로 받은 남은 이용 시간입니다)";
        ToolTip = $"{cell.Corridor.Tier switch
        {
            AbyssCorridorTier.Lower => "어비스 하층",
            AbyssCorridorTier.Middle => "어비스 중층",
            _ => "거점",
        }} · {cell.Corridor.Name} 아티팩트\n{state}{source}";
    }

    public int TicketId { get; }
    public string Name { get; }
    public string TierText { get; }

    private string _timeText = string.Empty;

    /// <summary>"m:ss". The only value on this panel that moves without a packet behind it, so it is the only
    /// one that is observable — the alternative, rebuilding the row list once a second for the 130 seconds of a
    /// visit, resets the scroll position and cancels whatever tooltip the user is reading.</summary>
    public string TimeText
    {
        get => _timeText;
        private set
        {
            if (!string.Equals(_timeText, value, StringComparison.Ordinal))
            {
                _timeText = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TimeText)));
            }
        }
    }

    public bool Spent { get; }
    public bool Ticking { get; }

    /// <summary>Whether the number came from a same-server character rather than from this one. Shown as the
    /// "~" on <see cref="TimeText"/>, and checked by <see cref="TryAdvance"/> so a chip that stops being a
    /// guess rebuilds even when the value does not move (2:10 assumed → 2:10 measured).</summary>
    public bool Inferred { get; }

    public double NameOpacity { get; }
    public string ToolTip { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Advance the readout for a still-running clock. Returns false when the new cell is no longer the
    /// same thing (a different corridor, or one that has since been spent), which the caller reads as "the row
    /// list itself is out of date and has to be rebuilt".</summary>
    internal bool TryAdvance(AbyssCorridorCell cell)
    {
        if (cell.Corridor.TicketId != TicketId
            || cell.Spent != Spent
            || cell.Ticking != Ticking
            || cell.Inferred != Inferred)
        {
            return false;
        }

        TimeText = (Inferred ? "~" : string.Empty) + FormatTime(cell.RemainingMs);
        return true;
    }

    /// <summary>"2:10" / "0:54" / "0:00". Rounded UP so a corridor with 200 ms left still reads "0:01" rather
    /// than announcing "0:00" on a clock that has not actually run out.</summary>
    private static string FormatTime(long remainingMs)
    {
        long seconds = remainingMs <= 0 ? 0 : (remainingMs + 999) / 1000;
        return string.Concat(
            (seconds / 60).ToString(CultureInfo.InvariantCulture), ":",
            (seconds % 60).ToString("00", CultureInfo.InvariantCulture));
    }
}

/// <summary>One currency chip on a character row: the game's icon and the balance in Korean units ("3억 2,945만"),
/// the exact figure in the tooltip.</summary>
public sealed class CurrencyChipViewModel
{
    public CurrencyChipViewModel(CurrencyCell cell, CurrencyCell? characterStorage = null)
    {
        IconSource = "pack://application:,,,/WaffleMeter.App.Wpf;component/Icons/" + cell.Currency.IconFile;
        ValueText = cell.Count is { } count ? CurrencyFormat.Compact(count) : "—";

        // A balance never stated reads as a dash, not a zero — the character may well hold some; the meter has
        // simply not seen this character's items since it started watching.
        Opacity = cell.Known ? 1.0 : 0.45;

        string body = cell.Count is { } exact
            ? $"{cell.Currency.Name} {CurrencyFormat.Exact(exact)}{Observed(cell.ObservedAtMs)}"
            : $"{cell.Currency.Name} · 기록 없음\n(이 캐릭터로 접속하면 실제 값으로 채워집니다)";

        // 캐릭터 창고 kinah rides the tradeable kinah chip rather than taking a chip of its own — it is empty for
        // nearly everyone, and the row has no room for a permanent zero.
        if (characterStorage is { Count: long stored and > 0 })
        {
            body += $"\n캐릭터 창고 {CurrencyFormat.Exact(stored)}";
        }

        ToolTip = body;
    }

    public string IconSource { get; }
    public string ValueText { get; }
    public double Opacity { get; }
    public string ToolTip { get; }

    /// <summary>" (10-07 00:01 기준)" — when the server last stated this balance. Every row but the current one is
    /// a memory, so the age is part of the answer.</summary>
    internal static string Observed(long observedAtMs)
    {
        if (observedAtMs <= 0
            || observedAtMs < DateTimeOffset.MinValue.ToUnixTimeMilliseconds()
            || observedAtMs > DateTimeOffset.MaxValue.ToUnixTimeMilliseconds())
        {
            return string.Empty; // a hand-edited settings file must not throw the panel down
        }

        return DateTimeOffset.FromUnixTimeMilliseconds(observedAtMs).ToLocalTime()
            .ToString(" (MM-dd HH:mm '기준')", CultureInfo.InvariantCulture);
    }
}

/// <summary>One 총 키나 line: a server's characters' own kinah plus its 서버 창고, counted once.</summary>
public sealed class ServerKinaViewModel
{
    public ServerKinaViewModel(ServerKinaLine line)
    {
        ServerText = line.ServerLabel.Length > 0
            ? line.ServerLabel
            : line.Server.ToString(CultureInfo.InvariantCulture);
        TotalText = CurrencyFormat.Compact(line.Total);
        StorageText = line.ServerStorage is { } storage ? "서버 창고 " + CurrencyFormat.Compact(storage) : "서버 창고 —";

        long characters = line.Characters.Sum(c => c.Kina);
        var tip = new System.Text.StringBuilder();
        tip.Append(ServerText).Append(" 총 키나 ").Append(CurrencyFormat.Exact(line.Total));
        if (line.IsLowerBound)
        {
            tip.Append(" 이상"); // an unknown balance counts as nothing, so the real total can only be higher
        }

        int partial = 0;
        foreach (CharacterKina c in line.Characters)
        {
            tip.Append("\n  ").Append(c.Label).Append(' ').Append(CurrencyFormat.Exact(c.Kina));
            if (c.Partial)
            {
                // Half-known (a meter started mid-session learns balances one change at a time): the number is
                // what is known of this character, not all it holds.
                tip.Append(" (일부만 기록)");
                partial++;
            }

            tip.Append(CurrencyChipViewModel.Observed(c.ObservedAtMs));
        }

        tip.Append("\n  서버 창고 ")
           .Append(line.ServerStorage is { } s
               ? CurrencyFormat.Exact(s) + CurrencyChipViewModel.Observed(line.ServerStorageObservedAtMs)
               : "기록 없음");
        tip.Append("\n= 캐릭터 키나 ").Append(CurrencyFormat.Exact(characters))
           .Append(" + 서버 창고 ").Append(CurrencyFormat.Exact(line.ServerStorage ?? 0));
        tip.Append("\n\n캐릭터마다 키나(각인)·키나·캐릭터 창고 키나를 더하고, 서버 창고는 서버당 한 번만 더합니다.");
        if (line.CharactersWithoutRecord > 0)
        {
            tip.Append("\n재화 기록이 없는 캐릭터 ").Append(line.CharactersWithoutRecord)
               .Append("명은 빠져 있습니다 — 그 캐릭터로 접속하면 채워집니다.");
        }

        if (partial > 0)
        {
            tip.Append("\n키나가 일부만 기록된 캐릭터 ").Append(partial)
               .Append("명은 기록된 만큼만 더했습니다 — 그 캐릭터로 접속하면 채워집니다.");
        }

        ToolTip = tip.ToString();
    }

    public string ServerText { get; }
    public string TotalText { get; }
    public string StorageText { get; }
    public string ToolTip { get; }
}

/// <summary>One character row in the 컨텐츠 관리 목록.</summary>
public sealed class AetherRowViewModel
{
    public AetherRowViewModel(AetherRosterRow row)
    {
        IdentityHash = row.IdentityHash;

        // 재화 한 줄. Drawn only when something is on file for this character; the 캐릭터 창고 balance is folded
        // into the tradeable kinah chip's tooltip rather than shown as a chip of its own.
        CurrencyCell? characterStorage = row.CurrencyCells
            .Where(c => c.Currency.Slug == CurrencyCatalog.CharacterStorageKina)
            .Select(c => (CurrencyCell?)c)
            .FirstOrDefault();
        Currencies = CurrencyCatalog.ChipSlugs
            .SelectMany(slug => row.CurrencyCells.Where(c => c.Currency.Slug == slug))
            .Select(c => new CurrencyChipViewModel(
                c, c.Currency.Slug == CurrencyCatalog.Kina ? characterStorage : null))
            .ToList();
        CurrenciesVisibility = row.CurrenciesKnown ? Visibility.Visible : Visibility.Collapsed;

        Weekly = row.WeeklyCells
            .Select(c => new WeeklyContentCellViewModel(row.IdentityHash, c))
            .ToList();
        Corridors = row.CorridorCells.Select(c => new AbyssCorridorCellViewModel(c)).ToList();

        // Three states, and the empty two are NOT the same. With the 점령 현황 broadcast on file the emptiness
        // is a fact about the abyss and may be stated as one; with only this character's login snapshot it is a
        // fact about our records, because a snapshot full of zeros also comes back from a character that has
        // not been to the abyss since the 점령전. Saying the wrong one tells the user their 진영 lost artifacts
        // it still holds.
        CorridorsVisibility = Corridors.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        CorridorsEmptyVisibility =
            Corridors.Count == 0 && row.CorridorsKnown ? Visibility.Visible : Visibility.Collapsed;
        CorridorsEmptyText = row.CorridorsConfirmed ? "점령한 어비스 회랑 없음" : "어비스 회랑 기록 없음";
        CorridorsEmptyToolTip = row.CorridorsConfirmed
            ? "이번 점령 주기에 이 캐릭터의 진영이 점령한 아티팩트가 없습니다.\n다음 점령전에서 아티팩트를 점령하면 회랑이 열립니다."
            : "아직 이 서버의 점령 현황을 받지 못했습니다.\n미터를 켜 둔 상태로 어비스에 한 번 들어가시면 점령한 회랑과 남은 이용 시간이 표시됩니다.\n같은 서버의 다른 캐릭터가 받아 온 현황도 함께 쓰입니다.";
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
    public IReadOnlyList<CurrencyChipViewModel> Currencies { get; }
    public Visibility CurrenciesVisibility { get; }
    public IReadOnlyList<WeeklyContentCellViewModel> Weekly { get; }
    public IReadOnlyList<AbyssCorridorCellViewModel> Corridors { get; }
    public Visibility CorridorsVisibility { get; }
    public Visibility CorridorsEmptyVisibility { get; }
    public string CorridorsEmptyText { get; }
    public string CorridorsEmptyToolTip { get; }
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

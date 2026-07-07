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
/// list from the live DPS report, mirroring the React MeterList/MeterRow: sort by metric desc, top 10,
/// always append self, per-row progress ratio + fill color by contribution tier, masked name + power
/// badge. Row clicks raise <see cref="SelectionToggled"/> (App opens/closes the detail window).
/// </summary>
public sealed class OverlayViewModel : INotifyPropertyChanged
{
    private readonly MeterSettings _settings;
    private readonly MeterColorTheme _theme;
    private readonly Func<bool> _isLight;

    // Rebuilt from the theme whenever a color changes (MeterColorTheme.Changed); rows are records that
    // bake in the brush references, so a theme change re-runs Update on the last report.
    private Brush _userBar = null!, _normalBar = null!, _warningBar = null!, _errorBar = null!;
    private Dictionary<JobClass, Brush> _jobBars = new(); // per-job bar brushes (직업 강조 mode), rebuilt on theme change
    private Brush _nameDefault = null!, _nameServerA = null!, _nameServerB = null!;
    private Brush _amountBrush = null!, _dpsBrush = null!, _percentBrush = null!;
    private DpsReport? _lastReport;

    // React isLightOverlay hardcoded stat colors — used when the active skin is "light" so values stay
    // readable on the light background (the user theme colors are tuned for dark).
    private static readonly Brush LightName = Frozen(Color.FromRgb(0x1E, 0x29, 0x3B));
    private static readonly Brush LightServerA = Frozen(Color.FromRgb(0x03, 0x69, 0xA1));
    private static readonly Brush LightServerB = Frozen(Color.FromRgb(0xA2, 0x1C, 0xAF));
    private static readonly Brush LightPower = Frozen(Color.FromRgb(0x8A, 0x5A, 0x00));
    private static readonly Brush LightDps = Frozen(Color.FromRgb(0x10, 0x20, 0x33));
    private static readonly Brush LightPercent = Frozen(Color.FromRgb(0x04, 0x78, 0x57));
    private static readonly Brush LightCombatTime = Frozen(Color.FromRgb(0x33, 0x41, 0x55));

    public OverlayViewModel(string version, MeterSettings settings, MeterColorTheme theme, Func<bool>? isLight = null)
    {
        _settings = settings;
        Settings = settings;
        _theme = theme;
        Theme = theme;
        _isLight = isLight ?? (() => false);
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

    /// <summary>Re-theme on a skin swap (light/dark stat colors) and repaint.</summary>
    public void RefreshSkin()
    {
        RebuildBrushes();
        if (_lastReport is { } report)
        {
            Update(report);
        }
    }

    /// <summary>Exposed for the overlay to bind opacity/font/etc. directly.</summary>
    public MeterSettings Settings { get; }

    /// <summary>Exposed so XAML can bind theme-driven chrome (e.g. combat-time color) directly.</summary>
    public MeterColorTheme Theme { get; }

    private Brush _combatTimeBrush = null!;
    public Brush CombatTimeBrush { get => _combatTimeBrush; private set => Set(ref _combatTimeBrush, value); }

    private Brush _bossBarBrush = null!;
    /// <summary>Target-info accent rail + HP fill gradient (React theme.bossBar).</summary>
    public Brush BossBarBrush { get => _bossBarBrush; private set => Set(ref _bossBarBrush, value); }

    // In-combat accent (React active dot/label #2dd4bf); standby reuses CombatTimeBrush.
    private static readonly Brush CombatActiveBrush = Frozen(Color.FromRgb(0x2D, 0xD4, 0xBF));

    private Brush _combatStatusBrush = CombatActiveBrush;
    public Brush CombatStatusBrush { get => _combatStatusBrush; private set => Set(ref _combatStatusBrush, value); }

    /// <summary>Boss icon for the target-info bar (bundled).</summary>
    public System.Windows.Media.ImageSource? BossIcon => JoinIcons.BossIcon;

    private void RebuildBrushes()
    {
        _userBar = ThemeGradient(_theme.UserBarFrom, _theme.UserBarTo);
        _normalBar = ThemeGradient(_theme.NormalBarFrom, _theme.NormalBarTo);
        _warningBar = ThemeGradient(_theme.WarningBarFrom, _theme.WarningBarTo);
        _errorBar = ThemeGradient(_theme.ErrorBarFrom, _theme.ErrorBarTo);
        _jobBars = new Dictionary<JobClass, Brush>();
        foreach (JobClass jc in Enum.GetValues<JobClass>())
        {
            _jobBars[jc] = ThemeSolid(_theme.JobBar(jc));
        }

        if (_isLight())
        {
            // Light skin: the user theme's stat colors (tuned for dark) are unreadable on the light bg,
            // so use React's isLightOverlay hardcodes.
            _nameDefault = LightName;
            _nameServerA = LightServerA;
            _nameServerB = LightServerB;
            _amountBrush = LightPower;
            _dpsBrush = LightDps;
            _percentBrush = LightPercent;
            CombatTimeBrush = LightCombatTime;
        }
        else
        {
            _nameDefault = ThemeSolid(_theme.ServerDefaultColor);
            _nameServerA = ThemeSolid(_theme.ServerAColor);
            _nameServerB = ThemeSolid(_theme.ServerBColor);
            _amountBrush = ThemeSolid(_theme.MeterStatAmount);  // power badge (React MeterRow.tsx:207)
            _dpsBrush = ThemeSolid(_theme.MeterStatDps);        // damage value (MeterRow.tsx:126)
            _percentBrush = ThemeSolid(_theme.MeterStatPercent); // percent (MeterRow.tsx:119/127)
            CombatTimeBrush = ThemeSolid(_theme.CombatTimeColor);
        }

        BossBarBrush = ThemeGradient(_theme.BossBarFrom, _theme.BossBarTo);
    }

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    public ObservableCollection<RowViewModel> Rows { get; } = new();

    /// <summary>Raised when a row is clicked (App toggles the detail window for that uid).</summary>
    public event Action<int>? SelectionToggled;

    public void ToggleSelection(int uid) => SelectionToggled?.Invoke(uid);

    private string _targetName = "-";
    public string TargetName { get => _targetName; private set => Set(ref _targetName, value); }

    private string _targetHpText = string.Empty;
    public string TargetHpText { get => _targetHpText; private set => Set(ref _targetHpText, value); }

    private string _duration = "0.0s";
    public string Duration { get => _duration; private set => Set(ref _duration, value); }

    private string _status;
    public string Status { get => _status; set => Set(ref _status, value); }

    private string _recognizedStatus = string.Empty;
    /// <summary>"· 콘팡 인식됨" shown beside the capture status once the own character is detected.</summary>
    public string RecognizedStatus { get => _recognizedStatus; private set => Set(ref _recognizedStatus, value); }

    private Visibility _recognizedVisibility = Visibility.Collapsed;
    public Visibility RecognizedVisibility { get => _recognizedVisibility; private set => Set(ref _recognizedVisibility, value); }

    // The recognized 본인 (executor) uid, mirrored LIVE from StatsBuilder.OwnCharacter().Id. The fallback
    // self signal for row coloring, used only when the report being shown carries no frozen executor id
    // (i.e. a live/in-progress report — see DpsReport.ExecutorId). A saved/history report self-identifies
    // its own player via report.ExecutorId, so it no longer depends on this transient live value (which the
    // history-replay path never refreshes). 0 = not recognized.
    private int _selfId;

    // The recognized executor's known identity (nickname/server/job/power), forwarded to OverlayRowBuilder for
    // lost-executor recovery: if 본인 re-instances and the new id's own-load packet never arrives, this names
    // and self-colors the bare row that would otherwise be hidden by the blank-row filter.
    private string? _selfNickname;
    private int _selfServer;
    private JobClass? _selfJob;
    private int _selfPower;

    private IReadOnlyList<User> _roster = [];

    /// <summary>App supplies the pre-combat party roster (recently-seen known players) each tick. It is
    /// merged as idle (0-DPS) rows in <see cref="Update"/> ONLY while there is no live combat data and the
    /// report is live (not a saved/history replay), so the combat row set, sort, and self-index are never
    /// touched once damage starts. Persisted in a field so it survives theme repaints (which re-run Update
    /// against the last report).</summary>
    public void SetRoster(IReadOnlyList<User> roster) => _roster = roster;

    /// <summary>App calls this each tick from StatsBuilder.OwnCharacter() so the indicator appears the
    /// moment the own character is recognized (and names it when known). <paramref name="selfId"/> is the
    /// recognized 본인 uid, used to keep the self row on the "내 캐릭터" color in 직업 강조 mode.</summary>
    public void SetRecognized(bool detected, string? nickname, int selfId = 0, int server = 0, JobClass? job = null, int power = 0)
    {
        _selfId = detected ? selfId : 0;
        _selfNickname = detected ? nickname : null;
        _selfServer = detected ? server : 0;
        _selfJob = detected ? job : null;
        _selfPower = detected ? power : 0;
        RecognizedVisibility = detected ? Visibility.Visible : Visibility.Collapsed;
        RecognizedStatus = !detected
            ? string.Empty
            : string.IsNullOrWhiteSpace(nickname) ? "· 캐릭터 인식됨" : $"· {nickname} 인식됨";
    }

    private Visibility _clickThroughVisibility = Visibility.Collapsed;
    /// <summary>Header lock badge: visible while click-through (input pass-through) is active, so the user
    /// can see the overlay is letting clicks fall through to the game (React Header LockKeyhole badge).</summary>
    public Visibility ClickThroughVisibility { get => _clickThroughVisibility; private set => Set(ref _clickThroughVisibility, value); }

    /// <summary>Driven by the overlay window whenever click-through toggles (hotkey, or taskbar-mode reset
    /// clearing it) so the header lock badge always reflects the real pass-through state.</summary>
    public void SetClickThroughIndicator(bool on) => ClickThroughVisibility = on ? Visibility.Visible : Visibility.Collapsed;

    private Visibility _updateReadyVisibility = Visibility.Collapsed;
    /// <summary>Header "update available" badge: shown once an update is downloaded and ready. Clicking it
    /// opens the restart toast on demand — there is no auto-popup, so the user updates when they choose.</summary>
    public Visibility UpdateReadyVisibility { get => _updateReadyVisibility; private set => Set(ref _updateReadyVisibility, value); }

    private string _updateTooltip = "업데이트";
    public string UpdateTooltip { get => _updateTooltip; private set => Set(ref _updateTooltip, value); }

    /// <summary>App calls this when an update has finished downloading; reveals the header update badge.</summary>
    public void SetUpdateReady(string version)
    {
        UpdateTooltip = $"업데이트 {version} — 클릭하면 적용(재시작)";
        UpdateReadyVisibility = Visibility.Visible;
    }

    private Visibility _placeholderVisibility = Visibility.Visible;
    public Visibility PlaceholderVisibility { get => _placeholderVisibility; private set => Set(ref _placeholderVisibility, value); }

    // ---- target-info bar (above the list) + combat-timer pill (below the list) ----
    private double _targetHpRatio;
    public double TargetHpRatio { get => _targetHpRatio; private set => Set(ref _targetHpRatio, value); }

    private double _targetHpRest = 1.0;
    public double TargetHpRest { get => _targetHpRest; private set => Set(ref _targetHpRest, value); }

    private Visibility _targetInfoVisibility = Visibility.Collapsed;
    public Visibility TargetInfoVisibility { get => _targetInfoVisibility; private set => Set(ref _targetInfoVisibility, value); }

    private Visibility _targetFailedVisibility = Visibility.Collapsed;
    public Visibility TargetFailedVisibility { get => _targetFailedVisibility; private set => Set(ref _targetFailedVisibility, value); }

    private Visibility _targetHpVisibility = Visibility.Collapsed;
    public Visibility TargetHpVisibility { get => _targetHpVisibility; private set => Set(ref _targetHpVisibility, value); }

    // Boss HP gauge form, driven by the same 게이지 형태 (BarStyle) setting as the meter rows: "fill" paints a
    // proportional cell fill behind the boss name, "bar" keeps the thin bottom HP bar, "none" hides both.
    private Visibility _targetBarFillVisibility = Visibility.Visible;
    /// <summary>Boss HP gauge as a proportional cell fill (게이지 형태 = "fill"), mirroring the meter rows.</summary>
    public Visibility TargetBarFillVisibility { get => _targetBarFillVisibility; private set => Set(ref _targetBarFillVisibility, value); }

    private Visibility _targetBottomBarVisibility = Visibility.Collapsed;
    /// <summary>Boss HP gauge as a thin bottom bar (게이지 형태 = "bar").</summary>
    public Visibility TargetBottomBarVisibility { get => _targetBottomBarVisibility; private set => Set(ref _targetBottomBarVisibility, value); }

    private Visibility _combatTimerVisibility = Visibility.Collapsed;
    public Visibility CombatTimerVisibility { get => _combatTimerVisibility; private set => Set(ref _combatTimerVisibility, value); }

    private string _combatStatusText = "대기 중";
    public string CombatStatusText { get => _combatStatusText; private set => Set(ref _combatStatusText, value); }

    /// <summary>The report the meter is CURRENTLY displaying — the live report, or a saved battle while
    /// replaying from history. The clickable rows are built from this exact report (<see cref="Update"/>
    /// keys rows by its Information), so the detail window must resolve a clicked uid against THIS report,
    /// not the app's live <c>_lastReport</c> (resolving against the live report while a saved battle is on
    /// screen is what produced the raw-uid title + all-zero breakdown).</summary>
    public DpsReport? CurrentReport => _lastReport;

    public void Update(DpsReport report)
    {
        _lastReport = report;
        string? mobName = report.Target?.Mob.Name;
        bool hasTarget = !string.IsNullOrEmpty(mobName);
        TargetName = hasTarget ? mobName! : "타겟 인식 실패";
        TargetFailedVisibility = hasTarget ? Visibility.Collapsed : Visibility.Visible;
        if (report.Target is { MaxHp: > 0 } tgt)
        {
            double ratio = Math.Clamp((double)tgt.RemainHp / tgt.MaxHp, 0, 1);
            double pct = ratio * 100.0;
            TargetHpRatio = ratio;
            TargetHpRest = 1.0 - ratio;
            TargetHpText = FormatTargetHp(tgt.RemainHp, tgt.MaxHp, pct, _settings.TargetInfoDisplayMode);
            TargetHpVisibility = hasTarget ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            TargetHpRatio = 0;
            TargetHpRest = 1.0;
            TargetHpText = string.Empty;
            TargetHpVisibility = Visibility.Collapsed;
        }

        long durationMs = Math.Max(report.BattleEnd - report.BattleStart, 0);
        Duration = FormatDuration(durationMs);
        // In combat = activity within the last ~1.5s (mirrors React isInCombat + 1s debounce).
        bool inCombat = report.Information.Count > 0
            && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - report.BattleEnd < 1500;
        CombatStatusText = inCombat ? "전투 중" : "대기 중";
        CombatStatusBrush = inCombat ? CombatActiveBrush : CombatTimeBrush;

        // metric = amount (total mode) or dps. Sort desc, take 10 (max raid = 5+5), always include self.
        bool total = _settings.UseTotalDamage;
        bool entire = _settings.UseEntireContribution;
        NameDisplay nameMode = _settings.NameDisplayMode;
        double rowHeight = _settings.RowHeight;
        string barStyle = _settings.BarStyle; // "fill" (cell fill) / "bar" (thin bottom bar) / "none"
        Visibility fillVis = barStyle == "fill" ? Visibility.Visible : Visibility.Collapsed;
        Visibility barVis = barStyle == "bar" ? Visibility.Visible : Visibility.Collapsed;
        // The boss HP gauge follows the same 게이지 형태 choice as the rows (fill / thin bar / none).
        TargetBarFillVisibility = fillVis;
        TargetBottomBarVisibility = barVis;
        double Metric(DpsInformation info) => total ? info.Amount : info.Dps;

        // Row selection lives in the pure OverlayRowBuilder (App.Core, unit-tested): it drops bare/no-nickname
        // combat rows (a mid-join provisional actor that would otherwise show as a blank "broken" line — its DPS
        // still accumulates and the row appears once identity arrives), surfaces the pre-combat party roster
        // (incl. the App-injected self) only on the fresh report, always includes self, and reports whether any
        // DISPLAYABLE combat row exists via hasCombatRows (so an all-bare mid-join can't render a boss bar/timer
        // over the placeholder). Self-coloring uses the frozen report.ExecutorId, else the live _selfId.
        IReadOnlyList<OverlayRowBuilder.Row> display = OverlayRowBuilder.Build(
            report, _roster, _selfId, total, _settings.ShowPreCombatRoster, out bool hasCombatRows,
            topN: _settings.EffectiveMaxVisibleRows,
            selfNickname: _selfNickname, selfServer: _selfServer, selfJob: _selfJob, selfPower: _selfPower);

        double topMetric = Math.Max(display.Count > 0 ? display.Max(e => Metric(e.Info)) : 0.0, 1.0);

        for (int i = 0; i < display.Count; i++)
        {
            OverlayRowBuilder.Row e = display[i];
            bool isUser = e.IsSelf;
            double contribution = e.Info.Contribution; // fill tier always uses party contribution
            int power = e.User?.Power ?? 0;
            int server = e.User?.Server ?? 0;
            // Restored bracketed server tag (dropped in the WPF migration). A SEPARATE row element (not folded
            // into Name) so it survives name masking + the Name MaxWidth ellipsis; collapsed when off/unknown.
            string serverTag = _settings.ShowServerTag ? ServerNames.GetServerLabel(server) : string.Empty;
            double ratio = Math.Clamp(Metric(e.Info) / topMetric, 0.0, 1.0);
            double barRatio = ratio > 0 ? Math.Max(0.015, ratio) : 0.0; // React max(1.5%, ratio) so small bars stay visible
            string? jobName = e.User?.Job is JobClass jc ? jc.ClassName() : null;
            // 직업 강조 mode: this player's job bar (self keeps _userBar; unresolved job -> _normalBar).
            Brush jobBar = e.User?.Job is JobClass jcb && _jobBars.TryGetValue(jcb, out Brush? jbr) ? jbr : _normalBar;

            string displayName = MeterFormat.DisplayName(e.User?.Nickname, nameMode, isUser);
            var row = new RowViewModel(
                Id: e.Uid,
                Rank: i + 1,
                Name: displayName,
                NameFontFamily: GlyphFallback.ForName(_settings.FontFamily, displayName),
                ServerTag: serverTag,
                ServerTagVisibility: serverTag.Length == 0 ? Visibility.Collapsed : Visibility.Visible,
                PowerText: power > 0 ? MeterFormat.FormatPower(power) : string.Empty,
                PowerVisible: power > 0 ? Visibility.Visible : Visibility.Collapsed,
                DamageText: total ? MeterFormat.FormatAmount(e.Info.Amount) : MeterFormat.FormatDps(e.Info.Dps),
                PercentText: MeterFormat.FormatPercent(entire ? e.Info.EntireContribution : contribution),
                BarRatio: barRatio,
                BarRest: 1.0 - barRatio,
                FillBrush: _theme.BarColorMode == "job"
                    ? (isUser ? _userBar : jobBar)
                    : (isUser ? _userBar : contribution < 3 ? _errorBar : contribution < 5 ? _warningBar : _normalBar),
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
                RowHeight: rowHeight,
                IconSource: JoinIcons.Job(jobName),
                AccentOpacity: isUser ? 0.95 : 0.82,
                BarFillVisibility: fillVis,
                BottomBarVisibility: barVis);

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
        // React TargetInfo/CombatTimer show when players>0; compact mode can hide each (with overrides).
        // hasCombatRows is the count of DISPLAYABLE (named) combat rows from OverlayRowBuilder above — NOT
        // report.Information.Count — so the pre-combat idle roster (and an all-bare mid-join) never shows a
        // "타겟 인식 실패" bar or a 00:00 timer before a nameable fight is on screen.
        bool minimal = _settings.IsMinimal;
        TargetInfoVisibility = hasCombatRows && (!minimal || _settings.ShowTargetInfoInMinimal) ? Visibility.Visible : Visibility.Collapsed;
        CombatTimerVisibility = durationMs > 0 && hasCombatRows && (!minimal || _settings.ShowCombatTimerInMinimal)
            ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Combat-time readout: under a minute keeps "12.3s"; from a minute up it converts to
    /// "6m 32s" (whole seconds, decimals truncated) so long fights read cleanly.</summary>
    private static string FormatDuration(long ms)
    {
        long totalSeconds = ms / 1000;
        if (totalSeconds < 60)
        {
            return $"{ms / 1000.0:F1}s";
        }

        return $"{totalSeconds / 60}m {totalSeconds % 60}s";
    }

    /// <summary>Boss HP readout per React targetInfoDisplayMode.</summary>
    private static string FormatTargetHp(int remain, int max, double pct, string mode) => mode switch
    {
        "percent" => $"{pct:F1}%",
        "remain_percent" => $"{MeterFormat.FormatAmount(remain)}  {pct:F1}%",
        "remain_full_percent" => $"{remain:N0}  {pct:F1}%",
        "hp_percent" => $"{MeterFormat.FormatAmount(remain)} / {MeterFormat.FormatAmount(max)}  {pct:F1}%",
        _ => $"{remain:N0} / {max:N0}  {pct:F1}%", // hp_full_percent
    };

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
    System.Windows.Media.FontFamily NameFontFamily,
    string ServerTag,
    Visibility ServerTagVisibility,
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
    double RowHeight,
    System.Windows.Media.ImageSource? IconSource,
    double AccentOpacity,
    Visibility BarFillVisibility,
    Visibility BottomBarVisibility);

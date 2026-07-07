using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using WaffleMeter.App.Core;
using WaffleMeter.Capture;
using WaffleMeter.Capture.Live;
using WaffleMeter.Stats;

namespace WaffleMeter.App.Wpf;

/// <summary>A label/value choice for a settings ComboBox.</summary>
public sealed record SettingOption(string Label, string Value);

/// <summary>One row of the per-character consent management list (immutable; the collection is rebuilt on
/// change). The public toggle binds <c>IsPublic</c> one-way and routes the change through a Click handler.</summary>
public sealed class ConsentCharacterRow
{
    public string IdentityHash { get; init; } = "";
    public string Label { get; init; } = "";
    public string SubLabel { get; init; } = "";
    public bool IsPublic { get; init; }
    public bool CanSetPublic { get; init; }
    public bool CanRevoke { get; init; }
    public string PublicToggleTooltip { get; init; } = "";
    public Visibility CurrentBadgeVisibility { get; init; }
}

/// <summary>One row of the custom-alarm list (immutable; the collection is rebuilt on change). The enable
/// toggle binds one-way and routes the change through a Click handler.</summary>
public sealed class CustomAlarmRow
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string TimeText { get; init; } = "";
    public string DaysText { get; init; } = "";
    public bool Enabled { get; init; }
}

/// <summary>
/// Backs the tabbed settings window. Display/overlay settings apply live via <see cref="MeterSettings"/>
/// (the overlay reads them each tick); hotkeys are buffered and committed on Save; Cancel reverts the
/// live-applied settings from a snapshot. Stats consent + server config call the services directly.
/// </summary>
public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly MeterServices _services;
    private readonly MeterSettings _settings;
    private readonly SkinManager _skin;
    private readonly OverlayController _controller;
    private readonly HotkeyHandler _hotkeys;
    private readonly Snapshot _snapshot;

    public SettingsViewModel(MeterServices services, MeterSettings settings, MeterColorTheme theme, SkinManager skin, OverlayController controller, HotkeyHandler hotkeys)
    {
        _services = services;
        _settings = settings;
        Theme = theme;
        _skin = skin;
        _controller = controller;
        _hotkeys = hotkeys;
        _snapshot = Snapshot.Capture(settings, controller);

        _pendingReset = hotkeys.Reset;
        _pendingVisibility = hotkeys.Visibility;
        _pendingClickThrough = hotkeys.ClickThrough;

        Reload();
    }

    // ---- option lists (React SettingsPanel) ----
    public IReadOnlyList<SettingOption> DisplayModes { get; } = new[]
    {
        new SettingOption("DPS · 퍼센트", "dps_percent"),
        new SettingOption("누적 · DPS · 퍼센트", "amount_dps_percent"),
        new SettingOption("누적 · 퍼센트", "amount_percent"),
        new SettingOption("누적(전체) · DPS · 퍼센트", "amount_full_dps_percent"),
        new SettingOption("누적(전체) · 퍼센트", "amount_full_percent"),
    };

    public IReadOnlyList<SettingOption> DamageValueModes { get; } = new[]
    {
        new SettingOption("DPS", "dps"),
        new SettingOption("누적 피해량", "total"),
    };

    public IReadOnlyList<SettingOption> ContributionModes { get; } = new[]
    {
        new SettingOption("파티 기여도", "contribution"),
        new SettingOption("보스 체력 기여도", "entireContribution"),
    };

    public IReadOnlyList<SettingOption> NameDisplays { get; } = new[]
    {
        new SettingOption("모두 표기", "all"),
        new SettingOption("나만 표기 (방송용 익명)", "me_only"),
        new SettingOption("모두 숨김", "hidden"),
    };

    public IReadOnlyList<SettingOption> TargetInfoDisplayModes { get; } = new[]
    {
        new SettingOption("남은/최대 · 퍼센트", "hp_full_percent"),
        new SettingOption("남은/최대(축약) · 퍼센트", "hp_percent"),
        new SettingOption("남은 체력 · 퍼센트", "remain_full_percent"),
        new SettingOption("남은 체력(축약) · 퍼센트", "remain_percent"),
        new SettingOption("퍼센트만", "percent"),
    };

    public IReadOnlyList<SettingOption> BarStyles { get; } = new[]
    {
        new SettingOption("칸 채우기 (두꺼운 게이지)", "fill"),
        new SettingOption("얇은 바", "bar"),
        new SettingOption("표시 안 함", "none"),
    };

    public IReadOnlyList<SettingOption> BarColorModes { get; } = new[]
    {
        new SettingOption("본인 강조", "self"),
        new SettingOption("직업 강조", "job"),
    };

    // Bundled-or-fallback fonts (see Fonts/README.md). Each family ships a regular + a bolder weight, and
    // four families add an even heavier "(EX)" extra-bold. Each Value is the name FontFamilyConverter feeds
    // to WPF as ./Fonts/#<value>, which resolves to that exact weight's typeface — WPF matches it against the
    // font's Win32 family name (e.g. "NEXON Lv2 Gothic Bold") or its family+face (e.g. "Pretendard Bold",
    // whose Win32 family is the shared "Pretendard") — so the weight needs no separate FontWeight plumbing.
    // (EX) values verified per file via GlyphTypeface resolution. Malgun Gothic is always available (fallback).
    public IReadOnlyList<SettingOption> FontFamilies { get; } = new[]
    {
        new SettingOption("NEXON Lv2 Gothic (Bold, 기본)", "NEXON Lv2 Gothic Medium"),
        new SettingOption("NEXON Lv2 Gothic (EX)", "NEXON Lv2 Gothic Bold"),
        new SettingOption("NEXON Lv2 Gothic", "NEXON Lv2 Gothic"),
        new SettingOption("Pretendard (Bold)", "Pretendard SemiBold"),
        new SettingOption("Pretendard (EX)", "Pretendard Bold"),
        new SettingOption("Pretendard", "Pretendard"),
        new SettingOption("Spoqa Han Sans Neo (Bold)", "Spoqa Han Sans Neo Medium"),
        new SettingOption("Spoqa Han Sans Neo (EX)", "Spoqa Han Sans Neo Bold"),
        new SettingOption("Spoqa Han Sans Neo", "Spoqa Han Sans Neo"),
        new SettingOption("Freesentation (Bold)", "Freesentation 6 SemiBold"),
        new SettingOption("Freesentation (EX)", "Freesentation 7 Bold"),
        new SettingOption("Freesentation", "Freesentation"),
        new SettingOption("Tmoney Round Wind (Bold)", "Tmoney RoundWind ExtraBold"),
        new SettingOption("Tmoney Round Wind", "Tmoney RoundWind"),
        new SettingOption("맑은 고딕", "Malgun Gothic"),
    };

    // ---- display tab (live) ----
    public string DisplayMode { get => _settings.DisplayMode; set { _settings.DisplayMode = value; OnPropertyChanged(); } }
    public string DamageValueMode { get => _settings.DamageValueMode; set { _settings.DamageValueMode = value; OnPropertyChanged(); } }
    public string ContributionMode { get => _settings.ContributionMode; set { _settings.ContributionMode = value; OnPropertyChanged(); } }
    public string NameDisplay { get => _settings.NameDisplay; set { _settings.NameDisplay = value; OnPropertyChanged(); } }
    public string FontFamily { get => _settings.FontFamily; set { _settings.FontFamily = value; OnPropertyChanged(); } }
    public int RowHeight { get => _settings.RowHeight; set { _settings.RowHeight = value; OnPropertyChanged(); } }
    public int RefreshIntervalMs { get => _settings.RefreshIntervalMs; set { _settings.RefreshIntervalMs = value; OnPropertyChanged(); } }
    public int MaxVisibleRows { get => _settings.MaxVisibleRows; set { _settings.MaxVisibleRows = value; OnPropertyChanged(); } }
    public bool LowSpecMode
    {
        get => _settings.LowSpecMode;
        set { _settings.LowSpecMode = value; OnPropertyChanged(); OnPropertyChanged(nameof(RefreshSliderEnabled)); }
    }

    /// <summary>The refresh-interval slider is disabled while low-spec mode pins the interval.</summary>
    public bool RefreshSliderEnabled => !_settings.LowSpecMode;
    public string TargetInfoDisplayMode { get => _settings.TargetInfoDisplayMode; set { _settings.TargetInfoDisplayMode = value; OnPropertyChanged(); } }
    public string BarStyle { get => _settings.BarStyle; set { _settings.BarStyle = value; OnPropertyChanged(); } }
    public bool IsMinimal { get => _settings.IsMinimal; set { _settings.IsMinimal = value; OnPropertyChanged(); } }
    public bool ShowCombatTimerInMinimal { get => _settings.ShowCombatTimerInMinimal; set { _settings.ShowCombatTimerInMinimal = value; OnPropertyChanged(); } }
    public bool ShowTargetInfoInMinimal { get => _settings.ShowTargetInfoInMinimal; set { _settings.ShowTargetInfoInMinimal = value; OnPropertyChanged(); } }
    public bool ShowServerTag { get => _settings.ShowServerTag; set { _settings.ShowServerTag = value; OnPropertyChanged(); } }
    public bool ShowAetherStatus { get => _settings.ShowAetherStatus; set { _settings.ShowAetherStatus = value; OnPropertyChanged(); } }
    public bool VrrCompatMode { get => _settings.VrrCompatMode; set { _settings.VrrCompatMode = value; OnPropertyChanged(); } }

    /// <summary>Wired by App: trigger an update check (results surface in the toast).</summary>
    public Action? CheckUpdateRequested { get; set; }
    public void CheckForUpdate() => CheckUpdateRequested?.Invoke();

    /// <summary>Wired by App: reset a panel position ("meter" / "join" / "history").</summary>
    public Action<string>? ResetPositionRequested { get; set; }
    public void ResetMeterPosition() => ResetPositionRequested?.Invoke("meter");
    public void ResetJoinPosition() => ResetPositionRequested?.Invoke("join");
    public void ResetHistoryPosition() => ResetPositionRequested?.Invoke("history");

    // ---- overlay tab (live) ----
    public double MeterOpacity { get => _settings.MeterOpacity; set { _settings.MeterOpacity = value; OnPropertyChanged(); } }
    public bool MultiMonitorMode { get => _settings.MultiMonitorMode; set { _settings.MultiMonitorMode = value; OnPropertyChanged(); } }
    public bool ShowJoinPanel { get => _settings.ShowJoinPanel; set { _settings.ShowJoinPanel = value; OnPropertyChanged(); } }
    public bool ShowPreCombatRoster { get => _settings.ShowPreCombatRoster; set { _settings.ShowPreCombatRoster = value; OnPropertyChanged(); } }
    // (Light mode is now a skin — "light" in the Skin list — not a separate overlayTheme toggle.)

    // ---- alarms (live; persisted immediately, not part of the Cancel snapshot) ----
    public bool ShugoAlarmEnabled { get => _settings.ShugoAlarmEnabled; set { _settings.ShugoAlarmEnabled = value; OnPropertyChanged(); } }
    public bool ShugoLead10 { get => _settings.ShugoLead10; set { _settings.ShugoLead10 = value; OnPropertyChanged(); } }
    public bool ShugoLead5 { get => _settings.ShugoLead5; set { _settings.ShugoLead5 = value; OnPropertyChanged(); } }
    public bool ShugoLead1 { get => _settings.ShugoLead1; set { _settings.ShugoLead1 = value; OnPropertyChanged(); } }
    public bool ShugoLeadStart { get => _settings.ShugoLeadStart; set { _settings.ShugoLeadStart = value; OnPropertyChanged(); } }
    public bool AlarmSoundEnabled { get => _settings.AlarmSoundEnabled; set { _settings.AlarmSoundEnabled = value; OnPropertyChanged(); } }
    public double AlarmVolume { get => _settings.AlarmVolume; set { _settings.AlarmVolume = value; OnPropertyChanged(); } }

    /// <summary>Settings "소리 테스트" button: play the alarm chime at the current volume.</summary>
    public void TestAlarmSound() => AlarmSound.Play(_settings.AlarmVolume);

    // ---- custom alarms (CRUD list) ----
    public IReadOnlyList<int> Hours { get; } = Enumerable.Range(0, 24).ToList();
    public IReadOnlyList<int> Minutes { get; } = Enumerable.Range(0, 60).ToList();

    public ObservableCollection<CustomAlarmRow> CustomAlarmRows { get; } = new();
    public bool HasCustomAlarms => CustomAlarmRows.Count > 0;

    private string _newAlarmTitle = "알람";
    public string NewAlarmTitle { get => _newAlarmTitle; set => Set(ref _newAlarmTitle, value); }
    private int _newAlarmHour = 12;
    public int NewAlarmHour { get => _newAlarmHour; set => Set(ref _newAlarmHour, value); }
    private int _newAlarmMinute;
    public int NewAlarmMinute { get => _newAlarmMinute; set => Set(ref _newAlarmMinute, value); }

    private bool _daySun, _dayMon, _dayTue, _dayWed, _dayThu, _dayFri, _daySat;
    public bool DaySun { get => _daySun; set => Set(ref _daySun, value); }
    public bool DayMon { get => _dayMon; set => Set(ref _dayMon, value); }
    public bool DayTue { get => _dayTue; set => Set(ref _dayTue, value); }
    public bool DayWed { get => _dayWed; set => Set(ref _dayWed, value); }
    public bool DayThu { get => _dayThu; set => Set(ref _dayThu, value); }
    public bool DayFri { get => _dayFri; set => Set(ref _dayFri, value); }
    public bool DaySat { get => _daySat; set => Set(ref _daySat, value); }

    /// <summary>Rebuild the displayed alarm rows from settings (call on open + after each change).</summary>
    public void RefreshCustomAlarms()
    {
        CustomAlarmRows.Clear();
        foreach (CustomAlarm a in _settings.CustomAlarms)
        {
            CustomAlarmRows.Add(ToRow(a));
        }

        OnPropertyChanged(nameof(HasCustomAlarms));
    }

    public void AddCustomAlarm()
    {
        var days = new List<int>();
        if (_daySun) days.Add(0);
        if (_dayMon) days.Add(1);
        if (_dayTue) days.Add(2);
        if (_dayWed) days.Add(3);
        if (_dayThu) days.Add(4);
        if (_dayFri) days.Add(5);
        if (_daySat) days.Add(6);

        var alarm = new CustomAlarm
        {
            Id = Guid.NewGuid().ToString("N"),
            Enabled = true,
            Title = string.IsNullOrWhiteSpace(NewAlarmTitle) ? "알람" : NewAlarmTitle.Trim(),
            Hour = Math.Clamp(NewAlarmHour, 0, 23),
            Minute = Math.Clamp(NewAlarmMinute, 0, 59),
            Days = days,
        };
        _settings.CustomAlarms = _settings.CustomAlarms.Append(alarm).ToList();
        RefreshCustomAlarms();
    }

    public void DeleteCustomAlarm(string id)
    {
        _settings.CustomAlarms = _settings.CustomAlarms.Where(a => a.Id != id).ToList();
        RefreshCustomAlarms();
    }

    public void SetCustomAlarmEnabled(string id, bool on)
    {
        _settings.CustomAlarms = _settings.CustomAlarms
            .Select(a => a.Id == id ? a with { Enabled = on } : a)
            .ToList();
        RefreshCustomAlarms();
    }

    private static readonly string[] DayLabels = { "일", "월", "화", "수", "목", "금", "토" };

    private static CustomAlarmRow ToRow(CustomAlarm a) => new()
    {
        Id = a.Id,
        Title = a.Title,
        TimeText = $"{a.Hour:00}:{a.Minute:00}",
        DaysText = FormatDays(a.Days),
        Enabled = a.Enabled,
    };

    private static string FormatDays(IReadOnlyList<int> days)
    {
        if (days.Count is 0 or 7)
        {
            return "매일";
        }

        return string.Join("·", days.OrderBy(d => d).Where(d => d is >= 0 and <= 6).Select(d => DayLabels[d]));
    }

    public bool IsAutoHide
    {
        get => _controller.IsAutoHide;
        set { _controller.SetAutoHide(value); OnPropertyChanged(); }
    }

    /// <summary>Taskbar / alt-tab mode: the overlay becomes a normal window (shows in taskbar + alt-tab,
    /// auto-hide suspended). Applied live + persisted; the header also exposes this as a toggle.</summary>
    public bool TaskbarMode
    {
        get => _settings.TaskbarMode;
        set { _settings.TaskbarMode = value; _controller.SetTaskbarMode(value); OnPropertyChanged(); }
    }

    // ---- hotkey rebinding (buffered, committed on Save; null = 미지정/unassigned) ----
    private HotkeyCombo? _pendingReset;
    public HotkeyCombo? PendingReset { get => _pendingReset; set => Set(ref _pendingReset, value); }
    private HotkeyCombo? _pendingVisibility;
    public HotkeyCombo? PendingVisibility { get => _pendingVisibility; set => Set(ref _pendingVisibility, value); }
    private HotkeyCombo? _pendingClickThrough;
    public HotkeyCombo? PendingClickThrough { get => _pendingClickThrough; set => Set(ref _pendingClickThrough, value); }

    // ---- stats consent ----
    private bool _consentAccepted;
    public bool ConsentAccepted { get => _consentAccepted; set => Set(ref _consentAccepted, value); }
    private bool _uploadEnabled;
    public bool UploadEnabled { get => _uploadEnabled; set => Set(ref _uploadEnabled, value); }
    private bool _publicCharacter;
    public bool PublicCharacter { get => _publicCharacter; set => Set(ref _publicCharacter, value); }
    private bool _characterDetected;
    public bool CharacterDetected { get => _characterDetected; private set => Set(ref _characterDetected, value); }
    private string _consentStatus = string.Empty;
    public string ConsentStatus { get => _consentStatus; private set => Set(ref _consentStatus, value); }
    private string _consentNotice = string.Empty;
    /// <summary>Localized notice for the last consent action (e.g. a public transition refused for lack of
    /// ownership, rolled back to private). Empty when there is nothing to say.</summary>
    public string ConsentNotice
    {
        get => _consentNotice;
        private set { Set(ref _consentNotice, value); OnPropertyChanged(nameof(HasConsentNotice)); }
    }
    public bool HasConsentNotice => !string.IsNullOrEmpty(_consentNotice);

    public string UploadStatus
    {
        get
        {
            StatsUploadStatus s = _services.UploadQueue.Status();
            return $"업로드 {s.Uploaded} · 대기 {s.Pending} · 건너뜀 {s.Skipped} · 실패 {s.Failed}";
        }
    }

    public void ApplyConsent()
    {
        string state = ConsentAccepted ? "accepted" : "declined";
        ApplyInfo(_services.Consent.Set(state, UploadEnabled, PublicCharacter, _services.Version));
    }

    public void RefreshConsentFromServer() => ApplyInfo(_services.Consent.GetInfo(syncRemote: true, _services.Version));

    /// <summary>Open the stats site to THIS character's own battle records ("내 캐릭터 통계 보기", Tier A:
    /// identityHash link — portable across reinstalls/other PCs, no nickname in the URL). No-op when no
    /// character is detected (the hash needs both a nickname and a server).</summary>
    public void OpenMyStats()
    {
        string? hash = _services.Consent.CurrentCharacterHash();
        if (string.IsNullOrWhiteSpace(hash))
        {
            return;
        }

        Process.Start(new ProcessStartInfo { FileName = _services.StatsApi.CharacterReportUrl(hash), UseShellExecute = true });
    }

    // ---- per-character consent management (the 내 캐릭터 관리 list) ----
    public ObservableCollection<ConsentCharacterRow> ConsentCharacters { get; } = new();
    public bool HasConsentCharacters => ConsentCharacters.Count > 0;

    /// <summary>Rebuild the management list from the locally-remembered consented characters (current
    /// character first). Local-only + UI-thread (no network); call on open and after each action.</summary>
    public void RefreshConsentCharacters()
    {
        ConsentCharacters.Clear();
        foreach (StatsConsentManager.CharacterConsentInfo c in _services.Consent.ListCharacters())
        {
            if (c.State != "accepted")
            {
                continue; // the management list = currently-consented characters
            }

            string label = !string.IsNullOrWhiteSpace(c.Nickname)
                ? (c.Server > 0 ? $"{c.Nickname} [{ServerNames.GetServerLabel(c.Server)}]" : c.Nickname!)
                : "이름 없음 (이전 기록)";
            string job = string.IsNullOrWhiteSpace(c.Job) ? string.Empty : c.Job! + " · ";

            // 공개 토글 게이트 (W18-UI): 일괄편집은 하나라도 접속 중일 때만(CharacterDetected) 활성화하고,
            // 개별 공개 토글은 CanSetPublic && Grant일 때만 활성. 현재 접속 캐릭터라도 grant가 없으면 이 토글은
            // 비활성이며, 상단 "캐릭터 공개" 체크박스(=Accept 경로, 서버가 최종 판정·실패 시 롤백)로 시도한다.
            // 비공개화·동의 철회는 게이트 없음. (CanSetPublic=false인 이전 기록 행은 목록에서 이미 숨겨짐.)
            bool canEditPublic = CharacterDetected && c.CanSetPublic && c.Grant;
            string tooltip = canEditPublic
                ? "공개하면 통계 사이트에 닉네임·서버가 표시됩니다."
                : !CharacterDetected
                    ? "캐릭터가 접속해 있어야 공개 설정을 바꿀 수 있어요."
                    : "이 기기에서 이 캐릭터로 전투를 업로드한 적이 있어야 공개로 전환할 수 있어요.";

            ConsentCharacters.Add(new ConsentCharacterRow
            {
                IdentityHash = c.IdentityHash,
                Label = label,
                SubLabel = job + (c.PublicCharacter ? "공개" : "비공개 (익명 집계)"),
                IsPublic = c.PublicCharacter,
                CanSetPublic = canEditPublic,
                CanRevoke = true, // 동의 철회는 항상 활성 (오프라인 포함)
                PublicToggleTooltip = tooltip,
                CurrentBadgeVisibility = c.IsCurrent ? Visibility.Visible : Visibility.Collapsed,
            });
        }

        OnPropertyChanged(nameof(HasConsentCharacters));
    }

    /// <summary>Change a character's public flag. Network call — run off the UI thread, then refresh.</summary>
    public void SetCharacterPublic(string identityHash, bool publicCharacter)
        => _services.Consent.SetCharacterPublic(identityHash, publicCharacter, _services.Version);

    /// <summary>Revoke a character's consent. Network call — run off the UI thread, then refresh.</summary>
    public void RevokeConsentCharacter(string identityHash)
        => _services.Consent.RevokeCharacter(identityHash, _services.Version);

    public void RefreshCharacterStatus()
    {
        bool detected = _services.StatsBuilder.OwnCharacter().Detected;
        bool changed = detected != CharacterDetected;
        CharacterDetected = detected;
        OnPropertyChanged(nameof(UploadStatus));
        // Per-character public-toggle enablement depends on CharacterDetected; re-evaluate the rows when it flips.
        if (changed)
        {
            RefreshConsentCharacters();
        }
    }

    /// <summary>Re-read local consent state (no network) and rebuild the management list — call on the UI
    /// thread after any consent action so the rolled-back public flag + notice show.</summary>
    public void RefreshConsentState()
    {
        ApplyInfo(_services.Consent.GetInfo(syncRemote: false, _services.Version));
        RefreshConsentCharacters();
    }

    private void ApplyInfo(StatsConsentManager.Info info)
    {
        ConsentAccepted = info.State == "accepted";
        UploadEnabled = info.UploadEnabled;
        PublicCharacter = info.PublicCharacter;
        ConsentStatus = info.SyncError is { } error ? $"{info.State} · {info.SyncStatus} ({error})" : $"{info.State} · {info.SyncStatus}";
        ConsentNotice = NoticeFor(info.SyncStatus);
        OnPropertyChanged(nameof(UploadStatus));
    }

    // The manager surfaces server outcomes as ASCII status codes (Korean can't live in the EUC-KR settings
    // keys); localize the user-facing ones here.
    private static string NoticeFor(string syncStatus) => syncStatus == StatsConsentManager.PublicRequiresOwnership
        ? "이 기기에서 이 캐릭터로 전투를 업로드한 적이 있어야 공개로 전환할 수 있어요. (지금은 비공개로 동의되었습니다.)"
        : string.Empty;

    // ---- server ----
    private string _serverIp = string.Empty;
    public string ServerIp { get => _serverIp; set => Set(ref _serverIp, value); }
    private string _serverPort = string.Empty;
    public string ServerPort { get => _serverPort; set => Set(ref _serverPort, value); }

    public void SaveServer()
    {
        _services.Props.SetProperty("server.ip", ServerIp);
        _services.Props.SetProperty("server.port", ServerPort);
    }

    // ---- nav rail + footer ----
    private string _selectedNav = "display";
    public string SelectedNav { get => _selectedNav; set => Set(ref _selectedNav, value); }

    public string Version => _services.Version;

    // ---- advanced ----
    public IReadOnlyList<SettingOption> CloseActions { get; } = new[]
    {
        new SettingOption("종료 시 묻기", "ask"),
        new SettingOption("트레이로 최소화", "tray"),
        new SettingOption("프로그램 종료", "exit"),
    };
    public string CloseAction { get => _settings.CloseAction; set { _settings.CloseAction = value; OnPropertyChanged(); } }

    public IReadOnlyList<SettingOption> CaptureBackends { get; } = new[]
    {
        new SettingOption("WinDivert (기본)", "windivert"),
        new SettingOption("Npcap", "npcap"),
    };
    public string CaptureBackend { get => _settings.CaptureBackend; set { _settings.CaptureBackend = value; OnPropertyChanged(); } }

    /// <summary>Footer "기본값 복원": restore the display settings + theme to defaults.</summary>
    public void ResetDefaults()
    {
        DisplayMode = "dps_percent";
        DamageValueMode = "dps";
        ContributionMode = "contribution";
        NameDisplay = "all";
        FontFamily = "NEXON Lv2 Gothic Medium";
        RowHeight = 36;
        MeterOpacity = 0.4;
        BarStyle = "fill";
        Skin = "dark";
        Theme.Reset();
    }

    // ---- skin (overall style preset) ----
    public IReadOnlyList<SkinManager.SkinOption> Skins => SkinManager.Skins;

    /// <summary>Active skin preset; applied + persisted live (swaps the Skin.* palette app-wide).</summary>
    public string Skin
    {
        get => _skin.Current;
        set { _skin.Apply(value); OnPropertyChanged(); }
    }

    // ---- theme (color picker) ----
    /// <summary>The live color theme; the 테마 tab binds swatches/gradient rows directly to its
    /// properties (colors apply + persist immediately, like the React panel).</summary>
    public MeterColorTheme Theme { get; }

    /// <summary>Restore the default palette (writes DEFAULT_THEME back to the "theme" key).</summary>
    public void ResetTheme() => Theme.Reset();

    // ---- diagnostics (packet logging) ----
    public bool IsLoggingActive => _services.DebugLogger.IsRunning;

    public string LoggingButtonLabel => _services.DebugLogger.IsRunning ? "기록 중지" : "기록 시작";

    public string LoggingStatus => _services.DebugLogger.IsRunning
        ? $"기록 중 · 세그먼트 {_services.DebugLogger.CaptureCount} · {_services.DebugLogger.LineCount} 줄"
        : "중지됨";

    /// <summary>Start/stop a packet-debug-logs capture session (replayable corpus).</summary>
    public void ToggleLogging()
    {
        if (_services.DebugLogger.IsRunning)
        {
            _services.DebugLogger.Stop();
        }
        else
        {
            _services.DebugLogger.Start();
        }

        RefreshLogging();
        OnPropertyChanged(nameof(LoggingButtonLabel));
        OnPropertyChanged(nameof(IsLoggingActive));
    }

    /// <summary>Re-reads the live logging counters (polled while the window is open).</summary>
    public void RefreshLogging() => OnPropertyChanged(nameof(LoggingStatus));

    public void OpenLogFolder()
    {
        string dir = PacketDebugLogger.LogDirectory();
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
    }

    public void Reload()
    {
        ApplyInfo(_services.Consent.GetInfo(syncRemote: false, _services.Version));
        RefreshCharacterStatus();
        RefreshConsentCharacters();
        RefreshCustomAlarms();
        CaptureConfig config = _services.BuildCaptureConfig();
        ServerIp = config.ServerIp;
        ServerPort = config.ServerPort;
    }

    /// <summary>Commit buffered hotkeys (Save).</summary>
    public void Commit()
    {
        _hotkeys.SetReset(PendingReset);
        _hotkeys.SetVisibility(PendingVisibility);
        _hotkeys.SetClickThrough(PendingClickThrough);
    }

    /// <summary>Revert live-applied settings + pending hotkeys (Cancel).</summary>
    public void Revert()
    {
        _snapshot.Apply(_settings, _controller);
        PendingReset = _hotkeys.Reset;
        PendingVisibility = _hotkeys.Visibility;
        PendingClickThrough = _hotkeys.ClickThrough;
        Reload();
    }

    private sealed record Snapshot(
        string DisplayMode, string DamageValueMode, string ContributionMode, string NameDisplay,
        string FontFamily, int RowHeight, double MeterOpacity, bool MultiMonitor, string Theme, bool AutoHide,
        string TargetInfoDisplayMode, bool IsMinimal, bool ShowCombatTimerInMinimal, bool ShowTargetInfoInMinimal,
        bool ShowServerTag, string BarStyle, bool ShowJoinPanel, bool ShowPreCombatRoster)
    {
        public static Snapshot Capture(MeterSettings s, OverlayController c) => new(
            s.DisplayMode, s.DamageValueMode, s.ContributionMode, s.NameDisplay,
            s.FontFamily, s.RowHeight, s.MeterOpacity, s.MultiMonitorMode, s.OverlayTheme, c.IsAutoHide,
            s.TargetInfoDisplayMode, s.IsMinimal, s.ShowCombatTimerInMinimal, s.ShowTargetInfoInMinimal,
            s.ShowServerTag, s.BarStyle, s.ShowJoinPanel, s.ShowPreCombatRoster);

        public void Apply(MeterSettings s, OverlayController c)
        {
            s.DisplayMode = DisplayMode;
            s.DamageValueMode = DamageValueMode;
            s.ContributionMode = ContributionMode;
            s.NameDisplay = NameDisplay;
            s.FontFamily = FontFamily;
            s.RowHeight = RowHeight;
            s.MeterOpacity = MeterOpacity;
            s.MultiMonitorMode = MultiMonitor;
            s.OverlayTheme = Theme;
            c.SetAutoHide(AutoHide);
            s.TargetInfoDisplayMode = TargetInfoDisplayMode;
            s.IsMinimal = IsMinimal;
            s.ShowCombatTimerInMinimal = ShowCombatTimerInMinimal;
            s.ShowTargetInfoInMinimal = ShowTargetInfoInMinimal;
            s.ShowServerTag = ShowServerTag;
            s.BarStyle = BarStyle;
            s.ShowJoinPanel = ShowJoinPanel;
            s.ShowPreCombatRoster = ShowPreCombatRoster;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value))
        {
            return;
        }

        field = value;
        OnPropertyChanged(name);
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

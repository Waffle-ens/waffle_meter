using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using WaffleMeter.App.Core;
using WaffleMeter.Capture;
using WaffleMeter.Capture.Live;
using WaffleMeter.Stats;

namespace WaffleMeter.App.Wpf;

/// <summary>A label/value choice for a settings ComboBox.</summary>
public sealed record SettingOption(string Label, string Value);

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
        new SettingOption("나만 표기", "me_only"),
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

    // Bundled-or-fallback fonts (see Fonts/README.md). Each font ships a regular + a bolder weight; the
    // bold weight is a SEPARATE Win32 family name (e.g. "NEXON Lv2 Gothic Medium") so it resolves as its
    // own family with no font-weight plumbing. Values = the font's family name used by FontFamilyConverter
    // (./Fonts/#<value>). Malgun Gothic is always available (system fallback).
    public IReadOnlyList<SettingOption> FontFamilies { get; } = new[]
    {
        new SettingOption("NEXON Lv2 Gothic (Bold, 기본)", "NEXON Lv2 Gothic Medium"),
        new SettingOption("NEXON Lv2 Gothic", "NEXON Lv2 Gothic"),
        new SettingOption("Pretendard (Bold)", "Pretendard SemiBold"),
        new SettingOption("Pretendard", "Pretendard"),
        new SettingOption("Spoqa Han Sans Neo (Bold)", "Spoqa Han Sans Neo Medium"),
        new SettingOption("Spoqa Han Sans Neo", "Spoqa Han Sans Neo"),
        new SettingOption("Freesentation (Bold)", "Freesentation 6 SemiBold"),
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
    public string TargetInfoDisplayMode { get => _settings.TargetInfoDisplayMode; set { _settings.TargetInfoDisplayMode = value; OnPropertyChanged(); } }
    public string BarStyle { get => _settings.BarStyle; set { _settings.BarStyle = value; OnPropertyChanged(); } }
    public bool IsMinimal { get => _settings.IsMinimal; set { _settings.IsMinimal = value; OnPropertyChanged(); } }
    public bool ShowCombatTimerInMinimal { get => _settings.ShowCombatTimerInMinimal; set { _settings.ShowCombatTimerInMinimal = value; OnPropertyChanged(); } }
    public bool ShowTargetInfoInMinimal { get => _settings.ShowTargetInfoInMinimal; set { _settings.ShowTargetInfoInMinimal = value; OnPropertyChanged(); } }
    public bool ShowServerTag { get => _settings.ShowServerTag; set { _settings.ShowServerTag = value; OnPropertyChanged(); } }

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
    // (Light mode is now a skin — "light" in the Skin list — not a separate overlayTheme toggle.)

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

    // ---- hotkey rebinding (buffered, committed on Save) ----
    private HotkeyCombo _pendingReset;
    public HotkeyCombo PendingReset { get => _pendingReset; set => Set(ref _pendingReset, value); }
    private HotkeyCombo _pendingVisibility;
    public HotkeyCombo PendingVisibility { get => _pendingVisibility; set => Set(ref _pendingVisibility, value); }
    private HotkeyCombo _pendingClickThrough;
    public HotkeyCombo PendingClickThrough { get => _pendingClickThrough; set => Set(ref _pendingClickThrough, value); }

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

    public void RefreshCharacterStatus()
    {
        CharacterDetected = _services.StatsBuilder.OwnCharacter().Detected;
        OnPropertyChanged(nameof(UploadStatus));
    }

    private void ApplyInfo(StatsConsentManager.Info info)
    {
        ConsentAccepted = info.State == "accepted";
        UploadEnabled = info.UploadEnabled;
        PublicCharacter = info.PublicCharacter;
        ConsentStatus = info.SyncError is { } error ? $"{info.State} · {info.SyncStatus} ({error})" : $"{info.State} · {info.SyncStatus}";
        OnPropertyChanged(nameof(UploadStatus));
    }

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
        CaptureConfig config = _services.BuildCaptureConfig();
        ServerIp = config.ServerIp;
        ServerPort = config.ServerPort;
    }

    /// <summary>Commit buffered hotkeys (Save).</summary>
    public void Commit()
    {
        _hotkeys.SetReset(PendingReset.Modifiers, PendingReset.VkCode);
        _hotkeys.SetVisibility(PendingVisibility.Modifiers, PendingVisibility.VkCode);
        _hotkeys.SetClickThrough(PendingClickThrough.Modifiers, PendingClickThrough.VkCode);
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
        bool ShowServerTag, string BarStyle, bool ShowJoinPanel)
    {
        public static Snapshot Capture(MeterSettings s, OverlayController c) => new(
            s.DisplayMode, s.DamageValueMode, s.ContributionMode, s.NameDisplay,
            s.FontFamily, s.RowHeight, s.MeterOpacity, s.MultiMonitorMode, s.OverlayTheme, c.IsAutoHide,
            s.TargetInfoDisplayMode, s.IsMinimal, s.ShowCombatTimerInMinimal, s.ShowTargetInfoInMinimal,
            s.ShowServerTag, s.BarStyle, s.ShowJoinPanel);

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

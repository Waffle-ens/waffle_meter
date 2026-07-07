using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using WaffleMeter.Services;

namespace WaffleMeter.App.Core;

/// <summary>
/// Persisted overlay/display settings — the .NET analogue of the React useSettingsStore saveProps
/// layer. Reads/writes the SAME PropertyHandler keys with the SAME string encoding (lowercase
/// "true"/"false" booleans, invariant numbers, raw enum strings) so settings round-trip with any
/// existing settings.properties. Raises INotifyPropertyChanged so the overlay updates live.
/// </summary>
public sealed class MeterSettings : INotifyPropertyChanged
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly string[] DisplayModes = { "dps_percent", "amount_dps_percent", "amount_percent", "amount_full_dps_percent", "amount_full_percent" };
    private static readonly string[] DamageValueModes = { "dps", "total" };
    private static readonly string[] ContributionModes = { "contribution", "entireContribution" };
    private static readonly string[] NameDisplays = { "all", "me_only", "hidden" };
    private static readonly string[] Themes = { "dark", "light" };
    private static readonly string[] CloseActions = { "ask", "tray", "exit" };
    private static readonly string[] CaptureBackends = { "windivert", "npcap" };
    private static readonly string[] TargetInfoDisplayModes = { "hp_full_percent", "hp_percent", "remain_full_percent", "remain_percent", "percent" };
    private static readonly string[] BarStyles = { "fill", "bar", "none" };

    private readonly PropertyHandler _props;

    public MeterSettings(PropertyHandler props)
    {
        _props = props;
        _displayMode = ReadEnum("displayMode", "dps_percent", DisplayModes);
        _damageValueMode = ReadEnum("damageValueMode", "dps", DamageValueModes);
        _contributionMode = ReadEnum("contributionMode", "contribution", ContributionModes);
        _nameDisplay = ReadEnum("nameDisplay", "all", NameDisplays);
        _overlayTheme = ReadEnum("overlayTheme", "dark", Themes);
        _closeAction = ReadEnum("closeAction", "ask", CloseActions);
        _fontFamily = _props.GetProperty("fontFamily") ?? "NEXON Lv2 Gothic Medium";
        _rowHeight = ReadInt("rowHeight", 36);
        _meterOpacity = ReadDouble("meterOpacity", 0.4);
        _isMinimal = ReadBool("isMinimal", false);
        _showCombatTimerInMinimal = ReadBool("showCombatTimerInMinimal", true);
        _showTargetInfoInMinimal = ReadBool("showTargetInfoInMinimal", true);
        _showServerTag = ReadBool("showServerTag", true);
        _showJoinPanel = ReadBool("showJoinPanel", true);
        _showPreCombatRoster = ReadBool("showPreCombatRoster", true);
        _multiMonitorMode = ReadBool("multiMonitorMode", false);
        _taskbarMode = ReadBool("taskbarMode", false);
        _captureBackend = ReadEnum("captureBackend", "windivert", CaptureBackends);
        _targetInfoDisplayMode = ReadEnum("targetInfoDisplayMode", "hp_full_percent", TargetInfoDisplayModes);
        _barStyle = ReadEnum("barStyle", "fill", BarStyles);
        _shugoAlarmEnabled = ReadBool("alarms.shugoEnabled", false);
        _shugoLead10 = ReadBool("alarms.shugoLead10", false);
        _shugoLead5 = ReadBool("alarms.shugoLead5", true);
        _shugoLead1 = ReadBool("alarms.shugoLead1", false);
        _shugoLeadStart = ReadBool("alarms.shugoStart", true);
        _alarmSoundEnabled = ReadBool("alarms.soundEnabled", true);
        _ttsEnabled = ReadBool("alarms.ttsEnabled", false);
        _alarmVolume = ReadDouble("alarms.volume", 0.5);
        _customAlarms = CustomAlarmCodec.Decode(_props.GetProperty("alarms.custom")).ToList();
        _fieldBossAlarmEnabled = ReadBool("alarms.fieldBossEnabled", false);
        _fieldBossLead5 = ReadBool("alarms.fieldBossLead5", false);
        _fieldBossLead10 = ReadBool("alarms.fieldBossLead10", true);
        _fieldBossLead30 = ReadBool("alarms.fieldBossLead30", false);
        _refreshIntervalMs = ReadInt("refreshIntervalMs", 500);
        _maxVisibleRows = ReadInt("maxVisibleRows", 10);
        _lowSpecMode = ReadBool("lowSpecMode", false);
        _showAetherStatus = ReadBool("showAetherStatus", true);
        _vrrCompatMode = ReadBool("vrrCompatMode", true);
        _showBuffUi = ReadBool("buffUi.show", false);
        _buffUiOnlyWhenActive = ReadBool("buffUi.onlyWhenActive", false);
        _showOtherPlayerBuffs = ReadBool("buffUi.showOther", true);
    }

    private string _displayMode;
    public string DisplayMode { get => _displayMode; set => SetProp(ref _displayMode, "displayMode", value); }

    private string _damageValueMode;
    public string DamageValueMode { get => _damageValueMode; set => SetProp(ref _damageValueMode, "damageValueMode", value); }

    private string _contributionMode;
    public string ContributionMode { get => _contributionMode; set => SetProp(ref _contributionMode, "contributionMode", value); }

    private string _nameDisplay;
    public string NameDisplay { get => _nameDisplay; set => SetProp(ref _nameDisplay, "nameDisplay", value); }

    private string _overlayTheme;
    public string OverlayTheme { get => _overlayTheme; set => SetProp(ref _overlayTheme, "overlayTheme", value); }

    private string _closeAction;
    public string CloseAction { get => _closeAction; set => SetProp(ref _closeAction, "closeAction", value); }

    private string _fontFamily;
    public string FontFamily { get => _fontFamily; set => SetProp(ref _fontFamily, "fontFamily", value); }

    private int _rowHeight;
    public int RowHeight { get => _rowHeight; set => SetInt(ref _rowHeight, "rowHeight", value); }

    private double _meterOpacity;
    public double MeterOpacity { get => _meterOpacity; set => SetDouble(ref _meterOpacity, "meterOpacity", value); }

    private bool _isMinimal;
    public bool IsMinimal { get => _isMinimal; set => SetBool(ref _isMinimal, "isMinimal", value); }

    private bool _showCombatTimerInMinimal;
    public bool ShowCombatTimerInMinimal { get => _showCombatTimerInMinimal; set => SetBool(ref _showCombatTimerInMinimal, "showCombatTimerInMinimal", value); }

    private bool _showTargetInfoInMinimal;
    public bool ShowTargetInfoInMinimal { get => _showTargetInfoInMinimal; set => SetBool(ref _showTargetInfoInMinimal, "showTargetInfoInMinimal", value); }

    private bool _showServerTag;
    /// <summary>Show the abbreviated server label "[xx]" next to each player nickname in the meter rows.</summary>
    public bool ShowServerTag { get => _showServerTag; set => SetBool(ref _showServerTag, "showServerTag", value); }

    private string _targetInfoDisplayMode;
    public string TargetInfoDisplayMode { get => _targetInfoDisplayMode; set => SetProp(ref _targetInfoDisplayMode, "targetInfoDisplayMode", value); }

    private string _barStyle;
    /// <summary>Damage gauge form: "fill" (proportional cell fill), "bar" (thin bottom bar), "none".</summary>
    public string BarStyle { get => _barStyle; set => SetProp(ref _barStyle, "barStyle", value); }

    private bool _multiMonitorMode;
    public bool MultiMonitorMode { get => _multiMonitorMode; set => SetBool(ref _multiMonitorMode, "multiMonitorMode", value); }

    private bool _showJoinPanel;
    /// <summary>Auto-show the party join-request panel when a request arrives. Off = stay hidden; the
    /// header 파티 신청 button still opens it manually.</summary>
    public bool ShowJoinPanel { get => _showJoinPanel; set => SetBool(ref _showJoinPanel, "showJoinPanel", value); }

    private bool _showPreCombatRoster;
    /// <summary>Show the party roster as idle (0-DPS) rows before combat starts, so party members appear on
    /// dungeon entry instead of the "전투 대기 중" placeholder. Only shown while there is no live combat
    /// data; the moment any damage lands the normal combat rows take over.</summary>
    public bool ShowPreCombatRoster { get => _showPreCombatRoster; set => SetBool(ref _showPreCombatRoster, "showPreCombatRoster", value); }

    private bool _taskbarMode;
    public bool TaskbarMode { get => _taskbarMode; set => SetBool(ref _taskbarMode, "taskbarMode", value); }

    private string _captureBackend;
    public string CaptureBackend { get => _captureBackend; set => SetProp(ref _captureBackend, "captureBackend", value); }

    // ---- alarms (슈고 페스타 reminder) ----
    private bool _shugoAlarmEnabled;
    /// <summary>Master toggle for the 슈고 페스타 (top-of-hour event) reminder alarm.</summary>
    public bool ShugoAlarmEnabled { get => _shugoAlarmEnabled; set => SetBool(ref _shugoAlarmEnabled, "alarms.shugoEnabled", value); }

    private bool _shugoLead10;
    public bool ShugoLead10 { get => _shugoLead10; set => SetBool(ref _shugoLead10, "alarms.shugoLead10", value); }

    private bool _shugoLead5;
    public bool ShugoLead5 { get => _shugoLead5; set => SetBool(ref _shugoLead5, "alarms.shugoLead5", value); }

    private bool _shugoLead1;
    public bool ShugoLead1 { get => _shugoLead1; set => SetBool(ref _shugoLead1, "alarms.shugoLead1", value); }

    private bool _shugoLeadStart;
    public bool ShugoLeadStart { get => _shugoLeadStart; set => SetBool(ref _shugoLeadStart, "alarms.shugoStart", value); }

    private bool _alarmSoundEnabled;
    public bool AlarmSoundEnabled { get => _alarmSoundEnabled; set => SetBool(ref _alarmSoundEnabled, "alarms.soundEnabled", value); }

    private bool _ttsEnabled;
    /// <summary>Speak alerts with an online Korean neural voice instead of (or before falling back to) the
    /// chime. Opt-in: the voice endpoint is unofficial and every failure degrades to the local sound.</summary>
    public bool TtsEnabled { get => _ttsEnabled; set => SetBool(ref _ttsEnabled, "alarms.ttsEnabled", value); }

    private double _alarmVolume;
    public double AlarmVolume { get => _alarmVolume; set => SetDouble(ref _alarmVolume, "alarms.volume", value); }

    // ---- field-boss respawn reminder ----
    private bool _fieldBossAlarmEnabled;
    /// <summary>Master toggle for the field-boss respawn-timer reminder (driven by the 0x9101 broadcast).</summary>
    public bool FieldBossAlarmEnabled { get => _fieldBossAlarmEnabled; set => SetBool(ref _fieldBossAlarmEnabled, "alarms.fieldBossEnabled", value); }

    private bool _fieldBossLead5;
    public bool FieldBossLead5 { get => _fieldBossLead5; set => SetBool(ref _fieldBossLead5, "alarms.fieldBossLead5", value); }

    private bool _fieldBossLead10;
    public bool FieldBossLead10 { get => _fieldBossLead10; set => SetBool(ref _fieldBossLead10, "alarms.fieldBossLead10", value); }

    private bool _fieldBossLead30;
    public bool FieldBossLead30 { get => _fieldBossLead30; set => SetBool(ref _fieldBossLead30, "alarms.fieldBossLead30", value); }

    /// <summary>The enabled field-boss lead minutes (empty = none).</summary>
    public IReadOnlyCollection<int> FieldBossLeads
    {
        get
        {
            var s = new HashSet<int>();
            if (_fieldBossLead5) s.Add(5);
            if (_fieldBossLead10) s.Add(10);
            if (_fieldBossLead30) s.Add(30);
            return s;
        }
    }

    private List<CustomAlarm> _customAlarms;
    /// <summary>User-defined recurring reminders. Persisted as one Base64(JSON) value (alarms.custom).</summary>
    public IReadOnlyList<CustomAlarm> CustomAlarms
    {
        get => _customAlarms;
        set
        {
            _customAlarms = value.ToList();
            _props.SetProperty("alarms.custom", CustomAlarmCodec.Encode(_customAlarms));
            OnPropertyChanged();
        }
    }

    // ---- display performance (refresh rate / row cap / low-spec) ----
    private int _refreshIntervalMs;
    /// <summary>How often (ms) the meter recomputes + repaints the report. Larger = less CPU/UI churn during
    /// combat, at the cost of coarser live DPS. Clamped to [100, 1000] where it is consumed.</summary>
    public int RefreshIntervalMs { get => _refreshIntervalMs; set => SetInt(ref _refreshIntervalMs, "refreshIntervalMs", value); }

    private int _maxVisibleRows;
    /// <summary>How many dealer rows the meter shows (1-10). Self is always shown even below the cap. All
    /// participants stay tracked; this only caps the display.</summary>
    public int MaxVisibleRows { get => _maxVisibleRows; set => SetInt(ref _maxVisibleRows, "maxVisibleRows", value); }

    private bool _lowSpecMode;
    /// <summary>Frame-drop relief: pins the refresh interval to a low-churn value and force-disables any
    /// display-only embellishments, prioritizing the game's frame rate. See <see cref="EffectiveRefreshIntervalMs"/>.</summary>
    public bool LowSpecMode { get => _lowSpecMode; set => SetBool(ref _lowSpecMode, "lowSpecMode", value); }

    /// <summary>The refresh interval actually applied: low-spec pins it to 500 ms (ignoring the slider);
    /// otherwise the slider value clamped to [100, 1000].</summary>
    public int EffectiveRefreshIntervalMs => _lowSpecMode ? 500 : Math.Clamp(_refreshIntervalMs, 100, 1000);

    /// <summary>The row cap actually applied, clamped to [1, 10].</summary>
    public int EffectiveMaxVisibleRows => Math.Clamp(_maxVisibleRows, 1, 10);

    private bool _showAetherStatus;
    /// <summary>Show the aether (오드) balance badge next to the recognized character.</summary>
    public bool ShowAetherStatus { get => _showAetherStatus; set => SetBool(ref _showAetherStatus, "showAetherStatus", value); }

    private bool _vrrCompatMode;
    /// <summary>FreeSync/G-Sync compatibility: render the overlay in software (default) so a GPU-composited
    /// transparent window can't disturb a variable-refresh display. Read at startup (process-global) — a
    /// change needs an app restart.</summary>
    public bool VrrCompatMode { get => _vrrCompatMode; set => SetBool(ref _vrrCompatMode, "vrrCompatMode", value); }

    // ---- combat-assist overlay (live buff / cooldown slots) ----
    private bool _showBuffUi;
    /// <summary>Show the combat-assist overlay: a small window of the local player's active buff slots
    /// (remaining time), separate from the meter.</summary>
    public bool ShowBuffUi { get => _showBuffUi; set => SetBool(ref _showBuffUi, "buffUi.show", value); }

    private bool _buffUiOnlyWhenActive;
    /// <summary>Hide the combat-assist overlay while there is nothing to show.</summary>
    public bool BuffUiOnlyWhenActive { get => _buffUiOnlyWhenActive; set => SetBool(ref _buffUiOnlyWhenActive, "buffUi.onlyWhenActive", value); }

    private bool _showOtherPlayerBuffs;
    /// <summary>Include buffs applied by other players (off = only the local player's own buffs).</summary>
    public bool ShowOtherPlayerBuffs { get => _showOtherPlayerBuffs; set => SetBool(ref _showOtherPlayerBuffs, "buffUi.showOther", value); }

    /// <summary>Resolve the masking mode enum for the meter rows.</summary>
    public NameDisplay NameDisplayMode => _nameDisplay switch
    {
        "me_only" => Core.NameDisplay.MeOnly,
        "hidden" => Core.NameDisplay.Hidden,
        _ => Core.NameDisplay.All,
    };

    public bool UseTotalDamage => _damageValueMode == "total";
    public bool UseEntireContribution => _contributionMode == "entireContribution";

    private string ReadEnum(string key, string fallback, string[] allowed)
    {
        string? v = _props.GetProperty(key);
        return v != null && Array.IndexOf(allowed, v) >= 0 ? v : fallback;
    }

    private bool ReadBool(string key, bool fallback) => _props.GetProperty(key) switch
    {
        "true" => true,
        "false" => false,
        _ => fallback,
    };

    private int ReadInt(string key, int fallback) =>
        int.TryParse(_props.GetProperty(key), NumberStyles.Integer, Inv, out int v) ? v : fallback;

    private double ReadDouble(string key, double fallback)
    {
        string? raw = _props.GetProperty(key);
        return !string.IsNullOrEmpty(raw) && double.TryParse(raw, NumberStyles.Float, Inv, out double v) ? v : fallback;
    }

    private void SetProp(ref string field, string key, string value, [CallerMemberName] string? prop = null)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        _props.SetProperty(key, value);
        OnPropertyChanged(prop);
    }

    private void SetInt(ref int field, string key, int value, [CallerMemberName] string? prop = null)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        _props.SetProperty(key, value.ToString(Inv));
        OnPropertyChanged(prop);
    }

    private void SetDouble(ref double field, string key, double value, [CallerMemberName] string? prop = null)
    {
        if (field.Equals(value))
        {
            return;
        }

        field = value;
        _props.SetProperty(key, value.ToString(Inv));
        OnPropertyChanged(prop);
    }

    private void SetBool(ref bool field, string key, bool value, [CallerMemberName] string? prop = null)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        _props.SetProperty(key, value ? "true" : "false");
        OnPropertyChanged(prop);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

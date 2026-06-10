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
        _fontFamily = _props.GetProperty("fontFamily") ?? "NEXON Lv2 Gothic";
        _rowHeight = ReadInt("rowHeight", 36);
        _meterOpacity = ReadDouble("meterOpacity", 0.4);
        _isMinimal = ReadBool("isMinimal", false);
        _multiMonitorMode = ReadBool("multiMonitorMode", false);
        _taskbarMode = ReadBool("taskbarMode", false);
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

    private bool _multiMonitorMode;
    public bool MultiMonitorMode { get => _multiMonitorMode; set => SetBool(ref _multiMonitorMode, "multiMonitorMode", value); }

    private bool _taskbarMode;
    public bool TaskbarMode { get => _taskbarMode; set => SetBool(ref _taskbarMode, "taskbarMode", value); }

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

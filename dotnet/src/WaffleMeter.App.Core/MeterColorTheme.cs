using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using WaffleMeter.Services;

namespace WaffleMeter.App.Core;

/// <summary>
/// User-customizable meter color theme — the .NET analogue of the React <c>ThemeColors</c> store.
/// Persists to the SAME single <c>theme</c> PropertyHandler key as one JSON object
/// (<c>{"userBar":["#..","#.."],"serverAColor":"#..",...}</c>) so it round-trips with any existing
/// <c>settings.properties</c>; missing keys fall back to <see cref="DefaultTheme"/> (React's
/// <c>{...DEFAULT_THEME, ...parsed}</c> merge). Raises <see cref="Changed"/> + INotifyPropertyChanged
/// so the overlay rebuilds its brushes live.
/// </summary>
public sealed class MeterColorTheme : INotifyPropertyChanged
{
    // DEFAULT_THEME (useSettingsStore.ts), verbatim.
    public static readonly (string From, string To) DefaultUserBar = ("#15c98f", "#0b8f72");
    public static readonly (string From, string To) DefaultNormalBar = ("#f6c65b", "#d68a21");
    public static readonly (string From, string To) DefaultWarningBar = ("#ff9f45", "#d96d19");
    public static readonly (string From, string To) DefaultErrorBar = ("#ef4444", "#991b1b");
    public static readonly (string From, string To) DefaultBossBar = ("#e11d48", "#7f1d1d");
    public const string DefaultServerAColor = "#7dd3fc";
    public const string DefaultServerBColor = "#f0abfc";
    public const string DefaultServerDefaultColor = "#ffffff";
    public const string DefaultMeterStatAmount = "#f8d66d";
    public const string DefaultMeterStatDps = "#f8fafc";
    public const string DefaultMeterStatPercent = "#8ee6cf";
    public const string DefaultBossRightValue = "#fecdd3";
    public const string DefaultCombatTimeColor = "#cbd5e1";

    private readonly PropertyHandler _props;
    private bool _loading;

    public MeterColorTheme(PropertyHandler props)
    {
        _props = props;
        ResetFields();
        Load();
    }

    private string _userBarFrom = null!, _userBarTo = null!;
    private string _normalBarFrom = null!, _normalBarTo = null!;
    private string _warningBarFrom = null!, _warningBarTo = null!;
    private string _errorBarFrom = null!, _errorBarTo = null!;
    private string _bossBarFrom = null!, _bossBarTo = null!;
    private string _serverAColor = null!, _serverBColor = null!, _serverDefaultColor = null!;
    private string _meterStatAmount = null!, _meterStatDps = null!, _meterStatPercent = null!;
    private string _bossRightValue = null!, _combatTimeColor = null!;

    public string UserBarFrom { get => _userBarFrom; set => Set(ref _userBarFrom, value); }
    public string UserBarTo { get => _userBarTo; set => Set(ref _userBarTo, value); }
    public string NormalBarFrom { get => _normalBarFrom; set => Set(ref _normalBarFrom, value); }
    public string NormalBarTo { get => _normalBarTo; set => Set(ref _normalBarTo, value); }
    public string WarningBarFrom { get => _warningBarFrom; set => Set(ref _warningBarFrom, value); }
    public string WarningBarTo { get => _warningBarTo; set => Set(ref _warningBarTo, value); }
    public string ErrorBarFrom { get => _errorBarFrom; set => Set(ref _errorBarFrom, value); }
    public string ErrorBarTo { get => _errorBarTo; set => Set(ref _errorBarTo, value); }
    public string BossBarFrom { get => _bossBarFrom; set => Set(ref _bossBarFrom, value); }
    public string BossBarTo { get => _bossBarTo; set => Set(ref _bossBarTo, value); }
    public string ServerAColor { get => _serverAColor; set => Set(ref _serverAColor, value); }
    public string ServerBColor { get => _serverBColor; set => Set(ref _serverBColor, value); }
    public string ServerDefaultColor { get => _serverDefaultColor; set => Set(ref _serverDefaultColor, value); }
    public string MeterStatAmount { get => _meterStatAmount; set => Set(ref _meterStatAmount, value); }
    public string MeterStatDps { get => _meterStatDps; set => Set(ref _meterStatDps, value); }
    public string MeterStatPercent { get => _meterStatPercent; set => Set(ref _meterStatPercent, value); }
    public string BossRightValue { get => _bossRightValue; set => Set(ref _bossRightValue, value); }
    public string CombatTimeColor { get => _combatTimeColor; set => Set(ref _combatTimeColor, value); }

    /// <summary>Raised on any color change (and reset) so views rebuild brushes.</summary>
    public event EventHandler? Changed;

    public void Reset()
    {
        _loading = true;
        ResetFields();
        _loading = false;
        Persist();
        RaiseAll();
    }

    private void ResetFields()
    {
        (_userBarFrom, _userBarTo) = DefaultUserBar;
        (_normalBarFrom, _normalBarTo) = DefaultNormalBar;
        (_warningBarFrom, _warningBarTo) = DefaultWarningBar;
        (_errorBarFrom, _errorBarTo) = DefaultErrorBar;
        (_bossBarFrom, _bossBarTo) = DefaultBossBar;
        _serverAColor = DefaultServerAColor;
        _serverBColor = DefaultServerBColor;
        _serverDefaultColor = DefaultServerDefaultColor;
        _meterStatAmount = DefaultMeterStatAmount;
        _meterStatDps = DefaultMeterStatDps;
        _meterStatPercent = DefaultMeterStatPercent;
        _bossRightValue = DefaultBossRightValue;
        _combatTimeColor = DefaultCombatTimeColor;
    }

    private void Load()
    {
        string? raw = _props.GetProperty("theme");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        try
        {
            ThemeDto? dto = JsonSerializer.Deserialize<ThemeDto>(raw);
            if (dto is null)
            {
                return;
            }

            // Merge over defaults: only override when the key is present (and gradients are 2-length).
            if (dto.UserBar is { Length: 2 }) { _userBarFrom = dto.UserBar[0]; _userBarTo = dto.UserBar[1]; }
            if (dto.NormalBar is { Length: 2 }) { _normalBarFrom = dto.NormalBar[0]; _normalBarTo = dto.NormalBar[1]; }
            if (dto.WarningBar is { Length: 2 }) { _warningBarFrom = dto.WarningBar[0]; _warningBarTo = dto.WarningBar[1]; }
            if (dto.ErrorBar is { Length: 2 }) { _errorBarFrom = dto.ErrorBar[0]; _errorBarTo = dto.ErrorBar[1]; }
            if (dto.BossBar is { Length: 2 }) { _bossBarFrom = dto.BossBar[0]; _bossBarTo = dto.BossBar[1]; }
            if (dto.ServerAColor is { }) { _serverAColor = dto.ServerAColor; }
            if (dto.ServerBColor is { }) { _serverBColor = dto.ServerBColor; }
            if (dto.ServerDefaultColor is { }) { _serverDefaultColor = dto.ServerDefaultColor; }
            if (dto.MeterStatAmount is { }) { _meterStatAmount = dto.MeterStatAmount; }
            if (dto.MeterStatDps is { }) { _meterStatDps = dto.MeterStatDps; }
            if (dto.MeterStatPercent is { }) { _meterStatPercent = dto.MeterStatPercent; }
            if (dto.BossRightValue is { }) { _bossRightValue = dto.BossRightValue; }
            if (dto.CombatTimeColor is { }) { _combatTimeColor = dto.CombatTimeColor; }
        }
        catch
        {
            // malformed theme -> keep defaults
        }
    }

    private void Persist()
    {
        var dto = new ThemeDto
        {
            UserBar = [_userBarFrom, _userBarTo],
            NormalBar = [_normalBarFrom, _normalBarTo],
            WarningBar = [_warningBarFrom, _warningBarTo],
            ErrorBar = [_errorBarFrom, _errorBarTo],
            BossBar = [_bossBarFrom, _bossBarTo],
            ServerAColor = _serverAColor,
            ServerBColor = _serverBColor,
            ServerDefaultColor = _serverDefaultColor,
            MeterStatAmount = _meterStatAmount,
            MeterStatDps = _meterStatDps,
            MeterStatPercent = _meterStatPercent,
            BossRightValue = _bossRightValue,
            CombatTimeColor = _combatTimeColor,
        };
        _props.SetProperty("theme", JsonSerializer.Serialize(dto));
    }

    private void Set(ref string field, string value, [CallerMemberName] string? name = null)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        if (_loading)
        {
            return;
        }

        Persist();
        OnPropertyChanged(name);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void RaiseAll()
    {
        OnPropertyChanged(string.Empty); // null/empty => all bindings refresh
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private sealed class ThemeDto
    {
        [JsonPropertyName("userBar")] public string[]? UserBar { get; set; }
        [JsonPropertyName("normalBar")] public string[]? NormalBar { get; set; }
        [JsonPropertyName("warningBar")] public string[]? WarningBar { get; set; }
        [JsonPropertyName("errorBar")] public string[]? ErrorBar { get; set; }
        [JsonPropertyName("bossBar")] public string[]? BossBar { get; set; }
        [JsonPropertyName("serverAColor")] public string? ServerAColor { get; set; }
        [JsonPropertyName("serverBColor")] public string? ServerBColor { get; set; }
        [JsonPropertyName("serverDefaultColor")] public string? ServerDefaultColor { get; set; }
        [JsonPropertyName("meterStatAmount")] public string? MeterStatAmount { get; set; }
        [JsonPropertyName("meterStatDps")] public string? MeterStatDps { get; set; }
        [JsonPropertyName("meterStatPercent")] public string? MeterStatPercent { get; set; }
        [JsonPropertyName("bossRightValue")] public string? BossRightValue { get; set; }
        [JsonPropertyName("combatTimeColor")] public string? CombatTimeColor { get; set; }
    }
}

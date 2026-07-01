using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using WaffleMeter.Data;
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

    // Meter bar color mode: "self" (본인 강조, by contribution — current/default) or "job" (직업 강조).
    public const string DefaultBarColorMode = "self";
    // Per-job bar colors for 직업 강조 mode. Source of truth = JoinPanelPalette accents (opaque RGB) so the
    // bars match the join-card job tints (design consistency) while staying vivid/eye-catching.
    public const string DefaultJobBarGladiator = "#22d3ee";    // 검성 cyan
    public const string DefaultJobBarTemplar = "#60a5fa";      // 수호성 blue
    public const string DefaultJobBarRanger = "#34d399";       // 궁성 emerald
    public const string DefaultJobBarAssassin = "#84cc16";     // 살성 lime
    public const string DefaultJobBarSorcerer = "#a78bfa";     // 마도성 violet
    public const string DefaultJobBarCleric = "#f59e0b";       // 치유성 amber
    public const string DefaultJobBarElementalist = "#d946ef"; // 정령성 fuchsia
    public const string DefaultJobBarChanter = "#f97316";      // 호법성 orange
    public const string DefaultJobBarFighter = "#f43f5e";      // 권성 rose-red

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
    private string _barColorMode = null!;
    private string _jobBarGladiator = null!, _jobBarTemplar = null!, _jobBarRanger = null!, _jobBarAssassin = null!;
    private string _jobBarSorcerer = null!, _jobBarCleric = null!, _jobBarElementalist = null!, _jobBarChanter = null!;
    private string _jobBarFighter = null!;

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

    /// <summary>Meter bar color mode: "self" (본인 강조) or "job" (직업 강조).</summary>
    public string BarColorMode { get => _barColorMode; set => Set(ref _barColorMode, value); }
    public string JobBarGladiator { get => _jobBarGladiator; set => Set(ref _jobBarGladiator, value); }
    public string JobBarTemplar { get => _jobBarTemplar; set => Set(ref _jobBarTemplar, value); }
    public string JobBarRanger { get => _jobBarRanger; set => Set(ref _jobBarRanger, value); }
    public string JobBarAssassin { get => _jobBarAssassin; set => Set(ref _jobBarAssassin, value); }
    public string JobBarSorcerer { get => _jobBarSorcerer; set => Set(ref _jobBarSorcerer, value); }
    public string JobBarCleric { get => _jobBarCleric; set => Set(ref _jobBarCleric, value); }
    public string JobBarElementalist { get => _jobBarElementalist; set => Set(ref _jobBarElementalist, value); }
    public string JobBarChanter { get => _jobBarChanter; set => Set(ref _jobBarChanter, value); }
    public string JobBarFighter { get => _jobBarFighter; set => Set(ref _jobBarFighter, value); }

    /// <summary>The configured bar color (hex/rgba) for a job in 직업 강조 mode.</summary>
    public string JobBar(JobClass job) => job switch
    {
        JobClass.GLADIATOR => _jobBarGladiator,
        JobClass.TEMPLAR => _jobBarTemplar,
        JobClass.RANGER => _jobBarRanger,
        JobClass.ASSASSIN => _jobBarAssassin,
        JobClass.SORCERER => _jobBarSorcerer,
        JobClass.CLERIC => _jobBarCleric,
        JobClass.ELEMENTALIST => _jobBarElementalist,
        JobClass.CHANTER => _jobBarChanter,
        JobClass.FIGHTER => _jobBarFighter,
        _ => DefaultServerDefaultColor, // neutral white fallback for any unmapped job
    };

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
        _barColorMode = DefaultBarColorMode;
        _jobBarGladiator = DefaultJobBarGladiator;
        _jobBarTemplar = DefaultJobBarTemplar;
        _jobBarRanger = DefaultJobBarRanger;
        _jobBarAssassin = DefaultJobBarAssassin;
        _jobBarSorcerer = DefaultJobBarSorcerer;
        _jobBarCleric = DefaultJobBarCleric;
        _jobBarElementalist = DefaultJobBarElementalist;
        _jobBarChanter = DefaultJobBarChanter;
        _jobBarFighter = DefaultJobBarFighter;
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
            if (dto.BarColorMode is { }) { _barColorMode = dto.BarColorMode; }
            if (dto.JobBarGladiator is { }) { _jobBarGladiator = dto.JobBarGladiator; }
            if (dto.JobBarTemplar is { }) { _jobBarTemplar = dto.JobBarTemplar; }
            if (dto.JobBarRanger is { }) { _jobBarRanger = dto.JobBarRanger; }
            if (dto.JobBarAssassin is { }) { _jobBarAssassin = dto.JobBarAssassin; }
            if (dto.JobBarSorcerer is { }) { _jobBarSorcerer = dto.JobBarSorcerer; }
            if (dto.JobBarCleric is { }) { _jobBarCleric = dto.JobBarCleric; }
            if (dto.JobBarElementalist is { }) { _jobBarElementalist = dto.JobBarElementalist; }
            if (dto.JobBarChanter is { }) { _jobBarChanter = dto.JobBarChanter; }
            if (dto.JobBarFighter is { }) { _jobBarFighter = dto.JobBarFighter; }
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
            BarColorMode = _barColorMode,
            JobBarGladiator = _jobBarGladiator,
            JobBarTemplar = _jobBarTemplar,
            JobBarRanger = _jobBarRanger,
            JobBarAssassin = _jobBarAssassin,
            JobBarSorcerer = _jobBarSorcerer,
            JobBarCleric = _jobBarCleric,
            JobBarElementalist = _jobBarElementalist,
            JobBarChanter = _jobBarChanter,
            JobBarFighter = _jobBarFighter,
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
        [JsonPropertyName("barColorMode")] public string? BarColorMode { get; set; }
        [JsonPropertyName("jobBarGladiator")] public string? JobBarGladiator { get; set; }
        [JsonPropertyName("jobBarTemplar")] public string? JobBarTemplar { get; set; }
        [JsonPropertyName("jobBarRanger")] public string? JobBarRanger { get; set; }
        [JsonPropertyName("jobBarAssassin")] public string? JobBarAssassin { get; set; }
        [JsonPropertyName("jobBarSorcerer")] public string? JobBarSorcerer { get; set; }
        [JsonPropertyName("jobBarCleric")] public string? JobBarCleric { get; set; }
        [JsonPropertyName("jobBarElementalist")] public string? JobBarElementalist { get; set; }
        [JsonPropertyName("jobBarChanter")] public string? JobBarChanter { get; set; }
        [JsonPropertyName("jobBarFighter")] public string? JobBarFighter { get; set; }
    }
}

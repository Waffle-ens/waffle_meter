using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using WaffleMeter.App.Core;
using WaffleMeter.Capture;
using WaffleMeter.Capture.Live;
using WaffleMeter.Stats;

namespace WaffleMeter.App.Wpf;

/// <summary>One effect on the settings preview strip: what it is called, which family it belongs to, and the
/// brush it paints a nickname with. The brush is the SAME shared instance the meter row uses, so an animated
/// sample moves in step with the real thing instead of being a separate approximation.</summary>
public sealed record NameFxSampleViewModel(string Name, string Kind, System.Windows.Media.Brush Fill);

/// <summary>내가 고를 수 있는 연출 하나. <paramref name="IsCurrent"/> 는 지금 적용된 것.</summary>
public sealed record NameFxChoiceViewModel(
    string Id,
    string Name,
    System.Windows.Media.Brush Fill,
    bool IsCurrent);

/// <summary>
/// One gauge skin, drawn as a MOCK METER ROW rather than a colour swatch.
/// <para>A bare bar cannot answer the question this preview exists for. A gauge is the background that damage
/// numbers are read on top of, so "is this skin too loud" only means something with the numbers actually there,
/// at the real fill opacity, with the real proportions. A swatch made every skin look fine.</para>
/// <para>It is a facsimile, not the meter's own row template (that template lives inside
/// <c>OverlayWindow.xaml</c> and pulling it out would touch the meter's rendering — a file this project has
/// regressed on twice over row geometry). What it does mirror exactly are the parts that decide the answer:
/// the fill brush instance, its 0.3 opacity, and the 3 px accent rail keeping its own colour.</para>
/// </summary>
public sealed record GaugeSkinSampleViewModel(
    string Name,
    System.Windows.Media.Brush Fill,
    System.Windows.Media.Brush RailBrush,
    System.Windows.Media.ImageSource? IconSource,
    string Rank,
    string Nickname,
    string ServerTag,
    string PowerText,
    string DpsText,
    string PercentText,
    double BarRatio,
    double BarRest,
    /// <summary>실제 행이 스킨을 받았을 때 쓰는 값과 같아야 한다 — 견본만 진하게(또는 옅게) 그리면
    /// 판단해야 할 축이 통째로 어긋난다.</summary>
    double GaugeOpacity);

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

    /// <summary>The character's last-seen aether (오드) as "base(+bonus)", or empty when none is remembered.</summary>
    public string AetherText { get; init; } = "";

    /// <summary>Visible only when we have a remembered aether balance for this character.</summary>
    public Visibility AetherVisibility { get; init; } = Visibility.Collapsed;
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

/// <summary>One buff-preset chip. <see cref="IsActive"/> is bound two-way to a RadioButton: checking one
/// selects that slot, and the view-model then clears the others (the chips deliberately carry no GroupName —
/// the bar is rendered on two tabs at once, and WPF would group all six buttons together).</summary>
public sealed class BuffPresetSlotViewModel : INotifyPropertyChanged
{
    private readonly Action<int> _select;
    private string _name;
    private bool _isActive;

    public BuffPresetSlotViewModel(int index, string name, bool isActive, Action<int> select)
    {
        Index = index;
        _name = name;
        _isActive = isActive;
        _select = select;
    }

    public int Index { get; }

    public string Name
    {
        get => _name;
        internal set
        {
            if (_name == value)
            {
                return;
            }

            _name = value;
            OnPropertyChanged();
        }
    }

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value)
            {
                return;
            }

            _isActive = value;
            OnPropertyChanged();
            if (value)
            {
                _select(Index);
            }
        }
    }

    /// <summary>Reflect the store's active slot without re-triggering a selection.</summary>
    internal void SyncActive(bool value)
    {
        if (_isActive == value)
        {
            return;
        }

        _isActive = value;
        OnPropertyChanged(nameof(IsActive));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
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
    private readonly BuffPresetManager _presets;
    private readonly GameOptimizerService _gameOpt;
    // NOT readonly: an import re-takes it. Cancel restores the values captured when the window opened, so
    // after a 70-key import it would put back a mixture that neither the code nor any backup describes.
    private Snapshot _snapshot;

    public SettingsViewModel(MeterServices services, MeterSettings settings, MeterColorTheme theme, SkinManager skin, OverlayController controller, HotkeyHandler hotkeys, BuffPresetManager presets, GameOptimizerService gameOpt)
    {
        _services = services;
        _settings = settings;
        Theme = theme;
        _skin = skin;
        _controller = controller;
        _hotkeys = hotkeys;
        _presets = presets;
        _gameOpt = gameOpt;
        _snapshot = Snapshot.Capture(settings, controller);
        RefreshGameOpt();

        _pendingReset = hotkeys.Reset;
        _pendingVisibility = hotkeys.Visibility;
        _pendingClickThrough = hotkeys.ClickThrough;
        _pendingDummyToggle = hotkeys.DummyToggle;
        _pendingDummyReset = hotkeys.DummyReset;

        IReadOnlyList<string> presetNames = _presets.Names;
        for (int i = 0; i < BuffPresetManager.SlotCount; i++)
        {
            PresetSlots.Add(new BuffPresetSlotViewModel(i, presetNames[i], i == _presets.ActiveIndex, SelectPreset));
        }

        RebuildFontCards();
        RebuildNameFxSamples(_skin.IsLight);
        SyncNameFxPreview();
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

    /// <summary>허수아비 test run lengths (label, seconds-as-string for the ComboBox SelectedValue).</summary>
    public IReadOnlyList<SettingOption> DummyDurations { get; } = new[]
    {
        new SettingOption("30초", "30"),
        new SettingOption("1분", "60"),
        new SettingOption("1분 30초", "90"),
        new SettingOption("2분", "120"),
        new SettingOption("3분", "180"),
        new SettingOption("5분", "300"),
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

    public IReadOnlyList<SettingOption> TierEffectModes { get; } = new[]
    {
        new SettingOption("테두리 + 효과", "animated"),
        new SettingOption("테두리만", "static"),
        new SettingOption("표시 안 함", "off"),
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
    /// <summary>Shipped default. Must stay byte-identical to <c>MeterSettings</c>'s own default, or a fresh
    /// install shows no card selected.</summary>
    public const string DefaultFontFamily = "NEXON Lv2 Gothic Medium";

    private static readonly SettingOption[] BuiltInFonts =
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

    /// <summary>The bundled fonts plus any the user has added (a .ttf/.otf in the fonts folder), so a custom
    /// font is selectable in the dropdown. Re-queried when <see cref="AddCustomFont"/> raises the change.</summary>
    public IReadOnlyList<SettingOption> FontFamilies
    {
        get
        {
            var list = new List<SettingOption>(BuiltInFonts);
            var seen = new HashSet<string>();
            foreach (SettingOption o in BuiltInFonts)
            {
                seen.Add(o.Value);
            }

            foreach (string name in FontResolver.EnumerateUserFontFamilies())
            {
                if (seen.Add(name))
                {
                    list.Add(new SettingOption(name + " (사용자)", name));
                }
            }

            return list;
        }
    }

    /// <summary>
    /// The font picker's cards — bundled fonts plus anything the user dropped into the fonts folder. Built once
    /// and mutated in place, because each card resolves its own <see cref="FontFamily"/> at construction and a
    /// getter that rebuilt the list would re-enumerate the fonts folder off disk on every binding refresh.
    /// </summary>
    public ObservableCollection<FontCardViewModel> FontCards { get; } = new();

    /// <summary>Installed system fonts. Not cards: a few hundred entries, and the bundled set is the curated one.
    /// The picked one still previews — the 현재 글꼴 row below the dropdown renders in whatever is applied.</summary>
    public IReadOnlyList<string> SystemFontFamilies => FontResolver.EnumerateSystemFontFamilies();

    private void RebuildFontCards()
    {
        FontCards.Clear();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (SettingOption o in BuiltInFonts)
        {
            if (seen.Add(o.Value))
            {
                FontCards.Add(new FontCardViewModel(o.Label, o.Value, o.Value == DefaultFontFamily));
            }
        }

        foreach (string name in FontResolver.EnumerateUserFontFamilies())
        {
            if (seen.Add(name))
            {
                FontCards.Add(new FontCardViewModel(name, name, isDefault: false));
            }
        }

        SyncFontSelection();
    }

    private void SyncFontSelection()
    {
        string current = _settings.FontFamily;
        foreach (FontCardViewModel c in FontCards)
        {
            c.IsSelected = string.Equals(c.Value, current, StringComparison.Ordinal);
        }

        OnPropertyChanged(nameof(CardFontSelection));
        OnPropertyChanged(nameof(SystemFontSelection));
        OnPropertyChanged(nameof(CurrentFontPreview));
        OnPropertyChanged(nameof(CurrentFontSample));
        OnPropertyChanged(nameof(CurrentFontStatus));
    }

    private bool IsCardFont(string name) => FontCards.Any(c => string.Equals(c.Value, name, StringComparison.Ordinal));

    /// <summary>
    /// Card-grid selection. Deliberately NOT bound straight to <see cref="FontFamily"/>: the card list and the
    /// system dropdown are two <c>Selector</c>s over the same setting, and a Selector whose bound value is absent
    /// from ITS list coerces to null and writes that null back (SelectedValue is TwoWay by default). Routing each
    /// through its own property means "the other one owns the value" shows as an empty selection instead of
    /// wiping the setting.
    /// </summary>
    public string? CardFontSelection
    {
        get => IsCardFont(_settings.FontFamily) ? _settings.FontFamily : null;
        set
        {
            if (!string.IsNullOrEmpty(value))
            {
                FontFamily = value;
            }
        }
    }

    /// <summary>System-dropdown selection. Same null-coercion reasoning as <see cref="CardFontSelection"/>.</summary>
    public string? SystemFontSelection
    {
        get => IsCardFont(_settings.FontFamily) ? null : _settings.FontFamily;
        set
        {
            if (!string.IsNullOrEmpty(value))
            {
                FontFamily = value;
            }
        }
    }

    /// <summary>The applied font, resolved — so the 현재 글꼴 row previews a system pick too, not just cards.</summary>
    public System.Windows.Media.FontFamily CurrentFontPreview => FontResolver.Resolve(_settings.FontFamily);

    public string CurrentFontSample =>
        GlyphFallback.CanRender(_settings.FontFamily, "가") ? FontCardViewModel.Sample : "Waffle 1,234";

    public string CurrentFontStatus
    {
        get
        {
            string name = _settings.FontFamily;
            string where = FontResolver.Classify(name) switch
            {
                FontResolver.FontOrigin.Bundled => "번들 글꼴",
                FontResolver.FontOrigin.User => "사용자 추가 글꼴",
                _ => "시스템 글꼴",
            };
            string hangul = GlyphFallback.CanRender(name, "가") ? string.Empty : " · 한글 미지원(이름은 맑은 고딕으로 대체)";
            return $"현재 글꼴 — {name} ({where}){hangul}";
        }
    }

    /// <summary>Copy a user-picked font file into the fonts folder, add it to the picker, and select+apply it.
    /// Returns false if the file can't be read as a font (the caller shows a message). The font renders live via
    /// FontResolver — no restart needed — and persists (the folder is the store).</summary>
    public bool AddCustomFont(string sourcePath)
    {
        string? family = FontResolver.InstallUserFont(sourcePath);
        if (string.IsNullOrWhiteSpace(family))
        {
            return false;
        }

        // Re-adding a DIFFERENT file under a family name already asked about would otherwise keep serving the
        // memoised old face. Adding a genuinely new name is safe on its own, but this is the cheap side.
        GlyphFallback.InvalidateCache();
        RebuildFontCards();      // the grid now includes the new font...
        FontFamily = family;     // ...so selecting it lands on a real card, and applies it live
        OnPropertyChanged(nameof(FontFamilies));
        return true;
    }

    // ---- display tab (live) ----
    public string DisplayMode { get => _settings.DisplayMode; set { _settings.DisplayMode = value; OnPropertyChanged(); } }
    public string DamageValueMode { get => _settings.DamageValueMode; set { _settings.DamageValueMode = value; OnPropertyChanged(); } }
    public string ContributionMode { get => _settings.ContributionMode; set { _settings.ContributionMode = value; OnPropertyChanged(); } }
    public string NameDisplay { get => _settings.NameDisplay; set { _settings.NameDisplay = value; OnPropertyChanged(); } }
    /// <summary>The applied meter font. The setter drops null/empty on purpose — see
    /// <see cref="CardFontSelection"/> for why two Selectors over one setting would otherwise erase it.</summary>
    public string FontFamily
    {
        get => _settings.FontFamily;
        set
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            _settings.FontFamily = value;
            OnPropertyChanged();
            SyncFontSelection();
        }
    }
    public int RowHeight { get => _settings.RowHeight; set { _settings.RowHeight = value; OnPropertyChanged(); } }

    /// <summary>미터 전체 크기 배율(퍼센트 문자열, ComboBox SelectedValue용). 설정은 int로 저장된다.</summary>
    public string MeterScalePercent
    {
        get => _settings.MeterScalePercent.ToString(CultureInfo.InvariantCulture);
        set
        {
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int p))
            {
                _settings.MeterScalePercent = p;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>미터 크기 배율 선택지(퍼센트). 값이 문자열이라 <see cref="MeterScalePercent"/>와 짝이 맞는다.</summary>
    public IReadOnlyList<SettingOption> MeterScales { get; } = new[]
    {
        new SettingOption("매우 작게 (75%)", "75"),
        new SettingOption("작게 (85%)", "85"),
        new SettingOption("보통 (100%)", "100"),
        new SettingOption("크게 (115%)", "115"),
        new SettingOption("아주 크게 (130%)", "130"),
    };

    /// <summary>현재 주 모니터 해상도 + 권장 배율 힌트("현재 화면 2560×1440 · 권장 100%"). 감지 실패 시 빈 문자열.</summary>
    public string MeterScaleHint { get; } = BuildScaleHint();

    private static string BuildScaleHint()
    {
        try
        {
            System.Windows.Forms.Screen? s = System.Windows.Forms.Screen.PrimaryScreen;
            if (s is null)
            {
                return string.Empty;
            }

            int w = s.Bounds.Width, h = s.Bounds.Height;
            string rec = h <= 1080 ? "권장 85~100%" : h <= 1440 ? "권장 100%" : "권장 115~130%";
            return $"현재 화면 {w}×{h} · {rec}";
        }
        catch
        {
            return string.Empty;
        }
    }
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

    // ---- game optimization tab (게임 최적화 · Engine.ini) ----
    private string _gpuText = string.Empty;
    /// <summary>감지된 그래픽카드 + VRAM 요약(감지 실패 시 안내 문구).</summary>
    public string GpuText { get => _gpuText; private set => Set(ref _gpuText, value); }

    private string _gameOptTierText = string.Empty;
    /// <summary>VRAM으로 정해진 적용 프로필(티어) 요약.</summary>
    public string GameOptTierText { get => _gameOptTierText; private set => Set(ref _gameOptTierText, value); }

    private string _gameOptStatus = string.Empty;
    /// <summary>현재 Engine.ini에 우리 블록이 들어 있는지("적용됨"/"미적용").</summary>
    public string GameOptStatus { get => _gameOptStatus; private set => Set(ref _gameOptStatus, value); }

    private bool _gameRunning;
    /// <summary>아이온2 실행 중 — 적용/되돌리기 전에 종료하라는 경고를 XAML에서 BoolToVis로 표시.</summary>
    public bool GameRunning { get => _gameRunning; private set => Set(ref _gameRunning, value); }

    private EngineIniOptimizer.Tier _gameOptTier;

    /// <summary>GPU·적용 상태·게임 실행 여부를 다시 읽어 표시를 갱신(탭 열 때 + 적용/되돌리기 후).</summary>
    public void RefreshGameOpt()
    {
        GameOptimizerService.Gpu gpu = _gameOpt.DetectGpu();
        _gameOptTier = EngineIniOptimizer.TierForVram(gpu.VramBytes);
        if (gpu.VramBytes > 0)
        {
            double gb = gpu.VramBytes / (1024.0 * 1024 * 1024);
            GpuText = $"{gpu.Name} · VRAM {gb:0.#}GB";
        }
        else
        {
            GpuText = "그래픽카드 VRAM을 감지하지 못했습니다 — 가장 안전한 설정으로 적용됩니다.";
        }

        GameOptTierText = $"프로필 {_gameOptTier.Label} · 스트리밍 풀 {_gameOptTier.PoolMiB}MB";
        GameOptStatus = _gameOpt.IsApplied() ? "현재 상태: 적용됨" : "현재 상태: 미적용";
        GameRunning = _gameOpt.IsGameRunning();
    }

    /// <summary>감지된 프로필로 Engine.ini에 최적화를 적용/갱신한다(게임 재실행 후 반영).</summary>
    public void ApplyGameOpt()
    {
        try
        {
            _gameOpt.Apply(_gameOptTier);
        }
        catch
        {
            // 실패는 아래 RefreshGameOpt의 "미적용" 표시로 드러난다
        }

        RefreshGameOpt();
    }

    /// <summary>우리가 추가한 블록만 제거한다(사용자의 다른 Engine.ini 설정은 유지).</summary>
    public void RevertGameOpt()
    {
        try
        {
            _gameOpt.Revert();
        }
        catch
        {
            // no-op; 상태는 RefreshGameOpt가 반영
        }

        RefreshGameOpt();
    }
    public string BarStyle { get => _settings.BarStyle; set { _settings.BarStyle = value; OnPropertyChanged(); OnPropertyChanged(nameof(GaugeSkinApplicable)); } }
    public bool IsMinimal { get => _settings.IsMinimal; set { _settings.IsMinimal = value; OnPropertyChanged(); } }
    public bool ShowCombatTimerInMinimal { get => _settings.ShowCombatTimerInMinimal; set { _settings.ShowCombatTimerInMinimal = value; OnPropertyChanged(); } }
    public bool ShowTargetInfoInMinimal { get => _settings.ShowTargetInfoInMinimal; set { _settings.ShowTargetInfoInMinimal = value; OnPropertyChanged(); } }
    public bool ShowServerTag { get => _settings.ShowServerTag; set { _settings.ShowServerTag = value; OnPropertyChanged(); } }

    public string TierEffects
    {
        get => _settings.TierEffects;
        set
        {
            _settings.TierEffects = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TierDetailEnabled));
        }
    }

    /// <summary>The per-row toggles only mean something while the decoration is on at all.</summary>
    public bool TierDetailEnabled => _settings.TierEffects != "off";

    public bool TierShowOthers { get => _settings.TierShowOthers; set { _settings.TierShowOthers = value; OnPropertyChanged(); } }

    public bool TierShowSelfChip { get => _settings.TierShowSelfChip; set { _settings.TierShowSelfChip = value; OnPropertyChanged(); } }

    // ---- 닉네임 효과 (후원자 · 랭커) ----

    public IReadOnlyList<SettingOption> NameFxModes { get; } = new[]
    {
        new SettingOption("끔", "off"),
        new SettingOption("색상만 (움직임 없음)", "static"),
        new SettingOption("애니메이션", "animated"),
    };

    public string NameFxMode
    {
        get => _settings.NameFxMode;
        set
        {
            _settings.NameFxMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(NameFxDetailEnabled));
            OnPropertyChanged(nameof(NameFxAnimated));
            SyncNameFxPreview();
        }
    }

    // ---- 내 연출 고르기 ----

    private IReadOnlyList<NameFxChoiceViewModel> _myEffectChoices = Array.Empty<NameFxChoiceViewModel>();

    /// <summary>내 캐릭터가 고를 수 있는 닉네임 효과. 자격이 없으면 빈 목록이다.</summary>
    public IReadOnlyList<NameFxChoiceViewModel> MyEffectChoices
    {
        get => _myEffectChoices;
        private set => Set(ref _myEffectChoices, value);
    }

    private IReadOnlyList<NameFxChoiceViewModel> _myGaugeChoices = Array.Empty<NameFxChoiceViewModel>();

    /// <summary>랭커 자격이 있을 때만 채워진다.</summary>
    public IReadOnlyList<NameFxChoiceViewModel> MyGaugeChoices
    {
        get => _myGaugeChoices;
        private set => Set(ref _myGaugeChoices, value);
    }

    private string _myFxStatus = string.Empty;

    public string MyFxStatus { get => _myFxStatus; private set => Set(ref _myFxStatus, value); }

    /// <summary>자격이 없으면 못 고르게만 한다 — 섹션을 숨기면 "받으면 뭐가 생기는지" 를 알 수 없다.</summary>
    public bool MyFxEnabled => MyEffectChoices.Count > 0;

    /// <summary>못 고르는 상태라는 걸 눈으로도 알 수 있게. IsEnabled 만으로는 버튼 색이 크게 안 죽는다.</summary>
    public double MyFxOpacity => MyFxEnabled ? 1.0 : 0.45;

    public Visibility MyGaugeVisibility => MyGaugeChoices.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// 내 캐릭터의 자격을 읽어 선택지를 만든다.
    /// <para>자격 판정은 서버가 이미 내려 명단의 <c>k</c> 에 실어 보낸다 — 미터는 그 값만 읽는다.
    /// 여기에 "누가 무슨 자격인가" 규칙을 두면 서버와 두 벌이 되고, 갈리는 순간 사용자는 고를 수 있는데
    /// 저장이 거부되는 상태를 만난다.</para>
    /// </summary>
    public void RefreshMyNameFx()
    {
        StatsOwnCharacter own = _services.StatsBuilder.OwnCharacter();
        string? hash = own.Detected ? StatsIdentity.CharacterIdentityHash(own.Server, own.Nickname) : null;
        NameFxEntry? grant = hash is null ? null : _services.NameFx.Roster.Find(hash);

        if (grant is null)
        {
            // 고를 수 있는 게 없어도 목록은 비운 채로 두고 섹션은 남긴다. 왜 못 고르는지는 상태줄이
            // 말한다 — 인식 전인지, 자격이 없는지는 사용자가 할 일이 다르다.
            MyEffectChoices = Array.Empty<NameFxChoiceViewModel>();
            MyGaugeChoices = Array.Empty<NameFxChoiceViewModel>();
            MyFxStatus = own.Detected
                ? "지금 캐릭터에는 스킨 자격이 없습니다. 위 안내를 참고해 주세요."
                : "캐릭터를 인식하면 내 스킨을 고를 수 있습니다.";
            RaiseMyNameFxState();
            return;
        }

        bool light = _skin.IsLight;
        MyEffectChoices = NameFxPalette.ChoicesFor(grant.Kind)
            .Select(e => new NameFxChoiceViewModel(e.Id, e.Name, NameFxPalette.For(e.Id, light).NameFill,
                string.Equals(e.Id, grant.EffectId, StringComparison.Ordinal)))
            .ToArray();
        MyGaugeChoices = NameFxPalette.GaugeChoicesFor(grant.Kind)
            .Select(e => new NameFxChoiceViewModel(e.Id, e.Name, NameFxPalette.For(e.Id, light).NameFill,
                string.Equals(e.Id, grant.GaugeId, StringComparison.Ordinal)))
            .ToArray();
        MyFxStatus = grant.Kind switch
        {
            "both" => $"{own.Nickname} — 후원자 · 랭커. 고르면 다른 사람에게도 그대로 보입니다.",
            "ranker" => $"{own.Nickname} — 랭커. 고르면 다른 사람에게도 그대로 보입니다.",
            _ => $"{own.Nickname} — 후원자. 고르면 다른 사람에게도 그대로 보입니다.",
        };
        RaiseMyNameFxState();
    }

    /// <summary>응답 본문의 <c>error</c> 코드. 못 읽으면 빈 문자열 — 그 경우 상태 코드만으로 판단한다.</summary>
    public static string ErrorCodeOf(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return string.Empty;
        }

        try
        {
            using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(responseBody);
            return doc.RootElement.TryGetProperty("error", out System.Text.Json.JsonElement e)
                ? e.GetString() ?? string.Empty
                : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>지금 적용된 닉네임 효과 id. 게이지만 바꿀 때 효과를 같이 보내야 해서 필요하다.</summary>
    public string? CurrentEffectId => MyEffectChoices.FirstOrDefault(c => c.IsCurrent)?.Id;

    private void RaiseMyNameFxState()
    {
        OnPropertyChanged(nameof(MyFxEnabled));
        OnPropertyChanged(nameof(MyFxOpacity));
        OnPropertyChanged(nameof(MyGaugeVisibility));
    }

    /// <summary>네트워크를 탄다. 호출부가 UI 스레드를 벗어나서 부른다.</summary>
    public string SubmitMyNameFx(string effectId, string? gaugeId)
    {
        StatsOwnCharacter own = _services.StatsBuilder.OwnCharacter();
        string? hash = own.Detected ? StatsIdentity.CharacterIdentityHash(own.Server, own.Nickname) : null;
        if (hash is null)
        {
            return "캐릭터가 인식되지 않았습니다.";
        }

        if (!MyFxEnabled)
        {
            // 버튼이 비활성이라 정상 경로로는 닿지 않지만, 자격 없는 요청을 서버까지 보내 401/403 을
            // 받아 오는 건 사용자에게 아무 도움이 안 된다.
            return "지금 캐릭터에는 스킨 자격이 없습니다.";
        }

        try
        {
            NameFxChoiceResponse response = _services.NameFx.SubmitChoice(hash, effectId, gaugeId, _services.Version);
            return response.Ok ? "적용했습니다." : "서버가 이 선택을 거절했습니다.";
        }
        catch (StatsApiException ex)
        {
            // ⚠ 상태 코드만 보면 안 된다. 403 이 두 가지다 — 소유 증명 실패(not_your_character)와
            // 자격 없음(not_entitled). 서버가 둘을 같은 코드로 주는 건 의도된 것이고(자격 유무 자체가
            // 정보라 구분해 주면 열거 오라클이 된다), 대신 본문의 error 로 갈린다.
            //
            // 이게 닿는 실제 경로: 명단은 최대 한 시간 낡는다. 그 사이 부여가 회수되면 미터는 아직
            // 선택 버튼을 보여 주고, 누르면 not_entitled 가 온다. 그때 "전투를 올린 기록이 없다" 고
            // 말하면 사실이 아닌 데다 사용자를 엉뚱한 곳으로 보낸다.
            string code = ErrorCodeOf(ex.ResponseBody);
            return (ex.StatusCode, code) switch
            {
                (401, _) => "설치 서명이 확인되지 않았습니다. 전투를 한 번 업로드한 뒤 다시 시도해 주세요.",
                (403, "not_your_character") => "이 PC 에서 그 캐릭터로 전투를 올린 기록이 없어 변경할 수 없습니다.",
                (403, _) => "이 캐릭터에는 지금 스킨 자격이 없습니다. 목록을 새로고침해 주세요.",
                (400, _) => "지금 자격으로 고를 수 없는 스킨입니다. 목록을 새로고침해 주세요.",
                _ => "지금은 변경할 수 없습니다. 잠시 뒤 다시 시도해 주세요.",
            };
        }
        catch
        {
            return "지금은 변경할 수 없습니다. 잠시 뒤 다시 시도해 주세요.";
        }
    }

    public IReadOnlyList<SettingOption> NameFxScopes { get; } = new[]
    {
        new SettingOption("명단에 있는 모든 캐릭터", "all"),
        new SettingOption("내 캐릭터만", "self"),
    };

    /// <summary>
    /// 연출을 누구에게 그릴지.
    /// <para>부여는 캐릭터에 붙고 명단은 모두가 같은 것을 받으므로, 같은 전투에 있는 다른 미터
    /// 사용자에게도 그 사람이 고른 연출이 그대로 보인다. 이 설정은 <b>보는 쪽</b>의 취향일 뿐이고,
    /// 남에게 무엇이 보이는지는 바꾸지 않는다.</para>
    /// <para>기존 두 토글(<c>nameFx.showSelf</c>·<c>nameFx.showOthers</c>) 위에 얹는다. 새 키를
    /// 만들지 않는 이유는 설정 백업·취소 스냅샷·키 카탈로그가 전부 그 두 키를 이미 알고 있어서다 —
    /// 키를 하나 더 만들면 세 곳이 같이 늘어나고, 그 중 하나를 빠뜨리는 게 이 파일의 단골 결함이다.</para>
    /// </summary>
    public string NameFxScope
    {
        get => _settings.NameFxShowOthers ? "all" : "self";
        set
        {
            bool all = value != "self";
            // 내 행은 항상 켠다. "내 캐릭터만"에서 내 것마저 꺼지면 고를 이유가 없는 상태가 된다.
            _settings.NameFxShowSelf = true;
            _settings.NameFxShowOthers = all;
            OnPropertyChanged();
        }
    }

    /// <summary>The per-row toggles and the brightness slider only mean something while effects are on at all.</summary>
    public bool NameFxDetailEnabled => _settings.NameFxMode != "off";

    /// <summary>Speed only applies to the moving variants.</summary>
    public bool NameFxAnimated => _settings.NameFxMode == "animated";

    public bool NameFxShowSelf { get => _settings.NameFxShowSelf; set { _settings.NameFxShowSelf = value; OnPropertyChanged(); } }

    public bool NameFxShowOthers { get => _settings.NameFxShowOthers; set { _settings.NameFxShowOthers = value; OnPropertyChanged(); } }

    public int NameFxSpeedPercent
    {
        get => _settings.NameFxSpeedPercent;
        set { _settings.NameFxSpeedPercent = value; OnPropertyChanged(); SyncNameFxPreview(); }
    }

    /// <summary>
    /// Brightness. The setter only stores; rebuilding the shared brushes is <see cref="CommitNameFxBrightness"/>,
    /// called on drag-end — recolouring every stop on each slider tick would rebuild the palette dozens of times
    /// per drag AND rewrite the whole properties file each time.
    /// </summary>
    public int NameFxBrightnessPercent
    {
        get => _settings.NameFxBrightnessPercent;
        set { _settings.NameFxBrightnessPercent = value; OnPropertyChanged(); }
    }

    public void CommitNameFxBrightness() => NameFxSheen.Rebuild(_settings.NameFxBrightnessPercent);

    /// <summary>
    /// Keep the preview strip moving while it is on screen. The sweep timer is demand-driven from the meter's
    /// row rebuild, and a user deciding about this setting has no decorated row anywhere — so without a second
    /// demand source the strip renders frozen and the animation setting cannot be judged at all.
    /// <para>Gated on the colour tab being selected AND the mode being "animated", so the preview tells the
    /// truth: switching to "색상만" visibly stops it, which is exactly what that option does.</para>
    /// </summary>
    private void SyncNameFxPreview()
    {
        // Low-spec is checked HERE too, not only on the row path. The brushes are process-wide singletons, so a
        // preview that grabbed the clock would animate the meter's own rows as well — which is precisely what
        // low-spec mode exists to prevent.
        NameFxSheen.SetLowSpec(_settings.LowSpecMode);
        NameFxSheen.SetPreviewDemand(
            string.Equals(SelectedNav, "theme", StringComparison.Ordinal) && _settings.NameFxMode == "animated",
            _settings.NameFxSpeedPercent);
    }

    /// <summary>Drop the preview's claim on the sweep timer when the settings window closes.</summary>
    public void StopNameFxPreview() => NameFxSheen.SetPreviewDemand(false, _settings.NameFxSpeedPercent);

    /// <summary>
    /// Every catalogue effect, drawn on a sample nickname. Grants come from the server, so without this a user
    /// has no way to see what the setting even does — and the preview is also how someone decides whether the
    /// motion bothers them before turning it off.
    /// </summary>
    public IReadOnlyList<NameFxSampleViewModel> NameFxSamples { get; private set; } = Array.Empty<NameFxSampleViewModel>();

    /// <summary>랭커 전용 DPS 게이지 스킨 미리보기. 닉네임 연출과 목록을 나눈 이유는 칠하는 자리가 달라서다 —
    /// 하나는 글자, 하나는 행을 가로지르는 막대다.</summary>
    public IReadOnlyList<GaugeSkinSampleViewModel> GaugeSkinSamples { get; private set; } = Array.Empty<GaugeSkinSampleViewModel>();

    public bool NameFxGauge { get => _settings.NameFxGauge; set { _settings.NameFxGauge = value; OnPropertyChanged(); } }

    /// <summary>게이지 형태가 '없음'이면 칠할 막대가 존재하지 않는다 — 토글을 켜 봐야 아무 일도 일어나지
    /// 않으므로 비활성화하고 이유를 밝힌다.</summary>
    public bool GaugeSkinApplicable => _settings.BarStyle != "none";

    private void RebuildNameFxSamples(bool isLight)
    {
        NameFxSamples = NameFxPalette.NameEffects
            .Select(e => new NameFxSampleViewModel(
                e.Name,
                e.Kind == NameFxPalette.NameFxKind.Ranker ? "랭커" : "후원자",
                NameFxPalette.For(e.Id, isLight).NameFill))
            .ToArray();
        // 같은 행 내용에 스킨만 갈아 끼운다 — 비교해야 할 변수가 스킨 하나뿐이어야 한다.
        GaugeSkinSamples = NameFxPalette.GaugeSkins
            .Select(e => new GaugeSkinSampleViewModel(
                Name: e.Name,
                Fill: NameFxPalette.For(e.Id, isLight).NameFill,
                RailBrush: OverlayViewModel.RowGradient(Theme.UserBarFrom, Theme.UserBarTo),
                IconSource: JoinIcons.Job("마도성"),
                Rank: "1",
                Nickname: "와플장인",
                ServerTag: "[시엘]",
                PowerText: "656.0k",
                DpsText: "408,239/s",
                PercentText: "35.1%",
                BarRatio: 0.55,
                BarRest: 0.45,
                GaugeOpacity: 0.58))
            .ToArray();
        OnPropertyChanged(nameof(NameFxSamples));
        OnPropertyChanged(nameof(GaugeSkinSamples));
    }

    private string _tierStatus = string.Empty;

    /// <summary>One line under the refresh button: what we have and how old it is.</summary>
    public string TierStatus { get => _tierStatus; private set { _tierStatus = value; OnPropertyChanged(); } }

    /// <summary>Re-read the tier service state for display. Cheap — no network.</summary>
    public void RefreshTierStatus()
    {
        TierServiceStatus status = _services.Tier.Status();
        if (!status.HasArtifact)
        {
            TierStatus = status.Failures > 0
                ? $"티어 지표를 받지 못했어요 (실패 {status.Failures}회{FormatReason(status.LastError)})"
                : "티어 지표를 받지 못했어요.";
            return;
        }

        TimeSpan age = _services.Tier.Age;
        string freshness = age.TotalHours < 1
            ? "방금 갱신"
            : age.TotalDays < 1
                ? $"{(int)age.TotalHours}시간 전 기준"
                : $"{(int)age.TotalDays}일 전 기준";
        string stale = age.TotalDays >= 14 ? " · 오래된 기준이에요" : string.Empty;
        TierStatus = $"던전 {status.Dungeons}개 · 보스 {status.Mobs}종 · 구간 {status.Rows:N0}개 · {freshness}{stale}";
    }

    /// <summary>Settings' 새로고침 button. Returns false when the 60s cooldown swallowed it.</summary>
    public bool RequestTierRefresh() => _services.Tier.RequestManualRefresh();

    private string _nameFxStatus = string.Empty;

    /// <summary>One line under the 후원자 목록 갱신 button.</summary>
    public string NameFxStatus { get => _nameFxStatus; private set => Set(ref _nameFxStatus, value); }

    private long _nameFxNoticeUntilMs;

    /// <summary>Show a message about what the button just did, and hold it against the 2.5s status poll.
    /// Without the hold the poll overwrites it before it can be read, which is how a button ends up looking
    /// dead in exactly the case it is trying to explain.</summary>
    public void SetNameFxNotice(string message, int holdMs = 4000)
    {
        NameFxStatus = message;
        _nameFxNoticeUntilMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + holdMs;
    }

    /// <summary>Re-read the grant-list state for display. Cheap — no network.</summary>
    public void RefreshNameFxStatus()
    {
        if (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() < _nameFxNoticeUntilMs)
        {
            return;
        }

        NameFxServiceStatus status = _services.NameFx.Status();
        if (status.UsingLocalFile)
        {
            NameFxStatus = $"로컬 파일에서 {status.Grants}명을 읽었습니다.";
            return;
        }

        if (!status.HasArtifact)
        {
            NameFxStatus = status.Failures > 0
                ? $"후원자 목록을 받지 못했어요 (실패 {status.Failures}회{FormatReason(status.LastError)})"
                : "아직 후원자 목록을 받지 않았어요. 미터를 켜고 잠시 뒤 자동으로 받아 옵니다.";
            return;
        }

        long age = Math.Max(0, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - status.FetchedAtMs);
        var span = TimeSpan.FromMilliseconds(age);
        string freshness = span.TotalHours < 1
            ? "방금 갱신"
            : span.TotalDays < 1
                ? $"{(int)span.TotalHours}시간 전 갱신"
                : $"{(int)span.TotalDays}일 전 갱신";
        NameFxStatus = $"연출이 적용된 캐릭터 {status.Grants}명 · {freshness}";
    }

    /// <summary>Settings' 후원자 목록 갱신 button. Returns false when the 60s cooldown swallowed it.</summary>
    public bool RequestNameFxRefresh() => _services.NameFx.RequestManualRefresh();

    private static string FormatReason(string? reason) => string.IsNullOrEmpty(reason) ? string.Empty : $", {reason}";
    public bool ShowAetherStatus { get => _settings.ShowAetherStatus; set { _settings.ShowAetherStatus = value; OnPropertyChanged(); } }
    public bool ShowLatencyIndicator { get => _settings.ShowLatencyIndicator; set { _settings.ShowLatencyIndicator = value; OnPropertyChanged(); } }
    public bool VrrCompatMode { get => _settings.VrrCompatMode; set { _settings.VrrCompatMode = value; OnPropertyChanged(); } }
    public bool ShowBuffUi { get => _settings.ShowBuffUi; set { _settings.ShowBuffUi = value; OnPropertyChanged(); } }
    public bool BuffUiTransparent { get => _settings.BuffUiTransparent; set { _settings.BuffUiTransparent = value; OnPropertyChanged(); } }
    /// <summary>Buff icon size as a 0/1 ComboBox index: 0 = 작게 (34px), 1 = 크게 (40px).</summary>
    public int BuffIconSizeIndex
    {
        get => _settings.BuffUiIconSize <= 34 ? 0 : 1;
        set { _settings.BuffUiIconSize = value == 0 ? 34 : 40; OnPropertyChanged(); }
    }

    /// <summary>Buff overlay countdown-text color (hex), bound to the color-swatch picker.</summary>
    public string BuffTextColor { get => _settings.BuffUiTextColor; set { _settings.BuffUiTextColor = value; OnPropertyChanged(); } }
    public bool BuffTtsOnStart { get => _settings.BuffTtsOnStart; set { _settings.BuffTtsOnStart = value; OnPropertyChanged(); } }
    public bool BuffTtsOnEnd { get => _settings.BuffTtsOnEnd; set { _settings.BuffTtsOnEnd = value; OnPropertyChanged(); } }
    public bool BuffUiGrayOnCooldown { get => _settings.BuffUiGrayOnCooldown; set { _settings.BuffUiGrayOnCooldown = value; OnPropertyChanged(); } }
    public bool ShowOtherPlayerBuffs { get => _settings.ShowOtherPlayerBuffs; set { _settings.ShowOtherPlayerBuffs = value; OnPropertyChanged(); } }

    /// <summary>버프 오버레이 정렬 모드를 ComboBox의 SelectedIndex(0 적용순 / 1 남은시간순 / 2 이름순)로
    /// 노출한다. 맨 앞 고정한 버프는 이 모드와 무관하게 항상 앞에 온다.</summary>
    public int BuffUiSortModeIndex
    {
        get => _settings.BuffUiSortMode switch
        {
            BuffOverlayOrder.Remaining => 1,
            BuffOverlayOrder.Name => 2,
            _ => 0,
        };
        set
        {
            _settings.BuffUiSortMode = value switch
            {
                1 => BuffOverlayOrder.Remaining,
                2 => BuffOverlayOrder.Name,
                _ => BuffOverlayOrder.Applied,
            };
            OnPropertyChanged();
        }
    }

    private BuffPickerViewModel? _buffPicker;
    /// <summary>The per-job buff picker, embedded in the 버프 알림 settings tab. Built lazily and disposed when
    /// the window closes (see <see cref="DisposeBuffPicker"/>).</summary>
    public BuffPickerViewModel BuffPicker => _buffPicker ??= new BuffPickerViewModel(_services.Data, _settings);

    // ---- buff presets (three saved copies of the whole buff config; the active one IS the live settings) ----

    /// <summary>The preset chips, shown on both the 전투 보조 and 버프 알림 tabs.</summary>
    public ObservableCollection<BuffPresetSlotViewModel> PresetSlots { get; } = new();

    /// <summary>The active slot's name, edited inline. Blank falls back to "프리셋 N".</summary>
    public string ActivePresetName
    {
        get => _presets.ActiveName;
        set
        {
            _presets.RenameSlot(_presets.ActiveIndex, value);
            SyncPresetSlots();
            OnPropertyChanged();
        }
    }

    /// <summary>Apply a preset: push the slot into the live settings + the buff store, resync the picker, and
    /// re-announce every bound buff property. That last step is not optional — <c>MeterSettings</c> setters
    /// no-op on an unchanged value, so the manager's writes cannot be relied on to refresh these controls.</summary>
    public void SelectPreset(int index)
    {
        _presets.SelectSlot(index);
        _buffPicker?.Reload(); // else its stale cached sets would overwrite the preset on the next edit
        SyncPresetSlots();

        OnPropertyChanged(nameof(BuffUiTransparent));
        OnPropertyChanged(nameof(BuffIconSizeIndex));
        OnPropertyChanged(nameof(BuffTextColor));
        OnPropertyChanged(nameof(BuffTtsOnStart));
        OnPropertyChanged(nameof(BuffTtsOnEnd));
        OnPropertyChanged(nameof(BuffUiGrayOnCooldown));
        OnPropertyChanged(nameof(ShowOtherPlayerBuffs));
        OnPropertyChanged(nameof(ActivePresetName));
    }

    private void SyncPresetSlots()
    {
        IReadOnlyList<string> names = _presets.Names;
        foreach (BuffPresetSlotViewModel slot in PresetSlots)
        {
            slot.Name = names[slot.Index];
            slot.SyncActive(slot.Index == _presets.ActiveIndex);
        }
    }

    /// <summary>Release the picker's catalog subscription when the settings window closes.</summary>
    public void DisposeBuffPicker() => _buffPicker?.Dispose();

    /// <summary>Wired by App: trigger an update check (results surface in the toast).</summary>
    public Action? CheckUpdateRequested { get; set; }
    public void CheckForUpdate() => CheckUpdateRequested?.Invoke();

    /// <summary>Wired by App: reset a panel position ("meter" / "join" / "history").</summary>
    public Action<string>? ResetPositionRequested { get; set; }
    public void ResetMeterPosition() => ResetPositionRequested?.Invoke("meter");
    public void ResetJoinPosition() => ResetPositionRequested?.Invoke("join");
    public void ResetHistoryPosition() => ResetPositionRequested?.Invoke("history");
    public void ResetAetherPosition() => ResetPositionRequested?.Invoke("aether");

    // ---- overlay tab (live) ----
    public double MeterOpacity { get => _settings.MeterOpacity; set { _settings.MeterOpacity = value; OnPropertyChanged(); } }
    public bool MultiMonitorMode { get => _settings.MultiMonitorMode; set { _settings.MultiMonitorMode = value; OnPropertyChanged(); } }
    public bool ShowJoinPanel { get => _settings.ShowJoinPanel; set { _settings.ShowJoinPanel = value; OnPropertyChanged(); } }
    public bool ShowPreCombatRoster { get => _settings.ShowPreCombatRoster; set { _settings.ShowPreCombatRoster = value; OnPropertyChanged(); } }
    public bool ForceInstanceTracking { get => _settings.ForceInstanceTracking; set { _settings.ForceInstanceTracking = value; OnPropertyChanged(); } }
    // (Light mode is now a skin — "light" in the Skin list — not a separate overlayTheme toggle.)

    // ---- alarms (live; persisted immediately, not part of the Cancel snapshot) ----
    public bool ShugoAlarmEnabled { get => _settings.ShugoAlarmEnabled; set { _settings.ShugoAlarmEnabled = value; OnPropertyChanged(); } }
    public bool ShugoLead10 { get => _settings.ShugoLead10; set { _settings.ShugoLead10 = value; OnPropertyChanged(); } }
    public bool ShugoLead5 { get => _settings.ShugoLead5; set { _settings.ShugoLead5 = value; OnPropertyChanged(); } }
    public bool ShugoLead1 { get => _settings.ShugoLead1; set { _settings.ShugoLead1 = value; OnPropertyChanged(); } }
    public bool ShugoLeadStart { get => _settings.ShugoLeadStart; set { _settings.ShugoLeadStart = value; OnPropertyChanged(); } }
    public bool AlarmSoundEnabled { get => _settings.AlarmSoundEnabled; set { _settings.AlarmSoundEnabled = value; OnPropertyChanged(); } }
    public bool TtsEnabled { get => _settings.TtsEnabled; set { _settings.TtsEnabled = value; OnPropertyChanged(); } }
    public double AlarmVolume { get => _settings.AlarmVolume; set { _settings.AlarmVolume = value; OnPropertyChanged(); } }

    /// <summary>Settings "소리 테스트" button: play the alarm chime at the current volume.</summary>
    public void TestAlarmSound() => AlarmSound.Play(_settings.AlarmVolume);

    /// <summary>Settings "음성 테스트" button: speak a sample line (falls back to the chime if TTS fails).</summary>
    public void TestTts() => TtsSpeech.Speak("슈고 페스타. 5분 뒤 시작합니다.", _settings.AlarmVolume);

    // ---- field-boss respawn reminder ----
    public bool FieldBossAlarmEnabled { get => _settings.FieldBossAlarmEnabled; set { _settings.FieldBossAlarmEnabled = value; OnPropertyChanged(); } }
    public bool FieldBossLead5 { get => _settings.FieldBossLead5; set { _settings.FieldBossLead5 = value; OnPropertyChanged(); } }
    public bool FieldBossLead10 { get => _settings.FieldBossLead10; set { _settings.FieldBossLead10 = value; OnPropertyChanged(); } }
    public bool FieldBossLead30 { get => _settings.FieldBossLead30; set { _settings.FieldBossLead30 = value; OnPropertyChanged(); } }
    public bool FieldBossAlarmMuteInCombat { get => _settings.FieldBossAlarmMuteInCombat; set { _settings.FieldBossAlarmMuteInCombat = value; OnPropertyChanged(); } }

    public bool KairaAlarmEnabled { get => _settings.KairaAlarmEnabled; set { _settings.KairaAlarmEnabled = value; OnPropertyChanged(); } }
    public bool KairaLead10 { get => _settings.KairaLead10; set { _settings.KairaLead10 = value; OnPropertyChanged(); } }
    public bool KairaLead5 { get => _settings.KairaLead5; set { _settings.KairaLead5 = value; OnPropertyChanged(); } }
    public bool KairaLead1 { get => _settings.KairaLead1; set { _settings.KairaLead1 = value; OnPropertyChanged(); } }

    /// <summary>Build the field-boss alarm selection dialog, bound to the persisted disabled set.</summary>
    public FieldBossPickerWindow CreateFieldBossPicker() => new(new FieldBossPickerViewModel(_settings));

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

    public bool KeepOverlayWhenMeterHidden
    {
        get => _controller.KeepOverlayWhenHidden;
        set { _controller.SetKeepOverlayWhenHidden(value); OnPropertyChanged(); }
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
    private HotkeyCombo? _pendingDummyToggle;
    public HotkeyCombo? PendingDummyToggle { get => _pendingDummyToggle; set => Set(ref _pendingDummyToggle, value); }
    private HotkeyCombo? _pendingDummyReset;
    public HotkeyCombo? PendingDummyReset { get => _pendingDummyReset; set => Set(ref _pendingDummyReset, value); }

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
            string counts = $"업로드 {s.Uploaded} · 대기 {s.Pending} · 건너뜀 {s.Skipped} · 실패 {s.Failed}";

            // 마지막 사유를 함께 보여준다. 큐는 사유를 코드까지 실어 만들어 두는데(예:
            // unsupported_encounter:2301059:영겁의 루드라) 지금까지 어디에도 표시되지 않아서, "안 올라가요" 제보를
            // 미동의·보스아님·카탈로그누락·전투력미해석 중 무엇인지 가를 방법이 없었다.
            return DescribeUploadReason(s.LastReason) is { } reason ? $"{counts}\n최근: {reason}" : counts;
        }
    }

    /// <summary>업로드 큐/페이로드 빌더가 남긴 마지막 사유를 한국어 한 줄로. 모르는 사유는 원문 그대로 보여준다 —
    /// 새 사유가 생겼을 때 "" 로 삼켜 버리면 진단 가치가 사라진다.</summary>
    private static string? DescribeUploadReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return null;
        }

        // 큐가 코드와 이름을 함께 싣는 형태: unsupported_encounter:<mobCode>:<보스 이름>
        if (reason.StartsWith("unsupported_encounter:", StringComparison.Ordinal))
        {
            string[] parts = reason.Split(':', 3);
            string boss = parts.Length >= 3 && !string.IsNullOrWhiteSpace(parts[2]) ? parts[2] : "이 보스";
            return $"{boss}는 아직 통계 대상 던전이 아닙니다";
        }

        if (reason.StartsWith("upload_failed:", StringComparison.Ordinal))
        {
            return $"전송 실패 — {reason["upload_failed:".Length..]}";
        }

        return reason switch
        {
            "uploaded" or "uploaded_duplicate" => "정상 업로드됨",
            "consent_not_allowed" => "통계 수집에 동의하지 않아 보내지 않았습니다",
            "force_tracking_mode" => "던전 강제 집계 중에는 보내지 않습니다",
            "not_boss" or "not_uploadable_boss" => "보스 전투가 아닙니다",
            "estimated_boss" => "보스를 확정하지 못했습니다(미상 보스)",
            "not_kill" => "처치하지 못한 전투입니다",
            "duplicate" => "같은 전투가 이미 올라가 있습니다",
            "no_report_id" => "서버가 리포트 번호를 주지 않았습니다",
            "target_missing" => "대상 보스 정보가 없습니다",
            "executor_missing" or "own_character_missing" => "본인 캐릭터를 아직 인식하지 못했습니다",
            "own_nickname_missing" or "own_identity_missing" => "본인 닉네임을 확인하지 못했습니다",
            "own_result_missing" or "own_damage_empty" => "본인 딜 기록이 없는 전투입니다",
            "invalid_duration" => "전투 시간이 올바르지 않습니다",
            "own_power_unresolved" => "본인 전투력을 확인하지 못했습니다",
            "participant_power_unresolved" => "참가자 중 전투력을 확인하지 못한 사람이 있습니다",
            _ => reason,
        };
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

    /// <summary>통계 웹서비스 첫 화면 주소(설정창 하단 '통계 웹' 버튼).</summary>
    public string StatsWebUrl => _services.StatsApi.WebHomeUrl;

    // ---- per-character consent management (the 내 캐릭터 관리 list) ----
    public ObservableCollection<ConsentCharacterRow> ConsentCharacters { get; } = new();
    public bool HasConsentCharacters => ConsentCharacters.Count > 0;

    /// <summary>Rebuild the management list from the locally-remembered consented characters (current
    /// character first). Local-only + UI-thread (no network); call on open and after each action.</summary>
    public void RefreshConsentCharacters()
    {
        ConsentCharacters.Clear();
        AetherPerCharacterStore aether = AetherPerCharacterStore.Parse(_settings.AetherPerCharacter);
        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach (StatsConsentManager.CharacterConsentInfo c in _services.Consent.ListCharacters())
        {
            if (c.State != "accepted")
            {
                continue; // the management list = currently-consented characters
            }

            // Projected forward over the 자연회복 accrued since the reading was taken, exactly as the 컨텐츠 관리
            // list does — the two show the same characters and must not disagree.
            AetherSnapshot? snap = aether.Get(c.IdentityHash);
            string aetherText = string.Empty;
            if (snap is { } a)
            {
                (int aBase, int aBonus) = AetherRegen.Project(a.Base, a.Bonus, a.SavedAtMs, nowMs);
                aetherText = aBonus > 0
                    ? $"{aBase}(+{aBonus})"
                    : aBase.ToString(CultureInfo.InvariantCulture);
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
                AetherText = aetherText,
                AetherVisibility = aetherText.Length > 0 ? Visibility.Visible : Visibility.Collapsed,
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

    /// <summary>
    /// The canonical tab keys, in nav-rail order. This is the SOURCE OF TRUTH — the XAML's
    /// <c>ListBoxItem.Tag</c> and each panel's <c>ConverterParameter</c> must agree with it, and
    /// <see cref="SettingsWindow"/> checks that at construction (see <c>VerifyNavContract</c>).
    /// </summary>
    public static readonly string[] NavKeys =
    {
        "display", "theme", "window", "buffs", "alarms",
        "battle", "hotkeys", "stats", "gameopt", "advanced",
    };

    private string _selectedNav = NavKeys[0];

    /// <summary>
    /// Selected tab key. The setter ABSORBS null and unknown values instead of storing them, and that is
    /// load-bearing: the nav rail binds <c>Selector.SelectedValue</c>, which is TwoWay by default, so WPF
    /// writes <c>null</c> back the moment the bound value has no matching item — during init, and again
    /// whenever a tab is renamed or retired. A stored null makes
    /// <see cref="StringEqualsToVisibilityConverter"/> collapse EVERY panel and the right-hand side of the
    /// window goes blank with no error anywhere. Keeping the previous key is always better than that.
    /// </summary>
    public string SelectedNav
    {
        get => _selectedNav;
        set
        {
            if (value is null || Array.IndexOf(NavKeys, value) < 0)
            {
                return;
            }

            Set(ref _selectedNav, value);
            SyncNameFxPreview(); // the strip only animates while its tab is on screen
        }
    }

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
        FontFamily = DefaultFontFamily;
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
        set
        {
            _skin.Apply(value);
            OnPropertyChanged();
            RebuildNameFxSamples(_skin.IsLight); // the preview strip carries its own palette per skin
        }
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

    // ---- 설정 백업 · 공유 ----

    /// <summary>Injected by App once every collaborator exists. Null in the preview harness — the section then
    /// reports that it is unavailable rather than throwing.</summary>
    public SettingsBundleApplier? BundleApplier { get; set; }

    /// <summary>"is a fight happening right now". Import repaints every row, swaps the skin dictionary and
    /// re-registers five global hotkeys in one go — not something to do mid-pull.</summary>
    public Func<bool>? IsCombatActive { get; set; }

    private string _bundleStatus = string.Empty;

    /// <summary>One line under the buttons: what just happened.</summary>
    public string BundleStatus { get => _bundleStatus; private set => Set(ref _bundleStatus, value); }

    private string _lastExportedCode = string.Empty;

    /// <summary>The code that was just produced, shown in a read-only box. Clipboard writes can fail (another
    /// app holding the clipboard is common), so the code is always visible to copy by hand.</summary>
    public string LastExportedCode { get => _lastExportedCode; private set => Set(ref _lastExportedCode, value); }

    public Visibility UndoVisibility =>
        SettingsBackupStore.List(_services.Props.AppDirectory()).Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public void ExportFull() => Export(SettingsProfile.Full, "전체 설정");

    public void ExportDesign() => Export(SettingsProfile.Design, "디자인");

    public void ExportAlarms() => Export(SettingsProfile.Alarms, "알림");

    private void Export(SettingsProfile profile, string label)
    {
        SettingsBundle bundle = SettingsBundleBuilder.Build(_services.Props, profile, _services.Version, DateTimeOffset.Now);
        string code = SettingsBundleCodec.Encode(bundle);
        LastExportedCode = code;

        bool copied = TryCopy(code);
        // 디스코드 한 메시지가 2,000자다. 그 선을 넘으면 붙여넣기가 잘려 나가고, 잘린 코드는 지문 검사에서
        // "손상됐다"로만 보인다 — 왜 잘렸는지는 받는 쪽이 알 길이 없으므로 보내는 쪽에서 미리 말해 준다.
        string tooLong = code.Length > 1800
            ? " 채팅에 붙여넣기엔 깁니다 — '파일로 저장'으로 넘기는 편이 안전해요."
            : string.Empty;
        BundleStatus = copied
            ? $"{label} 코드를 복사했습니다 — 설정 {bundle.Data.Count}개, {code.Length}자.{tooLong}"
            : $"{label} 코드를 만들었습니다 — 설정 {bundle.Data.Count}개. 클립보드 복사에 실패해 아래 상자에서 직접 복사해 주세요.";
    }

    private static bool TryCopy(string text)
    {
        try
        {
            // SetDataObject(copy: true) so the code survives this process exiting — a plain SetText leaves the
            // clipboard owned by us and the paste fails after the app closes.
            System.Windows.Clipboard.SetDataObject(text, true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Read a code from the clipboard, if it holds one. Used to prefill the import box.</summary>
    public string? ClipboardCode()
    {
        try
        {
            return System.Windows.Clipboard.ContainsText() ? SettingsBundleCodec.Extract(System.Windows.Clipboard.GetText()) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Decode and diff, writing nothing. <paramref name="error"/> carries the reason on failure.</summary>
    public SettingsBundlePlan? PreviewImport(string? text, out string error)
    {
        error = string.Empty;
        if (!SettingsBundleCodec.TryDecode(text, out SettingsBundle bundle, out SettingsCodeError code))
        {
            error = code switch
            {
                SettingsCodeError.ChecksumMismatch =>
                    "코드가 손상됐습니다. 복사할 때 일부가 빠졌을 수 있어요 — 'WM1.' 부터 끝까지 전체를 다시 복사해 붙여넣어 주세요.",
                SettingsCodeError.FutureVersion =>
                    "더 새로운 버전에서 만든 코드입니다. 미터를 업데이트한 뒤 다시 시도해 주세요.",
                SettingsCodeError.NotFound =>
                    "설정 코드를 찾지 못했습니다. 'WM1.' 으로 시작하는 코드를 붙여넣어 주세요.",
                _ => "코드를 읽지 못했습니다. 올바른 설정 코드인지 확인해 주세요.",
            };
            return null;
        }

        return SettingsBundleBuilder.Plan(_services.Props, bundle);
    }

    /// <summary>Apply a previewed plan. Returns the user-facing result line.</summary>
    public string ApplyImport(SettingsBundlePlan plan)
    {
        if (BundleApplier is not { } applier)
        {
            return "이 빌드에서는 설정 가져오기를 쓸 수 없습니다.";
        }

        SettingsImportResult result = applier.Apply(plan, _services.Version, DateTimeOffset.Now);

        // The window's own Cancel restores a 19-value snapshot taken when it opened, so after an import it would
        // put back a mixture no backup describes. Re-take it: Cancel now means "cancel what I did after this".
        _snapshot = Snapshot.Capture(_settings, _controller);
        PendingReset = _hotkeys.Reset;
        PendingVisibility = _hotkeys.Visibility;
        PendingClickThrough = _hotkeys.ClickThrough;
        PendingDummyToggle = _hotkeys.DummyToggle;
        PendingDummyReset = _hotkeys.DummyReset;

        Reload();
        RebuildFontCards();
        RebuildNameFxSamples(_skin.IsLight);
        SyncNameFxPreview();
        NameFxSheen.Rebuild(_settings.NameFxBrightnessPercent);
        OnPropertyChanged(nameof(UndoVisibility));

        string hint = result.RestartHint ? " 일부 항목은 미터를 다시 켜야 적용됩니다." : string.Empty;
        string backup = result.BackupPath is null
            ? " ⚠ 이전 설정 백업을 저장하지 못했습니다."
            : " 적용 전 설정은 백업해 뒀습니다.";
        BundleStatus = $"설정 {plan.Changes.Count}개를 적용했습니다.{backup}{hint}";
        return BundleStatus;
    }

    /// <summary>Put back the snapshot taken before the most recent import.</summary>
    public string UndoLastImport()
    {
        string? code = SettingsBackupStore.ReadNewest(_services.Props.AppDirectory());
        if (code is null || !SettingsBundleCodec.TryDecode(code, out SettingsBundle bundle, out _))
        {
            BundleStatus = "되돌릴 백업이 없습니다.";
            return BundleStatus;
        }

        SettingsBundlePlan plan = SettingsBundleBuilder.Plan(_services.Props, bundle);
        ApplyImport(plan);
        BundleStatus = "가져오기 직전 설정으로 되돌렸습니다.";
        return BundleStatus;
    }

    private string _importText = string.Empty;

    /// <summary>What the user pasted. Not necessarily just a code — the parser cuts it out of surrounding chat.</summary>
    public string ImportText
    {
        get => _importText;
        set { Set(ref _importText, value); ClearPreview(); }
    }

    private SettingsBundlePlan? _plan;

    private IReadOnlyList<SettingsChange> _importChanges = Array.Empty<SettingsChange>();

    /// <summary>Exactly what would change, so the user agrees to a list rather than to the word "가져오기".</summary>
    public IReadOnlyList<SettingsChange> ImportChanges { get => _importChanges; private set => Set(ref _importChanges, value); }

    private string _importSummary = string.Empty;

    public string ImportSummary { get => _importSummary; private set => Set(ref _importSummary, value); }

    public Visibility ImportPreviewVisibility => _plan is null ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Blocked mid-fight: applying repaints every row, swaps the skin dictionary and re-registers five
    /// global hotkeys at once.</summary>
    public bool CanApplyImport => _plan is { HasWork: true } && IsCombatActive?.Invoke() != true;

    public string CombatBlockNotice =>
        IsCombatActive?.Invoke() == true ? "전투 중에는 적용할 수 없습니다. 전투가 끝난 뒤 눌러 주세요." : string.Empty;

    public Visibility CombatBlockVisibility =>
        IsCombatActive?.Invoke() == true ? Visibility.Visible : Visibility.Collapsed;

    private void ClearPreview()
    {
        _plan = null;
        ImportChanges = Array.Empty<SettingsChange>();
        ImportSummary = string.Empty;
        RaisePreviewState();
    }

    private void RaisePreviewState()
    {
        OnPropertyChanged(nameof(ImportPreviewVisibility));
        OnPropertyChanged(nameof(CanApplyImport));
        OnPropertyChanged(nameof(CombatBlockNotice));
        OnPropertyChanged(nameof(CombatBlockVisibility));
    }

    /// <summary>Decode + diff the pasted text. Writes nothing.</summary>
    public void PreviewPastedCode()
    {
        _plan = PreviewImport(ImportText, out string error);
        if (_plan is null)
        {
            ImportChanges = Array.Empty<SettingsChange>();
            ImportSummary = string.Empty;
            BundleStatus = error;
            RaisePreviewState();
            return;
        }

        ImportChanges = _plan.Changes;
        var bits = new List<string> { $"바뀌는 설정 {_plan.Changes.Count}개" };
        if (_plan.UnchangedCount > 0) { bits.Add($"이미 같음 {_plan.UnchangedCount}개"); }
        if (_plan.UnknownCount > 0) { bits.Add($"이 버전이 모르는 항목 {_plan.UnknownCount}개(무시)"); }
        if (_plan.MissingCount > 0) { bits.Add($"코드에 없어 그대로 두는 항목 {_plan.MissingCount}개"); }
        ImportSummary = string.Join(" · ", bits);
        BundleStatus = _plan.HasWork ? string.Empty : "이 코드는 지금 설정과 같습니다. 적용해도 바뀌는 게 없어요.";
        RaisePreviewState();
    }

    /// <summary>Apply the previewed plan.</summary>
    public void ApplyPreviewedCode()
    {
        if (_plan is null || !CanApplyImport)
        {
            return;
        }

        ApplyImport(_plan);
        ImportText = string.Empty; // also clears the preview
    }

    /// <summary>Default file name for "파일로 저장", so a folder of these is still readable a month later.</summary>
    public string SuggestedFileName(DateTimeOffset now) => $"waffle-settings-{now:yyyyMMdd}.wmset";

    /// <summary>Feed a code read from a file into the import box and diff it.</summary>
    public void LoadCodeFromText(string text)
    {
        ImportText = text;
        PreviewPastedCode();
    }

    /// <summary>Open the folder holding the pre-import snapshots.</summary>
    public void OpenBackupFolder()
    {
        string dir = SettingsBackupStore.Directory(_services.Props.AppDirectory());
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
    }

    /// <summary>Open the user-fonts folder. It IS the store — deleting a file there removes the card — so this
    /// doubles as the "remove a font I added" path without a separate delete UI.</summary>
    public void OpenFontsFolder()
    {
        string dir = FontResolver.UserFontsDir();
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
    }

    public void OpenLogFolder()
    {
        string dir = PacketDebugLogger.LogDirectory();
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
    }

    // ---- replay (BETA) ----

    /// <summary>Record a positional replay per battle. Live: the capture tap is gated on this, so turning it
    /// off stops recording immediately (and turning it on needs no restart).</summary>
    public bool RecordReplay
    {
        get => _settings.RecordReplay;
        set
        {
            if (_settings.RecordReplay == value)
            {
                return;
            }

            _settings.RecordReplay = value;
            _services.RecordReplay = value;
            OnPropertyChanged();
        }
    }

    // ---- 허수아비 테스트 ----

    /// <summary>허수아비 테스트 모드 on/off. Live (like <see cref="RecordReplay"/>, not part of the Cancel snapshot):
    /// writing the shared setting mirrors onto the capture gate at once and updates the header toggle's accent.</summary>
    public bool DummyTestMode
    {
        get => _settings.DummyTestMode;
        set
        {
            if (_settings.DummyTestMode == value)
            {
                return;
            }

            _settings.DummyTestMode = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Selected run length as the ComboBox's string value ("30".."300" seconds).</summary>
    public string DummyDurationValue
    {
        get => _settings.DummyDurationSec.ToString(CultureInfo.InvariantCulture);
        set
        {
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int seconds)
                && seconds != _settings.DummyDurationSec)
            {
                _settings.DummyDurationSec = seconds;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>False in a build without the private replay engine — the panel says so instead of offering a
    /// toggle that could never do anything.</summary>
    public bool ReplayAvailable => _services.ReplayAvailable;

    public Visibility ReplayUnavailableVisibility => _services.ReplayAvailable ? Visibility.Collapsed : Visibility.Visible;

    public void OpenReplayFolder()
    {
        string dir = _services.ReplayDirectory;
        Directory.CreateDirectory(dir); // may not exist yet if nothing has been recorded
        Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
    }

    /// <summary>The replays folder, so the play dialog can open there by default.</summary>
    public string ReplayDirectory => _services.ReplayDirectory;

    /// <summary>Wired by the host (App) to a file picker + replay window (UI-thread work lives there, not in
    /// the VM). Raised by the "리플레이 재생" button.</summary>
    public Action? PlayReplayRequested;

    public void PlayReplay() => PlayReplayRequested?.Invoke();

    /// <summary>Wired by the host (App) to <c>MeterEngine.RequestDummyReset()</c>. Raised by the "허수아비 DPS
    /// 초기화" button — clears only the live dummy report for an immediate re-test (history is preserved).</summary>
    public Action? DummyResetRequested;

    public void ResetDummyDps() => DummyResetRequested?.Invoke();

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
        _hotkeys.SetDummyToggle(PendingDummyToggle);
        _hotkeys.SetDummyReset(PendingDummyReset);
    }

    /// <summary>Revert live-applied settings + pending hotkeys (Cancel).</summary>
    public void Revert()
    {
        _snapshot.Apply(_settings, _controller);
        // Apply() writes the settings object directly, so nothing told the font grid its selection moved.
        OnPropertyChanged(nameof(FontFamily));
        SyncFontSelection();
        SyncNameFxPreview(); // Apply() wrote the settings object directly; the clock has to be told
        NameFxSheen.Rebuild(_settings.NameFxBrightnessPercent);
        PendingReset = _hotkeys.Reset;
        PendingVisibility = _hotkeys.Visibility;
        PendingClickThrough = _hotkeys.ClickThrough;
        PendingDummyToggle = _hotkeys.DummyToggle;
        PendingDummyReset = _hotkeys.DummyReset;
        Reload();
    }

    private sealed record Snapshot(
        string DisplayMode, string DamageValueMode, string ContributionMode, string NameDisplay,
        string FontFamily, int RowHeight, double MeterOpacity, bool MultiMonitor, string Theme, bool AutoHide,
        string TargetInfoDisplayMode, bool IsMinimal, bool ShowCombatTimerInMinimal, bool ShowTargetInfoInMinimal,
        bool ShowServerTag, string BarStyle, bool ShowJoinPanel, bool ShowPreCombatRoster, bool ShowAetherStatus,
        string NameFxMode, bool NameFxShowSelf, bool NameFxShowOthers, int NameFxSpeedPercent, int NameFxBrightnessPercent,
        bool NameFxGauge,
        bool TierShow, string TierEffects, bool TierShowOthers, bool TierShowSelfChip)
    {
        public static Snapshot Capture(MeterSettings s, OverlayController c) => new(
            s.DisplayMode, s.DamageValueMode, s.ContributionMode, s.NameDisplay,
            s.FontFamily, s.RowHeight, s.MeterOpacity, s.MultiMonitorMode, s.OverlayTheme, c.IsAutoHide,
            s.TargetInfoDisplayMode, s.IsMinimal, s.ShowCombatTimerInMinimal, s.ShowTargetInfoInMinimal,
            s.ShowServerTag, s.BarStyle, s.ShowJoinPanel, s.ShowPreCombatRoster, s.ShowAetherStatus,
            s.NameFxMode, s.NameFxShowSelf, s.NameFxShowOthers, s.NameFxSpeedPercent, s.NameFxBrightnessPercent,
            s.NameFxGauge,
            s.TierShow, s.TierEffects, s.TierShowOthers, s.TierShowSelfChip);

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
            // Every setter here writes through to the properties file immediately, so a toggle left out of this
            // record is not "unsaved on Cancel" — it is saved and unrevertable. 오드 표시 was missing, which made
            // turning it off and cancelling a one-way trip: the footer badge (and the shugo key badge it gates)
            // stayed hidden across restarts with no way back except finding the same toggle again.
            s.ShowAetherStatus = ShowAetherStatus;
            s.NameFxMode = NameFxMode;
            s.NameFxShowSelf = NameFxShowSelf;
            s.NameFxShowOthers = NameFxShowOthers;
            s.NameFxSpeedPercent = NameFxSpeedPercent;
            s.NameFxBrightnessPercent = NameFxBrightnessPercent;
            s.NameFxGauge = NameFxGauge;
            // 같은 이유로 티어 장식 4키도 여기 있어야 한다 — 빠져 있던 동안 색상·스킨 탭에서 티어 표시를
            // 껐다가 취소해도 꺼진 채로 남았다.
            s.TierShow = TierShow;
            s.TierEffects = TierEffects;
            s.TierShowOthers = TierShowOthers;
            s.TierShowSelfChip = TierShowSelfChip;
            // 같은 이유로 티어 장식 4키도 여기 있어야 한다 — 빠져 있던 동안 색상·스킨 탭에서 티어 표시를
            // 껐다가 취소해도 꺼진 채로 남았다.
            NameFxSheen.Rebuild(NameFxBrightnessPercent);
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

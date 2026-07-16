using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using WaffleMeter.App.Core;
using WaffleMeter.Data;

namespace WaffleMeter.App.Wpf;

/// <summary>
/// View model for the detail window: fetches a player's skill breakdown + buff/debuff uptime via
/// <see cref="DetailModel"/> and exposes bindable, styled rows. <see cref="Refresh"/> is called on
/// each live report tick and preserves per-group expansion state.
/// </summary>
public sealed class DetailsViewModel : INotifyPropertyChanged
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    // Brighter emerald gradient (was a flat, dull #55c42a→#3a9e20) so the damage bar reads clean + vivid
    // on the dark panel; white bold damage text is overlaid for contrast.
    internal static readonly Brush SkillBar = Frozen(new LinearGradientBrush(C("#FF4ADE80"), C("#FF15A34A"), 0.0));
    // Uptime gauge: the bar is a left-to-right gradient (dark -> bright) so it reads as a lit gauge rather than
    // a painted block; the matching solid is used for the % text, where a gradient would smear across glyphs.
    private static readonly Brush GoodBar = Frozen(new LinearGradientBrush(C("#FF15803D"), C("#FF4ADE80"), 0.0));
    private static readonly Brush WarnBar = Frozen(new LinearGradientBrush(C("#FFB45309"), C("#FFFBBF24"), 0.0));
    private static readonly Brush BadBar = Frozen(new LinearGradientBrush(C("#FFB91C1C"), C("#FFF87171"), 0.0));
    private static readonly Brush GoodBuff = Frozen(new SolidColorBrush(C("#4ade80")));
    private static readonly Brush WarnBuff = Frozen(new SolidColorBrush(C("#fbbf24")));
    private static readonly Brush BadBuff = Frozen(new SolidColorBrush(C("#f87171")));

    private readonly int _uid;
    private readonly DpsCalculator _calc;
    private readonly string _fallbackName;

    public DetailsViewModel(DpsReport report, int uid, DpsCalculator calc, string name, MeterColorTheme theme, string fontFamily)
    {
        _uid = uid;
        _calc = calc;
        _fallbackName = name;
        FontFamily = fontFamily;
        // Theme-linked text colors (snapshot at open; the detail window is short-lived per row click).
        AmountBrush = ThemeBrush(theme.MeterStatAmount);
        ContributionBrush = ThemeBrush(theme.MeterStatPercent);
        DetailCombatTimeBrush = ThemeBrush(theme.CombatTimeColor);
        Refresh(report);
    }

    private string _title = string.Empty;

    /// <summary>"{player} 상세내역" — re-derived on every <see cref="Refresh"/> from the resolved report's
    /// roster, so it self-heals to the real nickname and never gets stuck showing a bare numeric uid.</summary>
    public string Title { get => _title; private set => Set(ref _title, value); }

    /// <summary>Selected UI font family (resolved to a FontFamily by FontFamilyConverter in XAML).</summary>
    public string FontFamily { get; }

    /// <summary>누적 피해량 color (theme meterStatAmount).</summary>
    public Brush AmountBrush { get; }

    /// <summary>피해량 기여도 color (theme meterStatPercent).</summary>
    public Brush ContributionBrush { get; }

    /// <summary>전투 시간 color (theme combatTimeColor).</summary>
    public Brush DetailCombatTimeBrush { get; }
    public ObservableCollection<SkillGroupVM> Skills { get; } = new();
    public ObservableCollection<BuffRowVM> Buffs { get; } = new();
    public ObservableCollection<BuffRowVM> Debuffs { get; } = new();

    private DpsGraphModel? _graph;

    /// <summary>Render-ready DPS-over-time graph for this player: per-second damage plus this player's buff
    /// timeline (icon lane), drawn on a plain Canvas by <see cref="DetailWindow"/>. Rebuilt every
    /// <see cref="Refresh"/>; null when there isn't enough to plot.</summary>
    public DpsGraphModel? Graph { get => _graph; private set => Set(ref _graph, value); }

    private bool _hasGraph;

    /// <summary>True when the series has at least two seconds to draw a line; a shorter fight shows the
    /// "표시할 데이터가 없어요" note on the graph tab instead of a single dot.</summary>
    public bool HasGraph { get => _hasGraph; private set => Set(ref _hasGraph, value); }

    private string _totalDamageText = "0";
    public string TotalDamageText { get => _totalDamageText; private set => Set(ref _totalDamageText, value); }

    private string _dpsText = "0";
    public string DpsText { get => _dpsText; private set => Set(ref _dpsText, value); }

    private string _hitsText = "0";
    public string HitsText { get => _hitsText; private set => Set(ref _hitsText, value); }
    private string _contributionText = "0.0%";
    public string ContributionText { get => _contributionText; private set => Set(ref _contributionText, value); }
    private string _critText = "0.0%";
    public string CritText { get => _critText; private set => Set(ref _critText, value); }
    private string _strongText = "0.0%";
    public string StrongText { get => _strongText; private set => Set(ref _strongText, value); }
    private string _perfectText = "0.0%";
    public string PerfectText { get => _perfectText; private set => Set(ref _perfectText, value); }
    private string _backText = "0.0%";
    public string BackText { get => _backText; private set => Set(ref _backText, value); }
    private string _frontText = "0.0%";
    public string FrontText { get => _frontText; private set => Set(ref _frontText, value); }
    private string _parryText = "0.0%";
    public string ParryText { get => _parryText; private set => Set(ref _parryText, value); }
    private string _combatTimeText = "0:00";
    public string CombatTimeText { get => _combatTimeText; private set => Set(ref _combatTimeText, value); }
    private bool _hasBuffs;
    public bool HasBuffs { get => _hasBuffs; private set => Set(ref _hasBuffs, value); }
    private bool _hasDebuffs;
    public bool HasDebuffs { get => _hasDebuffs; private set => Set(ref _hasDebuffs, value); }

    private bool _hasData = true;

    /// <summary>False when the resolved report has no row for this uid — the window then shows a "데이터 없음"
    /// placeholder and "-" stats instead of a misleading all-zero breakdown (and an honest title).</summary>
    public bool HasData
    {
        get => _hasData;
        private set
        {
            if (_hasData == value)
            {
                return;
            }

            _hasData = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasData)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NoDataVisibility)));
        }
    }

    public Visibility NoDataVisibility => _hasData ? Visibility.Collapsed : Visibility.Visible;

    public void Refresh(DpsReport report)
    {
        // Re-derive the title + presence from the RESOLVED report each tick: the title self-heals to the
        // real nickname (or an honest "플레이어 {uid}" rather than a bare number), and a uid that isn't in
        // this report renders a "데이터 없음" state instead of a misleading all-zero breakdown.
        string? nickname = report.Contributors.FirstOrDefault(c => c.Id == _uid)?.Nickname;
        bool present = report.Information.ContainsKey(_uid) || report.Contributors.Any(c => c.Id == _uid);
        string display = !string.IsNullOrWhiteSpace(nickname) ? nickname!
            : !string.IsNullOrWhiteSpace(_fallbackName) && !int.TryParse(_fallbackName, out _) ? _fallbackName
            : $"플레이어 {_uid}";
        Title = $"{display} 상세내역";
        HasData = present;

        if (!present)
        {
            Skills.Clear();
            Buffs.Clear();
            Debuffs.Clear();
            HasBuffs = false;
            HasDebuffs = false;
            Graph = null;
            HasGraph = false;
            _graphSignature = long.MinValue;
            TotalDamageText = ContributionText = CritText = StrongText =
                PerfectText = BackText = FrontText = ParryText = CombatTimeText = "-";
            return;
        }

        Dictionary<string, AnalyzedSkill> skills = _calc.BattleDetails(report, _uid);

        // Prefer the frozen buff-rate snapshot the battle was saved with (identical to the stats/web data).
        // The live buff repository is pruned once a battle is saved, so recomputing post-battle under-counts;
        // recompute only for the in-progress battle, where no snapshot exists yet and the repo is intact.
        bool hasSnapshot = report.BuffRates.Count > 0;
        List<OperatingData> own = hasSnapshot
            ? report.BuffRates.GetValueOrDefault(_uid) ?? new()
            : _calc.GetBuffOperatingRate(_uid, report.BattleStart, report.BattleEnd);
        List<OperatingData> boss = hasSnapshot
            ? report.BossBuffRates
            : report.Target != null
                ? _calc.GetBuffOperatingRate(report.Target.Id, report.BattleStart, report.BattleEnd)
                : new();

        User? user = report.Contributors.FirstOrDefault(c => c.Id == _uid);
        double contribution = report.Information.TryGetValue(_uid, out DpsInformation? info) ? info.Contribution : 0.0;
        long combatMs = Math.Max(report.BattleEnd - report.BattleStart, 0);

        DetailModel model = DetailModel.Compute(
            skills, own, boss, _uid, user?.Job, contribution, combatMs);

        TotalDamageText = MeterFormat.FormatAmount(model.TotalDamage);
        DpsText = model.CombatMs > 0
            ? (model.TotalDamage / (model.CombatMs / 1000.0)).ToString("N0", Inv)
            : "0";
        HitsText = model.HitCount.ToString("N0", Inv);
        ContributionText = model.Contribution.ToString("F1", Inv) + "%";
        CritText = model.CritPct.ToString("F1", Inv) + "%";
        StrongText = model.StrongPct.ToString("F1", Inv) + "%";
        PerfectText = model.PerfectPct.ToString("F1", Inv) + "%";
        BackText = model.BackPct.ToString("F1", Inv) + "%";
        FrontText = model.FrontPct.ToString("F1", Inv) + "%";
        ParryText = model.ParryPct.ToString("F1", Inv) + "%";
        TimeSpan span = TimeSpan.FromMilliseconds(model.CombatMs);
        CombatTimeText = $"{(int)span.TotalMinutes}:{span.Seconds:D2}";

        ReconcileSkills(model.Skills, model.CombatMs);
        Rebuild(Buffs, model.Buffs);
        Rebuild(Debuffs, model.Debuffs);
        HasBuffs = Buffs.Count > 0;
        HasDebuffs = Debuffs.Count > 0;

        // DPS-over-time graph: per-second damage + this player's buff timeline. Prefer the frozen snapshot
        // (history replay / post-combat), fall back to the live accumulator for the in-progress battle —
        // the same snapshot-vs-live split as the buff rates above (the two are frozen together at save).
        long[] series = report.DpsSeries.Count > 0
            ? report.DpsSeries.GetValueOrDefault(_uid) ?? []
            : _calc.GetDpsSeries(_uid, report.BattleStart, report.BattleEnd);
        List<BuffTimeline> allTimelines = report.BuffIntervals.Count > 0
            ? report.BuffIntervals.GetValueOrDefault(_uid) ?? new()
            : _calc.GetBuffIntervals(_uid, report.BattleStart, report.BattleEnd);

        // Keep only the buffs that actually shape THIS player's damage: their own class(job) buffs — exactly the
        // "내 버프" set the buff-uptime tab shows (mirrors DetailModel.BuildOwnBuffs: self-cast + job-prefix match).
        // Consumables (주문서/음식/음료 → EffectiveJobPrefix 0) and other players' buffs are dropped, so a 100%-uptime
        // food/scroll no longer crowds out the real damage buffs. Falls back to "any self class buff" when the
        // player's job isn't recognized yet — still excludes consumables.
        int selfPrefix = user?.Job is { } jb ? JobClassInfo.BasicSkillCode(jb) / 1_000_000 : -1;
        List<BuffTimeline> timelines = allTimelines
            .Where(t => t.ActorId == _uid && (selfPrefix > 0 ? t.EffectiveJobPrefix == selfPrefix : t.JobPrefix != 0))
            .ToList();
        // Only rebuild (and so redraw) the graph when the underlying data actually changed — Refresh runs every
        // tick, but the per-second series only grows once a second and is frozen while idle/replaying history,
        // so this keeps the Canvas redraw off the hot path (the handoff's 라이브 리프레시 비용 caveat).
        long sig = GraphSignature(series, timelines);
        if (Graph is null || sig != _graphSignature)
        {
            _graphSignature = sig;
            Graph = BuildGraph(series, timelines, report.BattleStart);
        }

        HasGraph = series.Length >= 2;
    }

    private long _graphSignature = long.MinValue;

    // A cheap content hash: total damage (grows on every hit, constant when frozen) + series length + buff/span
    // counts. Enough to notice any change worth redrawing without deep-comparing the model each tick.
    private static long GraphSignature(long[] series, List<BuffTimeline> timelines)
    {
        long sig = series.Length;
        long total = 0;
        foreach (long v in series) total += v;
        sig = sig * 1_000_003L + total;
        sig = sig * 1_000_003L + timelines.Count;
        long spans = 0;
        foreach (BuffTimeline t in timelines) spans += t.Spans.Count;
        return sig * 1_000_003L + spans;
    }

    // Distinct, well-separated hues so each buff lane / legend chip is told apart at a glance (cycled if a fight
    // somehow exceeds the count — capped at MaxGraphBuffs below, which is ≤ this length).
    private static readonly Brush[] GraphPalette =
    [
        Frozen(new SolidColorBrush(C("#FF38BDF8"))), // sky
        Frozen(new SolidColorBrush(C("#FFA78BFA"))), // violet
        Frozen(new SolidColorBrush(C("#FFFBBF24"))), // amber
        Frozen(new SolidColorBrush(C("#FF34D399"))), // emerald
        Frozen(new SolidColorBrush(C("#FFF472B6"))), // pink
        Frozen(new SolidColorBrush(C("#FF2DD4BF"))), // teal
        Frozen(new SolidColorBrush(C("#FFFB923C"))), // orange
        Frozen(new SolidColorBrush(C("#FF818CF8"))), // indigo
        Frozen(new SolidColorBrush(C("#FFA3E635"))), // lime
        Frozen(new SolidColorBrush(C("#FFFB7185"))), // rose
    ];

    private const int MaxGraphBuffs = 8;

    private static DpsGraphModel? BuildGraph(
        IReadOnlyList<long> perSecond, IReadOnlyList<BuffTimeline> timelines, long battleStart)
    {
        if (perSecond.Count == 0) return null;

        long peak = 0;
        foreach (long v in perSecond)
        {
            if (v > peak) peak = v;
        }

        // Highest-uptime buffs first, capped so the lane stack + legend stay readable (a player can carry a long
        // tail of low-value food/scroll/party buffs). The colour is assigned here so the XAML legend chip and the
        // hand-drawn lane share one source of truth.
        List<BuffTimeline> top = timelines
            .Where(t => t.Spans.Count > 0)
            .OrderByDescending(t => t.Spans.Sum(s => s.End - s.Start))
            .Take(MaxGraphBuffs)
            .ToList();

        var buffs = new List<DpsGraphBuff>(top.Count);
        for (int i = 0; i < top.Count; i++)
        {
            BuffTimeline t = top[i];
            var spans = new List<(double StartSec, double EndSec)>(t.Spans.Count);
            foreach ((long start, long end) in t.Spans)
            {
                spans.Add(((start - battleStart) / 1000.0, (end - battleStart) / 1000.0));
            }

            buffs.Add(new DpsGraphBuff(t.Name, JoinIcons.Skill(t.Code), GraphPalette[i % GraphPalette.Length], spans));
        }

        return new DpsGraphModel(perSecond, peak, buffs);
    }

    private void ReconcileSkills(IReadOnlyList<DetailSkillGroup> groups, long combatMs)
    {
        // Preserve expansion of chain groups (unique main code) across live ticks.
        Dictionary<int, bool> expanded = Skills.Where(g => g.HasChildren).ToDictionary(g => g.Code, g => g.IsExpanded);
        Skills.Clear();
        foreach (DetailSkillGroup g in groups)
        {
            var vm = new SkillGroupVM(g, combatMs);
            if (g.HasChildren && expanded.TryGetValue(g.Merged.Code, out bool ex))
            {
                vm.IsExpanded = ex;
            }

            Skills.Add(vm);
        }
    }

    // Sections are flattened into one table: the section label rides along as each row's subtitle
    // ("내 버프" / "파티원 버프" / "그 외", or the caster's name for boss debuffs), which is how the stats
    // site presents the same data. Keeping the label per row lets the rows sort as one list.
    private static void Rebuild(ObservableCollection<BuffRowVM> target, IReadOnlyList<DetailBuffSection> sections)
    {
        target.Clear();
        foreach (DetailBuffSection s in sections)
        {
            foreach (DetailBuffRow r in s.Rows)
            {
                target.Add(new BuffRowVM(r, s.Label));
            }
        }
    }

    internal static Brush BuffBrush(double rate) => rate >= 80 ? GoodBuff : rate >= 50 ? WarnBuff : BadBuff;

    internal static Brush BuffBarBrush(double rate) => rate >= 80 ? GoodBar : rate >= 50 ? WarnBar : BadBar;

    private static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex)!;
    private static Brush Frozen(Brush b)
    {
        b.Freeze();
        return b;
    }

    // Theme colors may be rgba(...) (ColorConverter can't parse that) -> use ColorString.
    private static Brush ThemeBrush(string value)
    {
        Color color = ColorString.TryParse(value, out ColorRgba c) ? Color.FromArgb(c.A, c.R, c.G, c.B) : Colors.White;
        return Frozen(new SolidColorBrush(color));
    }

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

public sealed class SkillGroupVM : INotifyPropertyChanged
{
    public SkillGroupVM(DetailSkillGroup group, long combatMs)
    {
        Code = group.Merged.Code;
        HasChildren = group.HasChildren;
        Merged = new SkillRowVM(group.Merged, combatMs);
        Children = group.Children.Select(r => new SkillRowVM(r, combatMs)).ToList();
    }

    public int Code { get; }
    public bool HasChildren { get; }
    public SkillRowVM Merged { get; }
    public IReadOnlyList<SkillRowVM> Children { get; }

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value)
            {
                return;
            }

            _isExpanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class SkillRowVM
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    // DetailModel appends this to a DoT row's name so the old single-column table could tell them apart.
    // The table now carries a 유형 badge, so the suffix would just repeat it.
    private const string DotSuffix = " - 지속";

    public SkillRowVM(DetailSkillRow row, long combatMs)
    {
        Name = row.IsDot && row.Name.EndsWith(DotSuffix, StringComparison.Ordinal)
            ? row.Name[..^DotSuffix.Length]
            : row.Name;
        IsDot = row.IsDot;
        TypeText = row.IsDot ? "지속" : "직접";
        IconSource = JoinIcons.Skill(row.Code);
        HitsText = row.Hits.ToString("N0", Inv);
        CritText = Pct(row.CritPct);
        StrongText = Pct(row.StrongPct);
        PerfectText = Pct(row.PerfectPct);
        BackText = Pct(row.BackPct);
        FrontText = Pct(row.FrontPct);
        ParryText = Pct(row.ParryPct);
        Spec = BuildSpec(row.Spec);
        HasSpec = row.Spec != null;
        DpsText = combatMs > 0 ? MeterFormat.FormatAmount(row.Damage / (combatMs / 1000.0)) : "-";
        AvgText = row.Hits > 0 ? MeterFormat.FormatAmount((double)row.Damage / row.Hits) : "-";
        DamageText = MeterFormat.FormatAmount(row.Damage);
        PercentText = (row.Ratio * 100).ToString("F1", Inv) + "%";
        BarRatio = row.Ratio;
        BarRest = 1.0 - row.Ratio;
    }

    public string Name { get; }
    public bool IsDot { get; }
    public string TypeText { get; }
    public ImageSource? IconSource { get; }
    public string HitsText { get; }
    public string CritText { get; }
    public string StrongText { get; }
    public string PerfectText { get; }
    public string BackText { get; }
    public string FrontText { get; }
    public string ParryText { get; }
    public string DpsText { get; }
    public string AvgText { get; }
    public string DamageText { get; }
    public string PercentText { get; }
    public double BarRatio { get; }
    public double BarRest { get; }
    public Brush BarBrush => DetailsViewModel.SkillBar;

    /// <summary>Five pips for the skill's specialization (특화) — active slots lit, inactive dim. Empty when
    /// the skill carries no specialization (basic attacks, DoT rows, non-player skills).</summary>
    public IReadOnlyList<SpecPipVM> Spec { get; }

    /// <summary>Whether this row has any specialization data (drives showing the pips vs a "-").</summary>
    public bool HasSpec { get; }

    public Visibility SpecVisibility => HasSpec ? Visibility.Visible : Visibility.Collapsed;

    public Visibility NoSpecVisibility => HasSpec ? Visibility.Collapsed : Visibility.Visible;

    private static IReadOnlyList<SpecPipVM> BuildSpec(IReadOnlyList<bool>? spec)
    {
        if (spec == null)
        {
            return Array.Empty<SpecPipVM>();
        }

        var pips = new SpecPipVM[spec.Count];
        for (int i = 0; i < spec.Count; i++)
        {
            pips[i] = new SpecPipVM(spec[i]);
        }

        return pips;
    }

    private static string Pct(int? value) => value.HasValue ? value.Value + "%" : "-";
}

/// <summary>One specialization pip: an active slot glows in the accent color, an inactive one is dim.</summary>
public sealed class SpecPipVM
{
    // Active = emerald (matches the skill damage bar / "output" accent); inactive = a faint gray dot.
    private static readonly Brush Active = Frozen(new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80)));
    private static readonly Brush Inactive = Frozen(new SolidColorBrush(Color.FromArgb(0x55, 0x9C, 0xA3, 0xAF)));

    public SpecPipVM(bool active) => Fill = active ? Active : Inactive;

    public Brush Fill { get; }

    private static Brush Frozen(Brush b)
    {
        b.Freeze();
        return b;
    }
}


public sealed class BuffRowVM
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public BuffRowVM(DetailBuffRow row, string subtitle)
    {
        Name = row.Name;
        Subtitle = subtitle;
        RateText = row.Rate.ToString("F1", Inv) + "%";
        BarRatio = row.Rate / 100.0;
        BarRest = 1.0 - BarRatio;
        RateBrush = DetailsViewModel.BuffBrush(row.Rate);
        BarBrush = DetailsViewModel.BuffBarBrush(row.Rate);
        Description = row.Description;
        IconSource = JoinIcons.Skill(row.Code); // buff/debuff share the skill-icon manifest
    }

    public string Name { get; }

    /// <summary>The section this row came from ("내 버프" / "그 외"). Empty for boss debuffs — every row there
    /// has the same caster (this player), so a subtitle would repeat on every line.</summary>
    public string Subtitle { get; }

    public Visibility SubtitleVisibility => Subtitle.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

    public string RateText { get; }
    public double BarRatio { get; }
    public double BarRest { get; }
    public Brush RateBrush { get; }
    public Brush BarBrush { get; }
    public string Description { get; }
    public ImageSource? IconSource { get; }
}

/// <summary>Render-ready DPS-over-time graph the detail window hand-draws on a Canvas. <see cref="PerSecond"/>
/// is dense damage-per-whole-second from the battle start (index = second offset); <see cref="PeakPerSecond"/>
/// is its max for y-axis scaling/label; <see cref="Buffs"/> is the icon lane.</summary>
public sealed record DpsGraphModel(
    IReadOnlyList<long> PerSecond,
    long PeakPerSecond,
    IReadOnlyList<DpsGraphBuff> Buffs);

/// <summary>One buff on the graph: its name + icon + assigned lane colour (shared by the legend chip and the
/// hand-drawn Gantt lane), and the second-offset spans (from battle start) it was active for.</summary>
public sealed record DpsGraphBuff(
    string Name,
    ImageSource? Icon,
    Brush LaneBrush,
    IReadOnlyList<(double StartSec, double EndSec)> Spans);

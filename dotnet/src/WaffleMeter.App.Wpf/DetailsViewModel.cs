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
    private static readonly Brush GoodBuff = Frozen(new SolidColorBrush(C("#55c42a")));
    private static readonly Brush WarnBuff = Frozen(new SolidColorBrush(C("#e6a817")));
    private static readonly Brush BadBuff = Frozen(new SolidColorBrush(C("#e05252")));

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
    public ObservableCollection<BuffSectionVM> Buffs { get; } = new();
    public ObservableCollection<BuffSectionVM> Debuffs { get; } = new();

    /// <summary>Every combat participant this battle, for the party overview in the detail window: job /
    /// server / sub-party slot / power / contribution. Sorted by damage desc; the viewed actor is flagged.</summary>
    public ObservableCollection<PartyMemberVM> Members { get; } = new();

    private bool _hasMembers;
    /// <summary>True when there is more than one participant (so a one-person fight hides the party section).</summary>
    public bool HasMembers { get => _hasMembers; private set => Set(ref _hasMembers, value); }

    private string _totalDamageText = "0";
    public string TotalDamageText { get => _totalDamageText; private set => Set(ref _totalDamageText, value); }
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
            Members.Clear();
            HasBuffs = false;
            HasDebuffs = false;
            HasMembers = false;
            TotalDamageText = ContributionText = CritText = StrongText =
                PerfectText = BackText = ParryText = CombatTimeText = "-";
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
            skills, own, boss, _uid, user?.Job, contribution, combatMs,
            id => report.Contributors.FirstOrDefault(c => c.Id == id)?.Nickname);

        TotalDamageText = model.TotalDamage.ToString("N0", Inv);
        ContributionText = model.Contribution.ToString("F1", Inv) + "%";
        CritText = model.CritPct.ToString("F1", Inv) + "%";
        StrongText = model.StrongPct.ToString("F1", Inv) + "%";
        PerfectText = model.PerfectPct.ToString("F1", Inv) + "%";
        BackText = model.BackPct.ToString("F1", Inv) + "%";
        ParryText = model.ParryPct.ToString("F1", Inv) + "%";
        TimeSpan span = TimeSpan.FromMilliseconds(model.CombatMs);
        CombatTimeText = $"{(int)span.TotalMinutes}:{span.Seconds:D2}";

        ReconcileSkills(model.Skills);
        Rebuild(Buffs, model.Buffs);
        Rebuild(Debuffs, model.Debuffs);
        HasBuffs = Buffs.Count > 0;
        HasDebuffs = Debuffs.Count > 0;
        RebuildMembers(report);
    }

    // The party overview: every named participant with damage, ranked, styled like a meter row.
    private void RebuildMembers(DpsReport report)
    {
        int selfId = report.ExecutorId;
        double top = report.Information.Values.Count > 0 ? report.Information.Values.Max(i => i.Amount) : 0;

        List<PartyMemberVM> members = report.Contributors
            .Where(u => !string.IsNullOrWhiteSpace(u.Nickname) && report.Information.ContainsKey(u.Id))
            .Select(u =>
            {
                DpsInformation info = report.Information[u.Id];
                bool isUser = u.IsExecutor || (selfId != 0 && u.Id == selfId);
                report.PartySlots.TryGetValue(u.Id, out int slot);
                return new PartyMemberVM(
                    uid: u.Id,
                    name: u.Nickname!,
                    jobIcon: JoinIcons.Job(u.Job?.ClassName()),
                    serverTag: ServerNames.GetServerLabel(u.Server),
                    slot: slot,
                    power: u.Power,
                    amount: info.Amount,
                    contribution: info.Contribution,
                    barRatio: top > 0 ? info.Amount / top : 0,
                    isUser: isUser,
                    isViewed: u.Id == _uid);
            })
            .OrderByDescending(m => m.Amount)
            .ToList();

        Members.Clear();
        foreach (PartyMemberVM m in members)
        {
            Members.Add(m);
        }

        HasMembers = Members.Count > 1;
    }

    private void ReconcileSkills(IReadOnlyList<DetailSkillGroup> groups)
    {
        // Preserve expansion of chain groups (unique main code) across live ticks.
        Dictionary<int, bool> expanded = Skills.Where(g => g.HasChildren).ToDictionary(g => g.Code, g => g.IsExpanded);
        Skills.Clear();
        foreach (DetailSkillGroup g in groups)
        {
            var vm = new SkillGroupVM(g);
            if (g.HasChildren && expanded.TryGetValue(g.Merged.Code, out bool ex))
            {
                vm.IsExpanded = ex;
            }

            Skills.Add(vm);
        }
    }

    private static void Rebuild(ObservableCollection<BuffSectionVM> target, IReadOnlyList<DetailBuffSection> sections)
    {
        target.Clear();
        foreach (DetailBuffSection s in sections)
        {
            target.Add(new BuffSectionVM(s.Label, s.Rows.Select(r => new BuffRowVM(r)).ToList()));
        }
    }

    internal static Brush BuffBrush(double rate) => rate >= 80 ? GoodBuff : rate >= 50 ? WarnBuff : BadBuff;

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
    public SkillGroupVM(DetailSkillGroup group)
    {
        Code = group.Merged.Code;
        HasChildren = group.HasChildren;
        Merged = new SkillRowVM(group.Merged);
        Children = group.Children.Select(r => new SkillRowVM(r)).ToList();
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

    public SkillRowVM(DetailSkillRow row)
    {
        Name = row.Name;
        IconSource = JoinIcons.Skill(row.Code);
        HitsText = row.Hits.ToString("N0", Inv);
        CritText = Pct(row.CritPct);
        StrongText = Pct(row.StrongPct);
        PerfectText = Pct(row.PerfectPct);
        BackText = Pct(row.BackPct);
        ParryText = Pct(row.ParryPct);
        DamageText = row.Damage.ToString("N0", Inv);
        PercentText = (row.Ratio * 100).ToString("F1", Inv) + "%";
        BarRatio = row.Ratio;
        BarRest = 1.0 - row.Ratio;
    }

    public string Name { get; }
    public ImageSource? IconSource { get; }
    public string HitsText { get; }
    public string CritText { get; }
    public string StrongText { get; }
    public string PerfectText { get; }
    public string BackText { get; }
    public string ParryText { get; }
    public string DamageText { get; }
    public string PercentText { get; }
    public double BarRatio { get; }
    public double BarRest { get; }
    public Brush BarBrush => DetailsViewModel.SkillBar;

    private static string Pct(int? value) => value.HasValue ? value.Value + "%" : "-";
}

/// <summary>One participant row in the detail window's party overview (read-only).</summary>
public sealed class PartyMemberVM
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public PartyMemberVM(int uid, string name, ImageSource? jobIcon, string serverTag, int slot,
        int power, double amount, double contribution, double barRatio, bool isUser, bool isViewed)
    {
        Uid = uid;
        Name = name;
        JobIcon = jobIcon;
        ServerTag = serverTag;
        ServerTagVisibility = string.IsNullOrEmpty(serverTag) ? Visibility.Collapsed : Visibility.Visible;
        // 8-인 공대 sub-party: slots 1-4 = party 1, 5-8 = party 2 (0 = unmatched / non-raid).
        SlotText = slot is >= 1 and <= 8 ? (slot <= 4 ? "1파티" : "2파티") : string.Empty;
        SlotVisibility = SlotText.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        PowerText = power > 0 ? MeterFormat.FormatPower(power) : string.Empty;
        PowerVisibility = power > 0 ? Visibility.Visible : Visibility.Collapsed;
        Amount = amount;
        AmountText = amount.ToString("N0", Inv);
        ContribText = MeterFormat.FormatPercent(contribution);
        BarRatio = Math.Clamp(barRatio, 0, 1);
        BarRest = 1.0 - BarRatio;
        IsUser = isUser;
        IsViewed = isViewed;
    }

    public int Uid { get; }
    public string Name { get; }
    public ImageSource? JobIcon { get; }
    public string ServerTag { get; }
    public Visibility ServerTagVisibility { get; }
    public string SlotText { get; }
    public Visibility SlotVisibility { get; }
    public string PowerText { get; }
    public Visibility PowerVisibility { get; }
    public double Amount { get; }
    public string AmountText { get; }
    public string ContribText { get; }
    public double BarRatio { get; }
    public double BarRest { get; }
    public bool IsUser { get; }
    /// <summary>True for the member whose breakdown the window is currently showing.</summary>
    public bool IsViewed { get; }
}

public sealed record BuffSectionVM(string Label, IReadOnlyList<BuffRowVM> Rows);

public sealed class BuffRowVM
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public BuffRowVM(DetailBuffRow row)
    {
        Name = row.Name;
        RateText = row.Rate.ToString("F1", Inv) + "%";
        BarRatio = row.Rate / 100.0;
        BarRest = 1.0 - BarRatio;
        Brush = DetailsViewModel.BuffBrush(row.Rate);
        Description = row.Description;
        IconSource = JoinIcons.Skill(row.Code); // buff/debuff share the skill-icon manifest
    }

    public string Name { get; }
    public string RateText { get; }
    public double BarRatio { get; }
    public double BarRest { get; }
    public Brush Brush { get; }
    public string Description { get; }
    public ImageSource? IconSource { get; }
}

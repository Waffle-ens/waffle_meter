using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
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
    internal static readonly Brush SkillBar = Frozen(new LinearGradientBrush(C("#55c42a"), C("#3a9e20"), 0.0));
    private static readonly Brush GoodBuff = Frozen(new SolidColorBrush(C("#55c42a")));
    private static readonly Brush WarnBuff = Frozen(new SolidColorBrush(C("#e6a817")));
    private static readonly Brush BadBuff = Frozen(new SolidColorBrush(C("#e05252")));

    private readonly int _uid;
    private readonly DpsCalculator _calc;

    public DetailsViewModel(DpsReport report, int uid, DpsCalculator calc, string name)
    {
        _uid = uid;
        _calc = calc;
        Title = $"{name} 상세내역";
        Refresh(report);
    }

    public string Title { get; }
    public ObservableCollection<SkillGroupVM> Skills { get; } = new();
    public ObservableCollection<BuffSectionVM> Buffs { get; } = new();
    public ObservableCollection<BuffSectionVM> Debuffs { get; } = new();

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
    private string _combatTimeText = "0:00";
    public string CombatTimeText { get => _combatTimeText; private set => Set(ref _combatTimeText, value); }
    private bool _hasBuffs;
    public bool HasBuffs { get => _hasBuffs; private set => Set(ref _hasBuffs, value); }
    private bool _hasDebuffs;
    public bool HasDebuffs { get => _hasDebuffs; private set => Set(ref _hasDebuffs, value); }

    public void Refresh(DpsReport report)
    {
        Dictionary<string, AnalyzedSkill> skills = _calc.BattleDetails(report, _uid);
        List<OperatingData> own = _calc.GetBuffOperatingRate(_uid, report.BattleStart, report.BattleEnd);
        List<OperatingData> boss = report.Target != null
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
        TimeSpan span = TimeSpan.FromMilliseconds(model.CombatMs);
        CombatTimeText = $"{(int)span.TotalMinutes}:{span.Seconds:D2}";

        ReconcileSkills(model.Skills);
        Rebuild(Buffs, model.Buffs);
        Rebuild(Debuffs, model.Debuffs);
        HasBuffs = Buffs.Count > 0;
        HasDebuffs = Debuffs.Count > 0;
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
    }

    public string Name { get; }
    public string RateText { get; }
    public double BarRatio { get; }
    public double BarRest { get; }
    public Brush Brush { get; }
    public string Description { get; }
}

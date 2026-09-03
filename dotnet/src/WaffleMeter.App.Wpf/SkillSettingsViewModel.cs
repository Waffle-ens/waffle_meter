using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using WaffleMeter.App.Core;

namespace WaffleMeter.App.Wpf;

/// <summary>
/// View model for the join-panel skill-settings flyout (port of JoinRequestSkillSettings): every tracked
/// skill grouped by job → 일반/스티그마, each a toggle chip bound to <see cref="SkillVisibility"/>.
/// <see cref="Changed"/> fires whenever the visible set changes so the join panel can re-render badges.
/// </summary>
public sealed class SkillSettingsViewModel
{
    private readonly SkillVisibility _visibility;

    public SkillSettingsViewModel(SkillVisibility visibility)
    {
        _visibility = visibility;
        Groups = SkillCatalog.GroupedByJob
            .Select(g => new SkillJobGroupViewModel(
                g, visibility, () => Changed?.Invoke(), c => SkillCatalog.GetName(c) ?? c.ToString()))
            .ToList();
    }

    public IReadOnlyList<SkillJobGroupViewModel> Groups { get; }

    /// <summary>Raised after any toggle (App syncs the join panel's visible set + reconciles).</summary>
    public event Action? Changed;

    /// <summary>Re-read every chip from <see cref="SkillVisibility"/>. The rows are built once in the
    /// constructor and each chip's <c>IsVisible</c> reads through to the shared set, so a replacement from
    /// outside (a settings import calling <c>Reload</c>) changes the truth without telling the bindings.
    /// Notify-only — this does not write, so it cannot echo back into <see cref="Changed"/>.</summary>
    public void Refresh()
    {
        foreach (SkillJobGroupViewModel group in Groups)
        {
            group.Refresh();
        }
    }
}

/// <summary>One job's block of chips. Deliberately generic over <see cref="ISkillVisibility"/> and the name
/// lookup so the same rows — and therefore the same flyout window and its 전체 선택/해제 buttons — serve both
/// the join-panel badge picker and the cooldown-overlay picker. The two run on different catalogues (167 vs
/// 249 codes) and different keys, so nothing else can be shared.</summary>
public sealed class SkillJobGroupViewModel
{
    private readonly ISkillVisibility _visibility;
    private readonly Action _onChanged;
    private readonly Func<int, string> _nameOf;

    public SkillJobGroupViewModel(GroupedJobSkills group, ISkillVisibility visibility, Action onChanged, Func<int, string> nameOf)
    {
        _visibility = visibility;
        _onChanged = onChanged;
        _nameOf = nameOf;
        Job = group.Job;
        JobIcon = JoinIcons.Job(group.Job);
        NormalChips = group.NormalSkills.Select(c => Chip(c)).ToList();
        StigmaChips = group.StigmaSkills.Select(c => Chip(c)).ToList();
    }

    public string Job { get; }
    public ImageSource? JobIcon { get; }
    public IReadOnlyList<SkillChipViewModel> NormalChips { get; }
    public IReadOnlyList<SkillChipViewModel> StigmaChips { get; }
    public bool HasNormal => NormalChips.Count > 0;
    public bool HasStigma => StigmaChips.Count > 0;

    public void SelectAll() => SetAll(true);
    public void DeselectAll() => SetAll(false);

    /// <summary>Re-read every chip in this group from the shared set. See <see cref="SkillSettingsViewModel.Refresh"/>.</summary>
    public void Refresh()
    {
        foreach (SkillChipViewModel chip in NormalChips.Concat(StigmaChips))
        {
            chip.Refresh();
        }
    }

    private void SetAll(bool on)
    {
        IEnumerable<int> all = NormalChips.Concat(StigmaChips).Select(c => c.Code);
        _visibility.SetMany(all, on);
        Refresh();
        _onChanged();
    }

    private SkillChipViewModel Chip(int code) => new(code, _visibility, _onChanged, _nameOf(code));
}

public sealed class SkillChipViewModel : INotifyPropertyChanged
{
    private readonly ISkillVisibility _visibility;
    private readonly Action _onChanged;

    public SkillChipViewModel(int code, ISkillVisibility visibility, Action onChanged, string name)
    {
        _visibility = visibility;
        _onChanged = onChanged;
        Code = code;
        Name = name;
        Icon = JoinIcons.Skill(code);
    }

    public int Code { get; }
    public string Name { get; }
    public ImageSource? Icon { get; }

    public bool IsVisible
    {
        get => _visibility.IsVisible(Code);
        set
        {
            _visibility.Set(Code, value);
            _onChanged();
            OnPropertyChanged();
        }
    }

    /// <summary>Re-read after a group bulk-toggle.</summary>
    public void Refresh() => OnPropertyChanged(nameof(IsVisible));

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

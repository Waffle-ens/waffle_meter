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
            .Select(g => new SkillJobGroupViewModel(g, visibility, () => Changed?.Invoke()))
            .ToList();
    }

    public IReadOnlyList<SkillJobGroupViewModel> Groups { get; }

    /// <summary>Raised after any toggle (App syncs the join panel's visible set + reconciles).</summary>
    public event Action? Changed;
}

public sealed class SkillJobGroupViewModel
{
    private readonly SkillVisibility _visibility;
    private readonly Action _onChanged;

    public SkillJobGroupViewModel(GroupedJobSkills group, SkillVisibility visibility, Action onChanged)
    {
        _visibility = visibility;
        _onChanged = onChanged;
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

    private void SetAll(bool on)
    {
        IEnumerable<int> all = NormalChips.Concat(StigmaChips).Select(c => c.Code);
        _visibility.SetMany(all, on);
        foreach (SkillChipViewModel chip in NormalChips.Concat(StigmaChips))
        {
            chip.Refresh();
        }

        _onChanged();
    }

    private SkillChipViewModel Chip(int code) => new(code, _visibility, _onChanged);
}

public sealed class SkillChipViewModel : INotifyPropertyChanged
{
    private readonly SkillVisibility _visibility;
    private readonly Action _onChanged;

    public SkillChipViewModel(int code, SkillVisibility visibility, Action onChanged)
    {
        _visibility = visibility;
        _onChanged = onChanged;
        Code = code;
        Name = SkillCatalog.GetName(code) ?? code.ToString();
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

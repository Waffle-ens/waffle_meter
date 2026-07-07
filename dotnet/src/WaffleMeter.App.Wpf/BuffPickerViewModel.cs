using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using WaffleMeter.App.Core;
using WaffleMeter.Data;

namespace WaffleMeter.App.Wpf;

/// <summary>Per-job buff picker: lists every buff observed on the local player / party, grouped by job,
/// with an icon and a show/hide toggle. Unchecked buffs go into the persisted hidden set, which the
/// combat-assist overlay suppresses. Consumable/item buffs never reach this list (the data layer keeps only
/// job-skill codes), so this is a pure "which of my class buffs do I want to see" control.</summary>
public sealed class BuffPickerViewModel : INotifyPropertyChanged
{
    private readonly DataManager _data;
    private readonly MeterSettings _settings;
    private readonly HashSet<int> _hidden;

    public BuffPickerViewModel(DataManager data, MeterSettings settings)
    {
        _data = data;
        _settings = settings;
        _hidden = MeterSettings.ParseCodeSet(settings.BuffUiHidden);
        Rebuild();
        _data.BuffCatalogChanged += OnCatalogChanged;
    }

    public ObservableCollection<BuffJobGroup> Groups { get; } = new();

    private bool _isEmpty;
    /// <summary>True until at least one buff has been observed (shows the "play to populate" hint).</summary>
    public bool IsEmpty { get => _isEmpty; private set => Set(ref _isEmpty, value); }

    // Job display order (prefix 11..19), so groups read top-to-bottom in the familiar class order.
    private static readonly string[] JobOrder =
    {
        "검성", "수호성", "살성", "궁성", "마도성", "정령성", "치유성", "호법성", "권성", "기타",
    };

    private void OnCatalogChanged() => Rebuild();

    private void Rebuild()
    {
        var catalog = _data.BuffPickerCatalog();
        var byJob = catalog
            .GroupBy(c => c.Job)
            .OrderBy(g => Array.IndexOf(JobOrder, g.Key) is int i and >= 0 ? i : int.MaxValue);

        Groups.Clear();
        foreach (var g in byJob)
        {
            var group = new BuffJobGroup(g.Key);
            foreach ((int baseCode, string name, _, _) in g.OrderBy(c => c.BaseCode))
            {
                group.Items.Add(new BuffPickerItem(baseCode, name, !_hidden.Contains(baseCode), OnItemToggled));
            }

            Groups.Add(group);
        }

        IsEmpty = catalog.Count == 0;
    }

    private void OnItemToggled(int baseCode, bool show)
    {
        if (show)
        {
            _hidden.Remove(baseCode);
        }
        else
        {
            _hidden.Add(baseCode);
        }

        Persist();
    }

    /// <summary>Toggle every buff in one job group on/off at once.</summary>
    public void SetGroup(BuffJobGroup group, bool show)
    {
        foreach (BuffPickerItem item in group.Items)
        {
            item.SetShownSilently(show);
            if (show)
            {
                _hidden.Remove(item.BaseCode);
            }
            else
            {
                _hidden.Add(item.BaseCode);
            }
        }

        Persist();
    }

    private void Persist()
    {
        _settings.BuffUiHidden = string.Join(",", _hidden);
        _data.SetHiddenBuffBases(_hidden);
    }

    public void Dispose() => _data.BuffCatalogChanged -= OnCatalogChanged;

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

/// <summary>One job's buffs in the picker.</summary>
public sealed class BuffJobGroup
{
    public BuffJobGroup(string job) => Job = job;
    public string Job { get; }
    public ObservableCollection<BuffPickerItem> Items { get; } = new();
}

/// <summary>One toggleable buff row: icon + name + a show/hide checkbox.</summary>
public sealed class BuffPickerItem : INotifyPropertyChanged
{
    private readonly Action<int, bool> _onToggled;
    private bool _suppress;

    public BuffPickerItem(int baseCode, string name, bool shown, Action<int, bool> onToggled)
    {
        BaseCode = baseCode;
        Name = name;
        IconSource = JoinIcons.Skill(baseCode);
        _shown = shown;
        _onToggled = onToggled;
    }

    public int BaseCode { get; }
    public string Name { get; }
    public ImageSource? IconSource { get; }

    private bool _shown;
    public bool Shown
    {
        get => _shown;
        set
        {
            if (_shown == value)
            {
                return;
            }

            _shown = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Shown)));
            if (!_suppress)
            {
                _onToggled(BaseCode, value);
            }
        }
    }

    /// <summary>Set the toggle without firing the per-item callback (used by the group-level toggle, which
    /// persists once for the whole group).</summary>
    public void SetShownSilently(bool value)
    {
        _suppress = true;
        Shown = value;
        _suppress = false;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

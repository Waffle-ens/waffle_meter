using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using WaffleMeter.App.Core;
using WaffleMeter.Data;

namespace WaffleMeter.App.Wpf;

/// <summary>Per-job buff picker: every buff observed on the local player / party, grouped by job, each with a
/// 3-way mode — 알림끔 (hidden) / 오버레이만 (shown) / 오버레이+음성 (shown + voice alert). Consumable/item buffs
/// never reach this list (the data layer keeps only job-skill codes). Modes persist to two sets: hidden codes
/// (Off) and voice codes (Voice); a code in neither is Overlay-only.</summary>
public sealed class BuffPickerViewModel : INotifyPropertyChanged
{
    private readonly DataManager _data;
    private readonly MeterSettings _settings;
    private readonly HashSet<int> _hidden;
    private readonly HashSet<int> _voice;

    public BuffPickerViewModel(DataManager data, MeterSettings settings)
    {
        _data = data;
        _settings = settings;
        _hidden = MeterSettings.ParseCodeSet(settings.BuffUiHidden);
        _voice = MeterSettings.ParseCodeSet(settings.BuffUiVoice);
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
                // Skip buffs with no bundled icon — they clutter the list and can't render an icon.
                ImageSource? icon = JoinIcons.Skill(baseCode);
                if (icon is null)
                {
                    continue;
                }

                group.Items.Add(new BuffPickerItem(baseCode, name, ModeOf(baseCode), OnItemModeChanged, icon));
            }

            if (group.Items.Count > 0)
            {
                Groups.Add(group);
            }
        }

        IsEmpty = Groups.Count == 0;
    }

    private int ModeOf(int baseCode) => _hidden.Contains(baseCode) ? BuffPickerItem.Off
        : _voice.Contains(baseCode) ? BuffPickerItem.Voice
        : BuffPickerItem.Overlay;

    private void OnItemModeChanged(int baseCode, int mode)
    {
        Apply(baseCode, mode);
        Persist();
    }

    private void Apply(int baseCode, int mode)
    {
        _hidden.Remove(baseCode);
        _voice.Remove(baseCode);
        if (mode == BuffPickerItem.Off)
        {
            _hidden.Add(baseCode);
        }
        else if (mode == BuffPickerItem.Voice)
        {
            _voice.Add(baseCode);
        }
    }

    /// <summary>Set every buff in one job group to the same mode at once.</summary>
    public void SetGroup(BuffJobGroup group, int mode)
    {
        foreach (BuffPickerItem item in group.Items)
        {
            item.SetModeSilently(mode);
            Apply(item.BaseCode, mode);
        }

        Persist();
    }

    private void Persist()
    {
        _settings.BuffUiHidden = string.Join(",", _hidden);
        _settings.BuffUiVoice = string.Join(",", _voice);
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

/// <summary>One buff row: icon + name + a 3-way mode (bound to a ComboBox SelectedIndex).</summary>
public sealed class BuffPickerItem : INotifyPropertyChanged
{
    public const int Off = 0;      // 알림끔
    public const int Overlay = 1;  // 오버레이만
    public const int Voice = 2;    // 오버레이+음성

    private readonly Action<int, int> _onModeChanged;
    private bool _suppress;

    public BuffPickerItem(int baseCode, string name, int mode, Action<int, int> onModeChanged, ImageSource? icon)
    {
        BaseCode = baseCode;
        Name = name;
        IconSource = icon;
        _mode = mode;
        _onModeChanged = onModeChanged;
    }

    public int BaseCode { get; }
    public string Name { get; }
    public ImageSource? IconSource { get; }

    private int _mode;
    /// <summary>0 = 알림끔, 1 = 오버레이만, 2 = 오버레이+음성 (bound to ComboBox.SelectedIndex).</summary>
    public int Mode
    {
        get => _mode;
        set
        {
            if (_mode == value || value is < Off or > Voice)
            {
                return; // ignore the transient -1 a ComboBox can push during template init
            }

            _mode = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Mode)));
            if (!_suppress)
            {
                _onModeChanged(BaseCode, value);
            }
        }
    }

    /// <summary>Set the mode without firing the per-item callback (group toggle persists once for the group).</summary>
    public void SetModeSilently(int value)
    {
        _suppress = true;
        Mode = value;
        _suppress = false;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

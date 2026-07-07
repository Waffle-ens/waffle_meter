using System.Collections.ObjectModel;
using System.ComponentModel;
using WaffleMeter.App.Core;

namespace WaffleMeter.App.Wpf;

/// <summary>Field-boss alarm picker: the known bosses grouped by realm, each with an alert on/off toggle.
/// Unchecked bosses go into the persisted disabled set, which the reminder skips.</summary>
public sealed class FieldBossPickerViewModel
{
    private readonly MeterSettings _settings;
    private readonly HashSet<int> _disabled;

    public FieldBossPickerViewModel(MeterSettings settings)
    {
        _settings = settings;
        _disabled = settings.FieldBossDisabledCodes;

        foreach (IGrouping<string, (int Code, string Name, string Realm)> g in FieldBossCatalog.All().GroupBy(b => b.Realm))
        {
            var group = new FieldBossGroup(g.Key);
            foreach ((int code, string name, _) in g)
            {
                group.Items.Add(new FieldBossItem(code, name, !_disabled.Contains(code), OnToggled));
            }

            Groups.Add(group);
        }
    }

    public ObservableCollection<FieldBossGroup> Groups { get; } = new();

    private void OnToggled(int code, bool alerted)
    {
        if (alerted)
        {
            _disabled.Remove(code);
        }
        else
        {
            _disabled.Add(code);
        }

        _settings.FieldBossDisabled = string.Join(",", _disabled);
    }

    /// <summary>Toggle every boss in one realm group at once.</summary>
    public void SetGroup(FieldBossGroup group, bool alerted)
    {
        foreach (FieldBossItem item in group.Items)
        {
            item.SetAlertedSilently(alerted);
            if (alerted)
            {
                _disabled.Remove(item.Code);
            }
            else
            {
                _disabled.Add(item.Code);
            }
        }

        _settings.FieldBossDisabled = string.Join(",", _disabled);
    }
}

public sealed class FieldBossGroup
{
    public FieldBossGroup(string realm) => Realm = realm;
    public string Realm { get; }
    public ObservableCollection<FieldBossItem> Items { get; } = new();
}

public sealed class FieldBossItem : INotifyPropertyChanged
{
    private readonly Action<int, bool> _onToggled;
    private bool _suppress;

    public FieldBossItem(int code, string name, bool alerted, Action<int, bool> onToggled)
    {
        Code = code;
        Name = name;
        _alerted = alerted;
        _onToggled = onToggled;
    }

    public int Code { get; }
    public string Name { get; }

    private bool _alerted;
    public bool Alerted
    {
        get => _alerted;
        set
        {
            if (_alerted == value)
            {
                return;
            }

            _alerted = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Alerted)));
            if (!_suppress)
            {
                _onToggled(Code, value);
            }
        }
    }

    public void SetAlertedSilently(bool value)
    {
        _suppress = true;
        Alerted = value;
        _suppress = false;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

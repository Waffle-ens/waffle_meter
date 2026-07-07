using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace WaffleMeter.App.Wpf;

/// <summary>View model for the combat-assist overlay: the local player's active buff slots, refreshed on a
/// timer from the data layer. Slots are reconciled in place (by code) so the icons don't flicker.</summary>
public sealed class BuffOverlayViewModel : INotifyPropertyChanged
{
    public ObservableCollection<BuffSlotVM> Slots { get; } = new();

    private Visibility _emptyVisibility = Visibility.Visible;
    /// <summary>Shown ("버프 없음" placeholder) when there are no active slots.</summary>
    public Visibility EmptyVisibility { get => _emptyVisibility; private set => Set(ref _emptyVisibility, value); }

    /// <summary>Replace the slot list from a fresh snapshot (code, name, remainingMs, byOther), reusing
    /// existing rows by code so only the countdown text changes on a normal tick.</summary>
    public void Update(IReadOnlyList<(int Code, string Name, long RemainingMs, bool ByOther)> buffs)
    {
        // remove slots no longer present
        for (int i = Slots.Count - 1; i >= 0; i--)
        {
            if (!buffs.Any(b => b.Code == Slots[i].Code))
            {
                Slots.RemoveAt(i);
            }
        }

        foreach ((int code, string name, long remainingMs, bool byOther) in buffs)
        {
            BuffSlotVM? existing = Slots.FirstOrDefault(s => s.Code == code);
            if (existing is null)
            {
                Slots.Add(new BuffSlotVM(code, name, remainingMs, byOther));
            }
            else
            {
                existing.SetRemaining(remainingMs);
            }
        }

        EmptyVisibility = Slots.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
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

/// <summary>One buff slot: icon + name + a live remaining-time countdown.</summary>
public sealed class BuffSlotVM : INotifyPropertyChanged
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public BuffSlotVM(int code, string name, long remainingMs, bool byOther)
    {
        Code = code;
        Name = name;
        IconSource = JoinIcons.Skill(code);
        ByOther = byOther;
        SetRemaining(remainingMs);
    }

    public int Code { get; }
    public string Name { get; }
    public ImageSource? IconSource { get; }
    public bool ByOther { get; }

    private string _remainingText = string.Empty;
    public string RemainingText { get => _remainingText; private set => Set(ref _remainingText, value); }

    public void SetRemaining(long remainingMs)
    {
        long s = Math.Max(0, remainingMs) / 1000;
        RemainingText = s >= 60 ? $"{s / 60}:{s % 60:D2}" : s.ToString(Inv) + "s";
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

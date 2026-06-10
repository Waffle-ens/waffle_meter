using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using WaffleMeter.Data;

namespace WaffleMeter.App.Wpf;

/// <summary>
/// View model for the overlay. <see cref="Update"/> is called on the WPF dispatcher (the engine
/// raises its report on the consumer thread; App marshals it here) and reconciles the row list from
/// the live DPS report.
/// </summary>
public sealed class OverlayViewModel : INotifyPropertyChanged
{
    public OverlayViewModel(string version)
    {
        _status = $"waffle_meter {version}";
    }

    public ObservableCollection<RowViewModel> Rows { get; } = new();

    private string _targetName = "-";
    public string TargetName
    {
        get => _targetName;
        private set => Set(ref _targetName, value);
    }

    private string _duration = "0.0s";
    public string Duration
    {
        get => _duration;
        private set => Set(ref _duration, value);
    }

    private string _status;
    public string Status
    {
        get => _status;
        set => Set(ref _status, value);
    }

    public void Update(DpsReport report)
    {
        TargetName = report.Target?.Mob.Name ?? "-";
        long durationMs = Math.Max(report.BattleEnd - report.BattleStart, 0);
        Duration = $"{durationMs / 1000.0:F1}s";

        List<KeyValuePair<int, DpsInformation>> ordered = report.Information
            .OrderByDescending(kv => kv.Value.Amount)
            .ToList();

        // Reconcile in place so existing rows update (and selection/scroll are preserved) rather
        // than flickering on a full clear. Rows are kept in the same descending order as 'ordered'.
        for (int i = 0; i < ordered.Count; i++)
        {
            KeyValuePair<int, DpsInformation> entry = ordered[i];
            User? user = report.Contributors.FirstOrDefault(c => c.Id == entry.Key);
            var row = new RowViewModel(
                Id: entry.Key,
                Name: user?.Nickname ?? entry.Key.ToString(),
                Job: user?.Job?.ClassName() ?? string.Empty,
                Amount: entry.Value.Amount.ToString("N0"),
                Dps: $"{entry.Value.Dps:N0}/s",
                Contribution: $"{entry.Value.Contribution:F1}%");

            if (i < Rows.Count)
            {
                Rows[i] = row;
            }
            else
            {
                Rows.Add(row);
            }
        }

        for (int i = Rows.Count - 1; i >= ordered.Count; i--)
        {
            Rows.RemoveAt(i);
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
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public sealed record RowViewModel(int Id, string Name, string Job, string Amount, string Dps, string Contribution);

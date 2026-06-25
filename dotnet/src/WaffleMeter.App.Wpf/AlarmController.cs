using System.Globalization;
using System.Windows.Threading;
using WaffleMeter.App.Core;

namespace WaffleMeter.App.Wpf;

/// <summary>
/// App-scoped clock that fires the 슈고 페스타 reminder. A 1s <see cref="DispatcherTimer"/> (UI thread)
/// evaluates the schedule once per wall-clock minute — the first tick that observes a new minute, so it is
/// robust to timer drift — and invokes <c>onShugo(lead)</c> when a configured lead is due. Settings are read
/// live, so turning the alarm off stops future fires without a restart. Mirrors OverlayController's
/// long-lived UI-thread timer.
/// </summary>
public sealed class AlarmController
{
    private readonly MeterSettings _settings;
    private readonly Action<int> _onShugo;
    private readonly Action<CustomAlarm> _onCustom;
    private readonly Func<DateTime> _now;
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
    private string _lastMinute = string.Empty;

    public AlarmController(MeterSettings settings, Action<int> onShugo, Action<CustomAlarm> onCustom, Func<DateTime>? now = null)
    {
        _settings = settings;
        _onShugo = onShugo;
        _onCustom = onCustom;
        _now = now ?? (() => DateTime.Now);
        _timer.Tick += (_, _) => Poll();
    }

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();

    private void Poll()
    {
        DateTime now = _now();
        string minute = now.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);
        if (minute == _lastMinute)
        {
            return; // already evaluated this minute
        }

        _lastMinute = minute;

        if (_settings.ShugoAlarmEnabled
            && ShugoAlarm.DueLead(now, ShugoAlarm.EnabledLeads(_settings)) is int lead)
        {
            _onShugo(lead);
        }

        foreach (CustomAlarm alarm in _settings.CustomAlarms)
        {
            if (CustomAlarmSchedule.IsDue(alarm, now))
            {
                _onCustom(alarm);
            }
        }
    }
}

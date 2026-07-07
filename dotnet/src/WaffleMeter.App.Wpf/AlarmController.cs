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
    private readonly Func<IReadOnlyDictionary<int, long>>? _fieldBossTimers;
    private readonly Action<FieldBossAlarm.Due>? _onFieldBoss;
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
    private string _lastMinute = string.Empty;
    // dedup: each (boss, respawn, lead) fires once. Value = the respawn target ms, so entries can be pruned
    // once the respawn has passed — bounding the map without a blanket clear (which would re-fire other
    // alerts still inside their lead window).
    private readonly Dictionary<string, long> _shownFieldBoss = new();

    public AlarmController(
        MeterSettings settings,
        Action<int> onShugo,
        Action<CustomAlarm> onCustom,
        Func<DateTime>? now = null,
        Func<IReadOnlyDictionary<int, long>>? fieldBossTimers = null,
        Action<FieldBossAlarm.Due>? onFieldBoss = null)
    {
        _settings = settings;
        _onShugo = onShugo;
        _onCustom = onCustom;
        _now = now ?? (() => DateTime.Now);
        _fieldBossTimers = fieldBossTimers;
        _onFieldBoss = onFieldBoss;
        _timer.Tick += (_, _) => Poll();
    }

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();

    private void Poll()
    {
        DateTime now = _now();

        // Field-boss reminder: evaluate EVERY second (not the minute gate) so a lead fires at the right
        // time, de-duplicated by (boss, respawn, lead) so it fires once.
        EvaluateFieldBoss(now);

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

    private void EvaluateFieldBoss(DateTime now)
    {
        if (!_settings.FieldBossAlarmEnabled || _fieldBossTimers is null || _onFieldBoss is null)
        {
            return;
        }

        long nowMs = new DateTimeOffset(now).ToUnixTimeMilliseconds();

        // Prune entries whose respawn has passed: they can never be due again (DueAlerts skips remaining<=0),
        // so dropping them bounds the map without ever clearing a still-active lead marker.
        if (_shownFieldBoss.Count > 0)
        {
            foreach (string key in _shownFieldBoss.Where(kv => kv.Value <= nowMs).Select(kv => kv.Key).ToList())
            {
                _shownFieldBoss.Remove(key);
            }
        }

        foreach (FieldBossAlarm.Due d in FieldBossAlarm.DueAlerts(_fieldBossTimers(), nowMs, _settings.FieldBossLeads))
        {
            if (_shownFieldBoss.TryAdd(FieldBossAlarm.Key(d), d.TargetMs))
            {
                _onFieldBoss(d);
            }
        }
    }
}

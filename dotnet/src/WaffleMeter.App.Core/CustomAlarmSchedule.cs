namespace WaffleMeter.App.Core;

/// <summary>
/// Pure schedule test for a <see cref="CustomAlarm"/>. Side-effect-free and WPF-free so it is unit-testable;
/// the AlarmController evaluates it once per wall-clock minute.
/// </summary>
public static class CustomAlarmSchedule
{
    /// <summary>True when <paramref name="a"/> is due exactly at <paramref name="now"/> — matching hour and
    /// minute and, if any days are set, the weekday (empty days = every day). Disabled alarms never fire.</summary>
    public static bool IsDue(CustomAlarm a, DateTime now)
        => a.Enabled
           && now.Hour == a.Hour
           && now.Minute == a.Minute
           && (a.Days.Count == 0 || a.Days.Contains((int)now.DayOfWeek));
}

namespace WaffleMeter.App.Core;

/// <summary>
/// Pure schedule logic shared by the reminders that hang off the top of the hour (HH:00): given the enabled
/// lead minutes, which one — if any — is due in the current minute. WPF-free and side-effect-free.
/// </summary>
public static class HourlyAlarm
{
    /// <summary>
    /// The lead (minutes-before-the-hour) that is due exactly at <paramref name="now"/>, or null if none of
    /// <paramref name="enabledLeads"/> matches this minute. 0 = the top of the hour itself. At most one lead
    /// can be due in any given minute.
    /// </summary>
    public static int? DueLead(DateTime now, IReadOnlyCollection<int> enabledLeads)
    {
        int minutesUntilHour = now.Minute == 0 ? 0 : 60 - now.Minute;
        return enabledLeads.Contains(minutesUntilHour) ? minutesUntilHour : null;
    }
}

namespace WaffleMeter.App.Core;

/// <summary>
/// Pure schedule logic for the 슈고 페스타 reminder. The in-game event recurs on the top of every hour
/// (HH:00); the alarm fires at the configured lead times before it (10 / 5 / 1 min) and at the start (0).
/// Kept WPF-free and side-effect-free so it is unit-testable; the AlarmController polls it once per minute.
/// </summary>
public static class ShugoAlarm
{
    /// <summary>
    /// The lead (minutes-before-the-hour) that is due exactly at <paramref name="now"/>, or null if none of
    /// <paramref name="enabledLeads"/> matches this minute. 0 = the event start (HH:00). At most one lead can
    /// be due in any given minute.
    /// </summary>
    public static int? DueLead(DateTime now, IReadOnlyCollection<int> enabledLeads)
    {
        int minutesUntilHour = now.Minute == 0 ? 0 : 60 - now.Minute;
        return enabledLeads.Contains(minutesUntilHour) ? minutesUntilHour : null;
    }

    /// <summary>The set of enabled lead minutes from settings (0 = start).</summary>
    public static IReadOnlyCollection<int> EnabledLeads(MeterSettings s)
    {
        var leads = new HashSet<int>();
        if (s.ShugoLead10)
        {
            leads.Add(10);
        }

        if (s.ShugoLead5)
        {
            leads.Add(5);
        }

        if (s.ShugoLead1)
        {
            leads.Add(1);
        }

        if (s.ShugoLeadStart)
        {
            leads.Add(0);
        }

        return leads;
    }
}

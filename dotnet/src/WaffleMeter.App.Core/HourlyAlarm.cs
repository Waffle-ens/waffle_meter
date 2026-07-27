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

/// <summary>
/// The 감시자 카이라 (어비스 하층) reminder. This boss is the one field boss the server never times: its
/// 0x9101 record arrives with a zeroed timestamp in every capture we have. It spawns on the hour and it is
/// not guaranteed to spawn at all, so the point of the alert is to be standing there BEFORE the hour turns
/// — which is why it is a clock-based reminder of its own rather than a row in the boss picker, and why it
/// fires wherever you are instead of only inside the abyss.
/// </summary>
public static class KairaAlarm
{
    /// <summary>The lead minutes enabled in settings (10 / 5 / 1 minutes before the hour).</summary>
    public static IReadOnlyCollection<int> EnabledLeads(MeterSettings s)
    {
        var leads = new HashSet<int>();
        if (s.KairaLead10)
        {
            leads.Add(10);
        }

        if (s.KairaLead5)
        {
            leads.Add(5);
        }

        if (s.KairaLead1)
        {
            leads.Add(1);
        }

        return leads;
    }
}

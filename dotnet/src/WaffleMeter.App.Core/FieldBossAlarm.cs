namespace WaffleMeter.App.Core;

/// <summary>
/// Pure schedule logic for the field-boss respawn reminder. Given the current timer table (boss code →
/// target Unix-ms) and the configured lead minutes, it returns which (boss, lead) alerts are due right now:
/// a lead L is due while the remaining time is in <c>(L-1, L]</c> minutes. WPF-free and side-effect-free;
/// the caller (AlarmController) polls it each second and de-duplicates by (code, targetMs, lead) so each
/// alert fires once. Mirrors the shape of <see cref="ShugoAlarm"/>.
/// </summary>
public static class FieldBossAlarm
{
    /// <summary>One due reminder: a boss, its respawn target, and the lead (minutes-before) that just hit.</summary>
    public readonly record struct Due(int Code, long TargetMs, int LeadMinutes);

    /// <summary>Alerts due at <paramref name="nowMs"/> for the given timers and enabled lead minutes.</summary>
    public static IReadOnlyList<Due> DueAlerts(
        IReadOnlyDictionary<int, long> timers, long nowMs, IReadOnlyCollection<int> leadMinutes)
    {
        var due = new List<Due>();
        if (timers.Count == 0 || leadMinutes.Count == 0)
        {
            return due;
        }

        foreach ((int code, long targetMs) in timers)
        {
            long remainingMs = targetMs - nowMs;
            if (remainingMs <= 0)
            {
                continue; // already spawned
            }

            foreach (int lead in leadMinutes)
            {
                long hi = lead * 60_000L;          // L minutes
                long lo = (lead - 1) * 60_000L;    // L-1 minutes
                if (remainingMs > lo && remainingMs <= hi)
                {
                    due.Add(new Due(code, targetMs, lead));
                }
            }
        }

        return due;
    }

    /// <summary>A stable de-dup key so a given (boss, respawn, lead) alert fires only once.</summary>
    public static string Key(Due d) => $"{d.Code}:{d.TargetMs}:{d.LeadMinutes}";
}

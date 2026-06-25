namespace WaffleMeter.Data;

/// <summary>
/// Pure interval-coverage math for buff/debuff 가동률 (uptime). Given buff application intervals
/// (each <c>[Start, End]</c> in ms) and a fight window <c>[windowStart, windowEnd]</c>, returns the total
/// covered milliseconds with overlapping or exactly-touching applications merged, so a stacked or
/// re-applied buff is never double-counted. Extracted from <see cref="DpsCalculator.GetBuffOperatingRate"/>
/// so the merge is unit-testable in isolation.
/// </summary>
public static class BuffUptime
{
    /// <summary>
    /// Clamp each interval to <c>[windowStart, windowEnd]</c>, drop empties, sort by start, union
    /// overlapping or exactly-touching intervals, and sum their lengths. Two intervals separated by a real
    /// gap (the next start strictly after the previous end) stay distinct — matching the historical behaviour.
    /// </summary>
    public static long CoveredMs(IEnumerable<(long Start, long End)> intervals, long windowStart, long windowEnd)
    {
        if (windowEnd <= windowStart)
        {
            return 0;
        }

        List<(long Start, long End)> clamped = intervals
            .Select(iv => (Start: Math.Max(iv.Start, windowStart), End: Math.Min(iv.End, windowEnd)))
            .Where(iv => iv.End > iv.Start)
            .OrderBy(iv => iv.Start)
            .ToList();

        long covered = 0;
        bool open = false;
        long curStart = 0, curEnd = 0;
        foreach ((long start, long end) in clamped)
        {
            if (!open)
            {
                curStart = start;
                curEnd = end;
                open = true;
            }
            else if (start > curEnd)
            {
                // real gap — close the current run and open a new one.
                covered += curEnd - curStart;
                curStart = start;
                curEnd = end;
            }
            else
            {
                // overlapping or exactly touching — extend the current run.
                curEnd = Math.Max(curEnd, end);
            }
        }

        if (open)
        {
            covered += curEnd - curStart;
        }

        return covered;
    }
}

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
    public static long CoveredMs(IEnumerable<(long Start, long End)> intervals, long windowStart, long windowEnd) =>
        MergeIntervals(intervals, windowStart, windowEnd).Sum(iv => iv.End - iv.Start);

    /// <summary>
    /// The same clamp → sort → union that <see cref="CoveredMs"/> sums over, but returns the merged runs
    /// themselves (each <c>[Start, End]</c>, in order) instead of just their total length. Used to draw a
    /// buff's applied intervals on the DPS-graph timeline — <c>CoveredMs</c> answers "how long" while this
    /// answers "when". Overlapping or exactly-touching applications merge into one run; a real gap (next start
    /// strictly after the previous end) opens a new run. Returns an empty list for an empty/zero window.
    /// </summary>
    public static List<(long Start, long End)> MergeIntervals(
        IEnumerable<(long Start, long End)> intervals, long windowStart, long windowEnd)
    {
        var merged = new List<(long Start, long End)>();
        if (windowEnd <= windowStart)
        {
            return merged;
        }

        List<(long Start, long End)> clamped = intervals
            .Select(iv => (Start: Math.Max(iv.Start, windowStart), End: Math.Min(iv.End, windowEnd)))
            .Where(iv => iv.End > iv.Start)
            .OrderBy(iv => iv.Start)
            .ToList();

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
                merged.Add((curStart, curEnd));
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
            merged.Add((curStart, curEnd));
        }

        return merged;
    }
}

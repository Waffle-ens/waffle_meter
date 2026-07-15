using System.Globalization;

namespace WaffleMeter.App.Core;

/// <summary>
/// Bounds the on-disk replay folder. The replay engine appends one <c>replay-{startMs}.json</c> per battle
/// and never removes any, so an always-on recorder would grow without limit. After each battle is built we
/// keep only the <see cref="DefaultKeep"/> most recent recordings and delete the rest. Recency is the
/// battle-start epoch encoded in the filename (falling back to last-write time for an unparseable name), so
/// pruning is independent of file-timestamp skew and is trivially unit-testable. Best-effort: a delete that
/// fails (a replay window transiently holding the file, AV, already gone) is swallowed and retried on the
/// next battle — retention must never disturb the capture-consumer thread that logs battles.
/// </summary>
public static class ReplayRetention
{
    /// <summary>How many of the most recent recordings to keep on disk.</summary>
    public const int DefaultKeep = 50;

    private const string Prefix = "replay-";
    private const string Suffix = ".json";

    /// <summary>Delete all but the <paramref name="keep"/> most recent <c>replay-*.json</c> files in
    /// <paramref name="replayDir"/>. Returns the number of files deleted. Never throws.</summary>
    public static int Prune(string replayDir, int keep = DefaultKeep)
    {
        if (keep < 0)
        {
            keep = 0;
        }

        try
        {
            var dir = new DirectoryInfo(replayDir);
            if (!dir.Exists)
            {
                return 0;
            }

            FileInfo[] files = dir.GetFiles(Prefix + "*" + Suffix);
            if (files.Length <= keep)
            {
                return 0;
            }

            int deleted = 0;
            foreach (FileInfo file in files.OrderByDescending(SortKey).Skip(keep)) // newest first; drop the tail
            {
                try
                {
                    file.Delete();
                    deleted++;
                }
                catch
                {
                    // locked / AV / already removed — the next battle's prune retries
                }
            }

            return deleted;
        }
        catch
        {
            return 0; // a missing/unreadable directory just means nothing to prune
        }
    }

    // Recency: prefer the battle-start epoch in the filename; tie-break (and fall back for an unparseable
    // name) on last-write time so ordering is always total and deterministic.
    private static (long StartMs, long WriteTicks) SortKey(FileInfo file)
        => (ParseStartMs(file.Name), file.LastWriteTimeUtc.Ticks);

    // "replay-1720000000000.json" -> 1720000000000; long.MinValue when the name isn't in that shape, so a
    // stray/corrupt file sorts oldest (pruned first) rather than displacing a real recording.
    private static long ParseStartMs(string fileName)
    {
        if (fileName.Length <= Prefix.Length + Suffix.Length
            || !fileName.StartsWith(Prefix, StringComparison.Ordinal)
            || !fileName.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase))
        {
            return long.MinValue;
        }

        string middle = fileName[Prefix.Length..^Suffix.Length];
        return long.TryParse(middle, NumberStyles.None, CultureInfo.InvariantCulture, out long ms)
            ? ms
            : long.MinValue;
    }
}

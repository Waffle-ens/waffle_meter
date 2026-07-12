using System.Globalization;
using WaffleMeter.Data;
using WaffleMeter.Replay;
using WaffleMeter.Services;

namespace WaffleMeter.App.Core;

/// <summary>
/// One-line-per-battle replay diagnostics (<c>replay-diag.log</c> next to settings.properties), written
/// only while the replay tap is enabled. Each line answers a live-verification question the offline
/// corpora cannot: the line existing at all with <c>defeated=False</c> proves OnBattleLogged fires on a
/// wipe; <c>path=</c> (players with a real path / player tracks) measures the AoI ceiling on a real raid;
/// <c>self=</c> shows how dense the local player's feed is (self is excluded from the 0x371D broadcast);
/// and the raw <c>hp=</c> rides along to audit the BossDefeated inference. Size-capped and best-effort —
/// it must never disturb the capture-consumer thread that logs battles.
/// </summary>
public static class ReplayDiag
{
    private const long MaxBytes = 1_000_000;

    /// <summary>Append the per-battle line for a just-built recording. <paramref name="rosterCount"/> =
    /// party-roster size the replay was scoped to (0 = no roster → self+boss only; negative = unknown,
    /// omitted from the line).</summary>
    public static void Log(PropertyHandler props, DpsReport report, ReplayRecording rec, int rosterCount = -1)
    {
        try
        {
            Append(props, Format(report, rec, rosterCount));
        }
        catch
        {
            // diagnostics must never disturb the battle-save path
        }
    }

    /// <summary>Append a free-form marker line (e.g. whether the engine DLL loaded at startup).</summary>
    public static void Note(PropertyHandler props, string line)
    {
        try
        {
            Append(props, line);
        }
        catch
        {
        }
    }

    // Pure so it is unit-testable without touching the filesystem.
    public static string Format(DpsReport report, ReplayRecording rec, int rosterCount = -1)
    {
        List<ReplayTrack> players = rec.Tracks.Where(t => !t.IsTarget).ToList();
        int withPath = players.Count(t => t.Points.Count >= 3);
        ReplayTrack? self = players.FirstOrDefault(t => t.IsSelf);
        ReplayTrack? boss = rec.Tracks.FirstOrDefault(t => t.IsTarget);

        string selfStr = self is null ? "MISSING"
            : self.Points.Count < 3 ? $"THIN({self.Points.Count.ToString(CultureInfo.InvariantCulture)}pt)"
            : $"{self.Points.Count.ToString(CultureInfo.InvariantCulture)}pt/p90={P90GapMs(self).ToString(CultureInfo.InvariantCulture)}ms";

        string hp = report.Target is { } t
            ? string.Create(CultureInfo.InvariantCulture, $"{t.RemainHp}/{t.MaxHp}")
            : "-";

        // roster= verifies the party-only scoping live: 0 = no roster (tracks should be self+boss only).
        string roster = rosterCount >= 0
            ? string.Create(CultureInfo.InvariantCulture, $" roster={rosterCount}")
            : "";

        return string.Create(
            CultureInfo.InvariantCulture,
            $"battle start={rec.StartMs} dur={rec.DurationMs / 1000}s target={rec.TargetName ?? "?"}({(rec.TargetCode?.ToString(CultureInfo.InvariantCulture) ?? "-")}) " +
            $"defeated={rec.BossDefeated} hp={hp} contributors={report.Contributors.Count}{roster} " +
            $"path={withPath}/{players.Count} self={selfStr} boss={(boss is null ? "none" : boss.Points.Count + "pt")} " +
            $"totalPts={rec.PointCount} playerP90={AggregateP90Ms(players)}ms");
    }

    /// <summary>p90 of the gaps between consecutive path points on one track (0 when &lt; 2 points).</summary>
    private static int P90GapMs(ReplayTrack track)
    {
        if (track.Points.Count < 2)
        {
            return 0;
        }

        var gaps = new List<int>(track.Points.Count - 1);
        for (int i = 1; i < track.Points.Count; i++)
        {
            gaps.Add(track.Points[i].TMs - track.Points[i - 1].TMs);
        }

        gaps.Sort();
        return gaps[(int)(gaps.Count * 0.9)];
    }

    private static int AggregateP90Ms(List<ReplayTrack> tracks)
    {
        var gaps = new List<int>();
        foreach (ReplayTrack t in tracks.Where(t => t.Points.Count >= 3))
        {
            for (int i = 1; i < t.Points.Count; i++)
            {
                gaps.Add(t.Points[i].TMs - t.Points[i - 1].TMs);
            }
        }

        if (gaps.Count == 0)
        {
            return 0;
        }

        gaps.Sort();
        return gaps[(int)(gaps.Count * 0.9)];
    }

    private static void Append(PropertyHandler props, string line)
    {
        string path = Path.Combine(props.AppDirectory(), "replay-diag.log");
        // Rotate (not silently drop) at the cap: keep the trace bounded to ~2x MaxBytes while never losing
        // the most recent lines — a silent stop would look identical to "replay never fired" during an
        // investigation. The previous file is preserved once as .old.
        if (new FileInfo(path) is { Exists: true, Length: > MaxBytes })
        {
            string old = path + ".old";
            File.Delete(old);
            File.Move(path, old);
        }

        File.AppendAllText(
            path,
            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + " " + line + Environment.NewLine);
    }
}

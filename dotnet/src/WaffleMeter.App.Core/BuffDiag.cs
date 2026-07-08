using System.Globalization;

namespace WaffleMeter.App.Core;

/// <summary>
/// Appends buff-overlay tracking diagnostics to <c>%APPDATA%\waffle_meter.v1.4\buff-diag.log</c> (rotated at
/// 1 MB), lands next to <c>overlay-diag.log</c>. Written every few seconds from the meter consumer thread so a
/// live 10-man raid session records, per interval, whether the local player's job-buff apply/refresh frames
/// arrive and pass the executor gate (<c>self/5s</c>) versus whether the capture channel / aligner is shedding
/// segments (<c>drops</c>, <c>gapSkip/5s</c>). Never throws — diagnostics must not disturb the pipeline.
/// </summary>
public static class BuffDiag
{
    private static readonly object Gate = new();

    public static void Write(string line)
    {
        try
        {
            string path = LogPath();
            lock (Gate)
            {
                if (new FileInfo(path) is { Exists: true, Length: > 1_000_000 })
                {
                    File.WriteAllText(path, ""); // rotate: keep RECENT history rather than hard-stopping
                }

                File.AppendAllText(
                    path,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + " " + line + Environment.NewLine);
            }
        }
        catch
        {
            // diagnostics must never disturb the consumer thread
        }
    }

    public static string LogPath()
    {
        string appData = Environment.GetEnvironmentVariable("APPDATA")
                         ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string dir = Path.Combine(appData, "waffle_meter.v1.4");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "buff-diag.log");
    }
}

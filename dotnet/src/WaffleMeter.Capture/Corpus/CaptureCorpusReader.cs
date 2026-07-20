using System.IO.Compression;
using System.Text.Json;

namespace WaffleMeter.Capture.Corpus;

/// <summary>
/// Reads the Phase 0 packet-debug corpus (.jsonl or gzipped .jsonl.gz) produced by the Kotlin
/// <c>PacketDebugLogger</c> (dev/packet-logging branch) and yields the raw capture segments
/// as <see cref="CapturedSegment"/> — the same shape the live capture helper emits, so the
/// downstream pipeline (aligner → assembler → parser → DPS) is exercised identically whether
/// driven live or from a recorded session.
///
/// The corpus is one JSON object per line. We consume only <c>{"type":"capture", ...}</c>
/// records; assembled/dispatch/damage/battle/meta lines are used by other diff layers.
/// Capture record shape (PacketDebugLogger.capture):
///   { "type":"capture", "at":&lt;arrivedAt&gt;, "ip":"&lt;src&gt;", "seq":&lt;long&gt;, "len":&lt;int&gt;, "head":"..", "data":"&lt;base64&gt;" }
/// </summary>
public static class CaptureCorpusReader
{
    /// <summary>
    /// Opens a corpus file for reading, transparently decompressing gzip. Detection is by the
    /// gzip magic bytes (0x1F 0x8B), not the extension, so renamed files and the pre-gzip
    /// .jsonl archive both keep working.
    /// </summary>
    public static Stream OpenCorpusStream(string path)
    {
        var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        int b0 = file.ReadByte();
        int b1 = file.ReadByte();
        file.Position = 0;
        if (b0 == 0x1F && b1 == 0x8B)
        {
            return new GZipStream(file, CompressionMode.Decompress);
        }

        return file;
    }

    /// <summary>
    /// Enumerates corpus lines from a plain or gzipped file. A gzip tail truncated by a killed
    /// session decompresses cleanly up to the last sync flush; genuinely corrupted data ends the
    /// enumeration instead of throwing, preserving the "crash-cut corpus stays replayable"
    /// property plain .jsonl files have always had.
    /// </summary>
    public static IEnumerable<string> ReadLines(string path)
    {
        using var reader = new StreamReader(OpenCorpusStream(path));
        while (true)
        {
            string? line;
            try
            {
                line = reader.ReadLine();
            }
            catch (InvalidDataException)
            {
                yield break;
            }

            if (line is null)
            {
                yield break;
            }

            yield return line;
        }
    }

    public static IEnumerable<CapturedSegment> ReadCaptures(string jsonlPath)
    {
        foreach (string line in ReadLines(jsonlPath))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using JsonDocument doc = JsonDocument.Parse(line);
            JsonElement root = doc.RootElement;

            if (!root.TryGetProperty("type", out JsonElement typeEl) || typeEl.GetString() != "capture")
            {
                continue;
            }

            long seq = root.GetProperty("seq").GetInt64();
            long arrivedAt = root.GetProperty("at").GetInt64();
            string srcIp = root.TryGetProperty("ip", out JsonElement ipEl) ? ipEl.GetString() ?? string.Empty : string.Empty;
            byte[] payload = Convert.FromBase64String(root.GetProperty("data").GetString() ?? string.Empty);

            yield return new CapturedSegment(seq, payload, arrivedAt, srcIp);
        }
    }
}

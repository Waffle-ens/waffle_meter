using System.Text.Json;

namespace WaffleMeter.Capture.Corpus;

/// <summary>
/// Reads the Phase 0 packet-debug corpus (.jsonl) produced by the Kotlin
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
    public static IEnumerable<CapturedSegment> ReadCaptures(string jsonlPath)
    {
        foreach (string line in File.ReadLines(jsonlPath))
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

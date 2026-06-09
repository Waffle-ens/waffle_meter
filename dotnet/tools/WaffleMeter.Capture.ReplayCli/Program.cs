using System.Text;
using System.Text.Json;
using WaffleMeter.Capture;

// Phase 0 real-data parity check (Layers 0/1/2):
//   capture records  -> CapturedSegment -> srcIp-aware PacketAlignmenter -> StreamAssembler
// then diff the emitted frames against the Kotlin 'assembled' records (len + first-24-byte head)
// recorded in the SAME corpus. Mirrors Main.kt:42-54.

if (args.Length < 1)
{
    Console.Error.WriteLine("usage: dotnet run --project <ReplayCli> -- <path-to-packet-debug.jsonl>");
    return 1;
}

string path = args[0];
if (!File.Exists(path))
{
    Console.Error.WriteLine($"file not found: {path}");
    return 1;
}

var typeCounts = new Dictionary<string, int>();
var captures = new List<CapturedSegment>();
var assembledExpected = new List<(int Len, string Head)>();

long lineNo = 0;
foreach (string line in File.ReadLines(path))
{
    lineNo++;
    if (string.IsNullOrWhiteSpace(line))
    {
        continue;
    }

    try
    {
        using JsonDocument doc = JsonDocument.Parse(line);
        JsonElement root = doc.RootElement;
        if (!root.TryGetProperty("type", out JsonElement typeEl))
        {
            continue;
        }

        string type = typeEl.GetString() ?? "";
        typeCounts[type] = typeCounts.GetValueOrDefault(type) + 1;

        if (type == "capture")
        {
            long seq = root.GetProperty("seq").GetInt64();
            long at = root.GetProperty("at").GetInt64();
            string ip = root.TryGetProperty("ip", out JsonElement ipEl) ? ipEl.GetString() ?? "" : "";
            byte[] payload = Convert.FromBase64String(root.GetProperty("data").GetString() ?? "");
            captures.Add(new CapturedSegment(seq, payload, at, ip));
        }
        else if (type == "assembled")
        {
            int len = root.GetProperty("len").GetInt32();
            string head = root.TryGetProperty("head", out JsonElement h) ? h.GetString() ?? "" : "";
            assembledExpected.Add((len, head));
        }
    }
    catch (JsonException ex)
    {
        Console.Error.WriteLine($"line {lineNo}: bad json: {ex.Message}");
    }
}

// Replay (Main.kt:42-54): on srcIp change reset the aligner only; feed each aligned chunk to the assembler.
var emitted = new List<(int Len, string Head)>();
var aligner = new PacketAlignmenter();
var assembler = new StreamAssembler((packet, _) => emitted.Add((packet.Length, HexHead(packet))));
string currentIp = "";
foreach (CapturedSegment seg in captures)
{
    if (seg.SrcIp != currentIp)
    {
        currentIp = seg.SrcIp;
        aligner.Reset();
    }

    foreach (AlignedChunk chunk in aligner.Feed(seg.Seq, seg.Payload, seg.ArrivedAtMs))
    {
        assembler.ProcessChunk(chunk.Data, chunk.ArrivedAt);
    }
}

Console.WriteLine($"=== corpus: {Path.GetFileName(path)} ===");
Console.WriteLine("--- record coverage ---");
foreach (KeyValuePair<string, int> kv in typeCounts.OrderByDescending(k => k.Value))
{
    Console.WriteLine($"  {kv.Key,-18} {kv.Value}");
}

Console.WriteLine();
Console.WriteLine("--- Layer 1/2 parity: capture -> aligner -> assembler  vs  Kotlin 'assembled' ---");
Console.WriteLine($"  captures fed:           {captures.Count}");
Console.WriteLine($"  frames emitted (.NET):  {emitted.Count}");
Console.WriteLine($"  assembled (Kotlin):     {assembledExpected.Count}");

int compareN = Math.Min(emitted.Count, assembledExpected.Count);
int firstDiff = -1;
for (int i = 0; i < compareN; i++)
{
    if (emitted[i].Len != assembledExpected[i].Len || emitted[i].Head != assembledExpected[i].Head)
    {
        firstDiff = i;
        break;
    }
}

if (firstDiff >= 0)
{
    Console.WriteLine($"  RESULT: DIVERGENCE at frame #{firstDiff}");
    Console.WriteLine($"    .NET   : len={emitted[firstDiff].Len} head=[{emitted[firstDiff].Head}]");
    Console.WriteLine($"    Kotlin : len={assembledExpected[firstDiff].Len} head=[{assembledExpected[firstDiff].Head}]");
    return 2;
}

if (emitted.Count == assembledExpected.Count)
{
    Console.WriteLine($"  RESULT: PARITY OK — all {emitted.Count} frames match (len + first-24-byte head).");
    return 0;
}

Console.WriteLine($"  RESULT: PREFIX OK — first {compareN} frames match; counts differ by {Math.Abs(emitted.Count - assembledExpected.Count)} " +
                  $"({(emitted.Count > assembledExpected.Count ? ".NET emitted more — likely Kotlin stopped mid-pipeline at session end" : "Kotlin logged more — investigate")}).");
return emitted.Count > assembledExpected.Count ? 0 : 2;

static string HexHead(byte[] bytes, int maxBytes = 24)
{
    int n = Math.Min(maxBytes, bytes.Length);
    var sb = new StringBuilder(n * 3);
    for (int i = 0; i < n; i++)
    {
        if (i > 0)
        {
            sb.Append(' ');
        }

        sb.Append(bytes[i].ToString("X2"));
    }

    return sb.ToString();
}

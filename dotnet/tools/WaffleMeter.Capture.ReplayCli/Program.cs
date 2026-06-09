using System.Text;
using System.Text.Json;
using WaffleMeter.Capture;

// Phase 0 real-data parity check (Layers 0/1/2/3a):
//   capture -> CapturedSegment -> srcIp-aware PacketAlignmenter -> StreamAssembler -> StreamProcessor
// then diff against the Kotlin records recorded in the SAME corpus:
//   L1/L2: emitted frames vs 'assembled' (len + first-24-byte head)
//   L3a:   dispatched opcode sequence vs 'dispatch'; compressed_packet / unknown_opcode / parser_error counts
// Mirrors Main.kt:42-54.

if (args.Length < 1)
{
    Console.Error.WriteLine("usage: dotnet run --project <ReplayCli> -c Release [path] <path-to-packet-debug.jsonl>");
    return 1;
}

string path = args[^1];
if (!File.Exists(path))
{
    Console.Error.WriteLine($"file not found: {path}");
    return 1;
}

var typeCounts = new Dictionary<string, int>();
var captures = new List<CapturedSegment>();
var assembledExpected = new List<(int Len, string Head)>();
var dispatchExpected = new List<int>();
int compressedExpected = 0, unknownExpected = 0, parserErrorExpected = 0;

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

        switch (type)
        {
            case "capture":
                captures.Add(new CapturedSegment(
                    root.GetProperty("seq").GetInt64(),
                    Convert.FromBase64String(root.GetProperty("data").GetString() ?? ""),
                    root.GetProperty("at").GetInt64(),
                    root.TryGetProperty("ip", out JsonElement ipEl) ? ipEl.GetString() ?? "" : ""));
                break;
            case "assembled":
                assembledExpected.Add((
                    root.GetProperty("len").GetInt32(),
                    root.TryGetProperty("head", out JsonElement h) ? h.GetString() ?? "" : ""));
                break;
            case "dispatch":
                dispatchExpected.Add(root.GetProperty("opcode").GetInt32());
                break;
            case "compressed_packet":
                compressedExpected++;
                break;
            case "unknown_opcode":
                unknownExpected++;
                break;
            case "parser_error":
                parserErrorExpected++;
                break;
        }
    }
    catch (JsonException ex)
    {
        Console.Error.WriteLine($"line {lineNo}: bad json: {ex.Message}");
    }
}

// Replay (Main.kt:42-54): srcIp change resets the aligner; each aligned chunk -> assembler -> processor.
var emitted = new List<(int Len, string Head)>();
var sink = new CollectingSink();
var processor = new StreamProcessor(sink);
var aligner = new PacketAlignmenter();
var assembler = new StreamAssembler((packet, at) =>
{
    emitted.Add((packet.Length, Hex.Head(packet)));
    processor.OnPacketReceived(packet, at);
});

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

int rc = 0;

Console.WriteLine();
Console.WriteLine("--- L1/L2: capture -> aligner -> assembler  vs  Kotlin 'assembled' ---");
Console.WriteLine($"  frames emitted (.NET): {emitted.Count}   assembled (Kotlin): {assembledExpected.Count}");
rc |= ReportPairSeq("assembled frame", emitted, assembledExpected);

Console.WriteLine();
Console.WriteLine("--- L3a: dispatch opcodes (incl. decompressed inner) + compression ---");
Console.WriteLine($"  dispatched (.NET): {sink.Dispatched.Count}   dispatch (Kotlin): {dispatchExpected.Count}");
rc |= ReportIntSeq("dispatch opcode", sink.Dispatched, dispatchExpected);
rc |= ReportCount("compressed_packet", sink.Compressed, compressedExpected);
rc |= ReportCount("unknown_opcode", sink.Unknown, unknownExpected);
rc |= ReportCount("parser_error", sink.ParserErrors, parserErrorExpected);

Console.WriteLine();
Console.WriteLine(rc == 0 ? "RESULT: ALL PARITY OK" : "RESULT: DIVERGENCE (see above)");
return rc;

int ReportPairSeq(string label, List<(int Len, string Head)> got, List<(int Len, string Head)> exp)
{
    int n = Math.Min(got.Count, exp.Count);
    for (int i = 0; i < n; i++)
    {
        if (got[i] != exp[i])
        {
            Console.WriteLine($"  DIVERGE {label} #{i}: .NET (len={got[i].Len} [{got[i].Head}])  Kotlin (len={exp[i].Len} [{exp[i].Head}])");
            return 2;
        }
    }

    if (got.Count != exp.Count)
    {
        Console.WriteLine($"  COUNT DIFF {label}: .NET {got.Count} vs Kotlin {exp.Count} (first {n} match)");
        return got.Count > exp.Count ? 0 : 2;
    }

    Console.WriteLine($"  OK {label}: all {got.Count} match");
    return 0;
}

int ReportIntSeq(string label, List<int> got, List<int> exp)
{
    int n = Math.Min(got.Count, exp.Count);
    for (int i = 0; i < n; i++)
    {
        if (got[i] != exp[i])
        {
            Console.WriteLine($"  DIVERGE {label} #{i}: .NET {got[i]} (0x{got[i]:X4})  Kotlin {exp[i]} (0x{exp[i]:X4})");
            return 2;
        }
    }

    if (got.Count != exp.Count)
    {
        Console.WriteLine($"  COUNT DIFF {label}: .NET {got.Count} vs Kotlin {exp.Count} (first {n} match)");
        return got.Count > exp.Count ? 0 : 2;
    }

    Console.WriteLine($"  OK {label}: all {got.Count} match");
    return 0;
}

int ReportCount(string label, int got, int exp)
{
    bool ok = got == exp;
    Console.WriteLine($"  {(ok ? "OK" : "DIFF")} {label}: .NET {got} vs Kotlin {exp}");
    return ok ? 0 : 2;
}

sealed class CollectingSink : IStreamProcessorSink
{
    public readonly List<int> Dispatched = [];
    public int Unknown;
    public int Compressed;
    public int ParserErrors;

    public void Dispatch(int opcode, string? opcodeName, bool extraFlag, int len) => Dispatched.Add(opcode);
    public void UnknownOpcode(int opcode, bool extraFlag, int len) => Unknown++;
    public void CompressedPacket(int len, bool extraFlag) => Compressed++;
    public void ParserError(string stage, string reason) => ParserErrors++;
}

static class Hex
{
    public static string Head(byte[] bytes, int maxBytes = 24)
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
}

using System.Text;
using System.Text.Json;
using WaffleMeter.Capture;

// Phase 0 real-data parity check (Layers 0/1/2/3a/3b/3c-partial):
//   capture -> CapturedSegment -> srcIp-aware PacketAlignmenter -> StreamAssembler -> StreamProcessor
// diffed against the Kotlin records in the SAME corpus:
//   L1/L2: emitted frames vs 'assembled'
//   L3a:   dispatched opcode sequence vs 'dispatch'; compressed/unknown/parser-error counts
//   L3b:   parsed damage events vs 'damage'
//   L3c:   byte-derived meta vs 'nickname' + 'own_combat_power' (catalog/runtime-state-dependent
//          meta types — summon/mob_spawn/remain_hp/battle/buff — are validated with the data layer)
// Usage: dotnet run --project <ReplayCli> -c Release -- <corpus.jsonl> [skills.json]

if (args.Length < 1)
{
    Console.Error.WriteLine("usage: dotnet run --project <ReplayCli> -c Release -- <corpus.jsonl> [skills.json]");
    return 1;
}

string path = args[0];
if (!File.Exists(path))
{
    Console.Error.WriteLine($"corpus not found: {path}");
    return 1;
}

var skillSet = new HashSet<long>();
bool skillsLoaded = false;
if (args.Length >= 2 && File.Exists(args[1]))
{
    using JsonDocument skillsDoc = JsonDocument.Parse(File.ReadAllText(args[1]));
    foreach (JsonElement el in skillsDoc.RootElement.EnumerateArray())
    {
        if (el.TryGetProperty("code", out JsonElement codeEl))
        {
            skillSet.Add(codeEl.GetInt64());
        }
    }

    skillsLoaded = true;
}

var typeCounts = new Dictionary<string, int>();
var captures = new List<CapturedSegment>();
var assembledExpected = new List<(int Len, string Head)>();
var dispatchExpected = new List<int>();
var damageExpected = new List<DmgRec>();
var metaExpected = new List<(string Type, string Norm)>();
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
            case "damage":
                damageExpected.Add(ReadDamage(root));
                break;
            case "nickname":
            case "own_combat_power":
                metaExpected.Add((type, NormalizeJsonMeta(root)));
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
Func<long, bool> skillExists = skillsLoaded ? (c => skillSet.Contains(c)) : (_ => false);
var processor = new StreamProcessor(sink, skillExists);
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
Console.WriteLine($"skills.json: {(skillsLoaded ? $"loaded ({skillSet.Count} codes) — skill field compared" : "not supplied — skill field NOT compared")}");
Console.WriteLine("--- record coverage ---");
foreach (KeyValuePair<string, int> kv in typeCounts.OrderByDescending(k => k.Value))
{
    Console.WriteLine($"  {kv.Key,-18} {kv.Value}");
}

int rc = 0;

Console.WriteLine();
Console.WriteLine("--- L1/L2: assembler frames vs Kotlin 'assembled' ---");
Console.WriteLine($"  .NET {emitted.Count}   Kotlin {assembledExpected.Count}");
rc |= ReportPairSeq("assembled frame", emitted, assembledExpected);

Console.WriteLine();
Console.WriteLine("--- L3a: dispatch opcodes + compression ---");
Console.WriteLine($"  dispatched .NET {sink.Dispatched.Count}   Kotlin {dispatchExpected.Count}");
rc |= ReportIntSeq("dispatch opcode", sink.Dispatched, dispatchExpected);
rc |= ReportCount("compressed_packet", sink.Compressed, compressedExpected);
rc |= ReportCount("unknown_opcode", sink.Unknown, unknownExpected);
rc |= ReportCount("parser_error", sink.ParserErrors, parserErrorExpected);

Console.WriteLine();
Console.WriteLine("--- L3b: parsed damage events vs Kotlin 'damage' ---");
Console.WriteLine($"  .NET {sink.Damages.Count}   Kotlin {damageExpected.Count}");
rc |= ReportDamageSeq(sink.Damages, damageExpected, skillsLoaded);

Console.WriteLine();
Console.WriteLine("--- L3c (byte-derived): nickname + own_combat_power vs Kotlin meta ---");
Console.WriteLine($"  .NET {sink.Metas.Count}   Kotlin {metaExpected.Count}");
rc |= ReportMetaSeq(sink.Metas, metaExpected);

Console.WriteLine();
Console.WriteLine(rc == 0 ? "RESULT: ALL PARITY OK" : "RESULT: DIVERGENCE (see above)");
return rc;

DmgRec ReadDamage(JsonElement root)
{
    string? reason = null;
    if (root.TryGetProperty("reason", out JsonElement rEl) && rEl.ValueKind == JsonValueKind.String)
    {
        reason = rEl.GetString();
    }

    return new DmgRec(
        root.GetProperty("kind").GetString() ?? "",
        root.GetProperty("saved").GetBoolean(),
        reason,
        root.GetProperty("actor").GetInt32(),
        root.GetProperty("target").GetInt32(),
        root.GetProperty("skill").GetInt32(),
        root.GetProperty("damage").GetInt32(),
        root.GetProperty("crit").GetBoolean(),
        root.GetProperty("dot").GetBoolean(),
        root.GetProperty("loop").GetInt32());
}

// Canonical "k=v|k=v" (sorted by key) of a Kotlin meta record, excluding type/at.
string NormalizeJsonMeta(JsonElement root)
{
    var kv = new SortedDictionary<string, string>(StringComparer.Ordinal);
    foreach (JsonProperty prop in root.EnumerateObject())
    {
        if (prop.Name is "type" or "at")
        {
            continue;
        }

        kv[prop.Name] = prop.Value.ValueKind switch
        {
            JsonValueKind.String => prop.Value.GetString() ?? "null",
            JsonValueKind.Number => prop.Value.GetInt64().ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "null",
            _ => prop.Value.ToString(),
        };
    }

    return string.Join("|", kv.Select(p => $"{p.Key}={p.Value}"));
}

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

    return ReportTail(label, got.Count, exp.Count, n);
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

    return ReportTail(label, got.Count, exp.Count, n);
}

int ReportDamageSeq(List<DmgRec> got, List<DmgRec> exp, bool compareSkill)
{
    int n = Math.Min(got.Count, exp.Count);
    for (int i = 0; i < n; i++)
    {
        if (!DmgRec.Equal(got[i], exp[i], compareSkill))
        {
            Console.WriteLine($"  DIVERGE damage #{i}:");
            Console.WriteLine($"    .NET   {got[i]}");
            Console.WriteLine($"    Kotlin {exp[i]}");
            return 2;
        }
    }

    return ReportTail("damage", got.Count, exp.Count, n);
}

int ReportMetaSeq(List<(string Type, string Norm)> got, List<(string Type, string Norm)> exp)
{
    int n = Math.Min(got.Count, exp.Count);
    for (int i = 0; i < n; i++)
    {
        if (got[i] != exp[i])
        {
            Console.WriteLine($"  DIVERGE meta #{i}:");
            Console.WriteLine($"    .NET   {got[i].Type}: {got[i].Norm}");
            Console.WriteLine($"    Kotlin {exp[i].Type}: {exp[i].Norm}");
            return 2;
        }
    }

    return ReportTail("meta(nickname+own_combat_power)", got.Count, exp.Count, n);
}

int ReportTail(string label, int gotCount, int expCount, int matched)
{
    if (gotCount != expCount)
    {
        Console.WriteLine($"  COUNT DIFF {label}: .NET {gotCount} vs Kotlin {expCount} (first {matched} match)");
        return gotCount > expCount ? 0 : 2;
    }

    Console.WriteLine($"  OK {label}: all {gotCount} match");
    return 0;
}

int ReportCount(string label, int got, int exp)
{
    bool ok = got == exp;
    Console.WriteLine($"  {(ok ? "OK" : "DIFF")} {label}: .NET {got} vs Kotlin {exp}");
    return ok ? 0 : 2;
}

readonly record struct DmgRec(
    string Kind, bool Saved, string? Reason, int Actor, int Target, int Skill, int Damage, bool Crit, bool Dot, int Loop)
{
    public static bool Equal(DmgRec a, DmgRec b, bool compareSkill)
        => a.Kind == b.Kind && a.Saved == b.Saved && a.Reason == b.Reason
           && a.Actor == b.Actor && a.Target == b.Target && a.Damage == b.Damage
           && a.Crit == b.Crit && a.Dot == b.Dot && a.Loop == b.Loop
           && (!compareSkill || a.Skill == b.Skill);
}

sealed class CollectingSink : IStreamProcessorSink
{
    public readonly List<int> Dispatched = [];
    public readonly List<DmgRec> Damages = [];
    public readonly List<(string Type, string Norm)> Metas = [];
    public int Unknown;
    public int Compressed;
    public int ParserErrors;

    public void Dispatch(int opcode, string? opcodeName, bool extraFlag, int len) => Dispatched.Add(opcode);
    public void UnknownOpcode(int opcode, bool extraFlag, int len) => Unknown++;
    public void CompressedPacket(int len, bool extraFlag) => Compressed++;
    public void ParserError(string stage, string reason) => ParserErrors++;

    public void Damage(string kind, ParsedDamagePacket packet, bool saved, string? reason, int? mobCode)
        => Damages.Add(new DmgRec(kind, saved, reason, packet.ActorId, packet.TargetId, packet.SkillCode,
            packet.Damage, packet.IsCrit, packet.Dot, packet.Loop));

    public void Meta(string type, params (string Key, object? Value)[] fields)
    {
        if (type is not ("nickname" or "own_combat_power"))
        {
            return; // only the byte-derived meta types are validated at this phase
        }

        var kv = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach ((string Key, object? Value) f in fields)
        {
            kv[f.Key] = f.Value switch
            {
                null => "null",
                bool b => b ? "true" : "false",
                _ => f.Value.ToString() ?? "null",
            };
        }

        Metas.Add((type, string.Join("|", kv.Select(p => $"{p.Key}={p.Value}"))));
    }
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

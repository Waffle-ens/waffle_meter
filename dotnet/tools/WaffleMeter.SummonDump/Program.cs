using System.Text.Json;
using WaffleMeter.Capture;
using WaffleMeter.Data;

// SUMMON-OWNER RESOLUTION CHECK (investigation / regression guard): replay a packet-debug .jsonl through the
// real pipeline (aligner → assembler → StreamProcessor) and report, per 0x3641 Summon instanceId, whether a
// mob_spawn AND a summon_map (summon→owner) were emitted. Used to confirm the 07 02 06 → 07 02 owner-marker
// fallback recovers the atypical variant (e.g. the 251-byte 18066 in the 그리오사 phantom case) without
// changing the owner resolved for the uniform 209-byte siblings. Prints the total summon_map count so a
// before/after replay shows exactly how many previously-unmapped summons the fix recovers.
if (args.Length < 2) { Console.Error.WriteLine("usage: <corpus.jsonl> <skills.json> [watchIds csv]"); return 1; }
string corpus = args[0], skillsPath = args[1];
var watch = new HashSet<int>();
if (args.Length >= 3) foreach (string s in args[2].Split(',')) { if (int.TryParse(s, out int v)) watch.Add(v); }
if (watch.Count == 0) { watch.Add(18066); watch.Add(16944); watch.Add(25773); watch.Add(22744); }

string jsonDir = Path.GetDirectoryName(Path.GetFullPath(skillsPath))!;
HashSet<long> skills = ReferenceJson.LoadSkillCodes(skillsPath);
string mobsPath = Path.Combine(jsonDir, "mobs.json");
Dictionary<int, Mob> mobs = File.Exists(mobsPath) ? ReferenceJson.LoadMobs(mobsPath) : new Dictionary<int, Mob>();
var gameData = new GameData(mobs, skills);

var sink = new Sink();
var processor = new StreamProcessor(sink, gameData);

var byIp = new Dictionary<string, List<CapturedSegment>>();
foreach (string line in File.ReadLines(corpus))
{
    if (string.IsNullOrWhiteSpace(line)) continue;
    JsonDocument doc;
    try { doc = JsonDocument.Parse(line); } catch { continue; }
    using (doc)
    {
        JsonElement r = doc.RootElement;
        if (!r.TryGetProperty("type", out JsonElement t) || t.GetString() != "capture") continue;
        string ip = r.TryGetProperty("ip", out JsonElement ipe) ? ipe.GetString() ?? "" : "";
        var seg = new CapturedSegment(r.GetProperty("seq").GetInt64(),
            Convert.FromBase64String(r.GetProperty("data").GetString() ?? ""),
            r.GetProperty("at").GetInt64(), ip);
        (byIp.TryGetValue(ip, out List<CapturedSegment>? l) ? l : byIp[ip] = new()).Add(seg);
    }
}

var aligners = new Dictionary<string, PacketAlignmenter>();
var assemblers = new Dictionary<string, StreamAssembler>();
List<CapturedSegment> allSegs = byIp.Values.SelectMany(l => l).OrderBy(s => s.ArrivedAtMs).ThenBy(s => s.Seq).ToList();
foreach (CapturedSegment seg in allSegs)
{
    string ip = seg.SrcIp;
    if (!assemblers.TryGetValue(ip, out StreamAssembler? asm))
    {
        aligners[ip] = new PacketAlignmenter();
        assemblers[ip] = asm = new StreamAssembler((p, at) => processor.OnPacketReceived(p, at));
    }
    foreach (AlignedChunk chunk in aligners[ip].Feed(seg.Seq, seg.Payload, seg.ArrivedAtMs))
        asm.ProcessChunk(chunk.Data, chunk.ArrivedAt);
}

Console.WriteLine($"segs={allSegs.Count} streams={aligners.Count}");
Console.WriteLine($"mob_spawn total={sink.SpawnCount}  summon_map total={sink.SummonMapCount}\n");
Console.WriteLine("watched summon ids — spawn? / owner (summon_map)?:");
foreach (int id in watch.OrderBy(x => x))
{
    bool spawned = sink.SpawnCode.TryGetValue(id, out int code);
    bool mapped = sink.Owner.TryGetValue(id, out int owner);
    Console.WriteLine($"  iid={id}  mob_spawn={(spawned ? code.ToString() : "NO")}  summon_map={(mapped ? $"owner={owner}" : "NO (ORPHAN)")}");
}

// Full owner map dump for A/B diffing (old 07 02 06 vs new 07 02 fallback). Written to <corpus>.ownermap.txt.
string outPath = args.Length >= 4 ? args[3] : corpus + ".ownermap.txt";
using (var w = new StreamWriter(outPath))
    foreach (KeyValuePair<int, int> kv in sink.Owner.OrderBy(k => k.Key))
        w.WriteLine($"{kv.Key} {kv.Value}");
Console.WriteLine($"\nowner map ({sink.Owner.Count} entries) written to {outPath}");

return 0;

sealed class Sink : IStreamProcessorSink
{
    public int SpawnCount, SummonMapCount;
    public readonly Dictionary<int, int> SpawnCode = new();
    public readonly Dictionary<int, int> Owner = new();
    public void Dispatch(int opcode, string? opcodeName, bool extraFlag, int len) { }
    public void UnknownOpcode(int opcode, bool extraFlag, int len) { }
    public void CompressedPacket(int len, bool extraFlag) { }
    public void ParserError(string stage, string reason) { }
    public void Damage(string kind, ParsedDamagePacket packet, bool saved, string? reason, int? mobCode) { }
    public void Battle(int target, int toggle, int? mobCode, string? mobName, bool accepted, string? reason) { }

    public void Meta(string type, params (string Key, object? Value)[] fields)
    {
        if (type == "mob_spawn")
        {
            SpawnCount++;
            int iid = Get(fields, "instanceId"); int code = Get(fields, "mobCode");
            SpawnCode[iid] = code;
        }
        else if (type == "summon_map")
        {
            SummonMapCount++;
            int sid = Get(fields, "summonId"); int owner = Get(fields, "ownerId");
            Owner[sid] = owner;
        }
    }

    private static int Get((string Key, object? Value)[] fields, string key)
    {
        foreach ((string Key, object? Value) f in fields) if (f.Key == key) return Convert.ToInt32(f.Value);
        return 0;
    }
}

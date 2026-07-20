using System.Globalization;
using System.Text.Json;
using WaffleMeter.Capture;
using WaffleMeter.Capture.Corpus;

// Replays a packet-debug-logs corpus through the live parser and dumps everything related to combat
// power, to diagnose the "power shows wrong" bug. Usage: PowerProbe <corpus.jsonl>
if (args.Length < 1)
{
    Console.Error.WriteLine("usage: PowerProbe <corpus.jsonl>");
    return 1;
}

string corpus = args[0];
var sink = new ProbeSink();
var aligner = new PacketAlignmenter();
StreamAssembler assembler = null!;
var processor = new StreamProcessor(sink);
assembler = new StreamAssembler((p, at) => processor.OnPacketReceived(p, at));

string currentIp = "";
foreach (string line in CaptureCorpusReader.ReadLines(corpus))
{
    if (string.IsNullOrWhiteSpace(line))
    {
        continue;
    }

    using JsonDocument doc = JsonDocument.Parse(line);
    JsonElement root = doc.RootElement;
    if (!root.TryGetProperty("type", out JsonElement t) || t.GetString() != "capture")
    {
        continue;
    }

    long seq = root.GetProperty("seq").GetInt64();
    long at = root.GetProperty("at").GetInt64();
    string ip = root.TryGetProperty("ip", out JsonElement ipe) ? ipe.GetString() ?? "" : "";
    byte[] data = Convert.FromBase64String(root.GetProperty("data").GetString() ?? "");
    if (ip != currentIp)
    {
        currentIp = ip;
        aligner.Reset();
    }

    foreach (AlignedChunk chunk in aligner.Feed(seq, data, at))
    {
        assembler.ProcessChunk(chunk.Data, chunk.ArrivedAt);
    }
}

Console.WriteLine($"=== own_combat_power events: {sink.OwnPower.Count} ===");
foreach ((int uid, int power) in sink.OwnPower)
{
    Console.WriteLine($"  uid={uid}  power={power}");
}

Console.WriteLine("=== own nickname power per uid (the req-3 fix: every executor uid should get a power) ===");
foreach (IGrouping<int, (int Uid, string Nick, int Power)> g in sink.OwnNick.GroupBy(x => x.Uid))
{
    string powers = string.Join(", ", g.Select(x => x.Power).Distinct().OrderBy(x => x));
    Console.WriteLine($"  uid={g.Key}  nick={g.First().Nick}  count={g.Count()}  powers=[{powers}]");
}

Console.WriteLine("=== other nickname power per uid (distinct values; instability = heuristic mis-locate) ===");
foreach (IGrouping<int, (int Uid, string Nick, int Power)> g in sink.OtherNick.GroupBy(x => x.Uid))
{
    string powers = string.Join(", ", g.Select(x => x.Power).Distinct().OrderBy(x => x));
    Console.WriteLine($"  uid={g.Key}  nick={g.First().Nick}  count={g.Count()}  powers=[{powers}]");
}

return 0;

sealed class ProbeSink : IStreamProcessorSink
{
    public readonly List<(int Uid, int Power)> OwnPower = [];
    public readonly List<(int Uid, string Nick, int Power)> OwnNick = [];
    public readonly List<(int Uid, string Nick, int Power)> OtherNick = [];

    public void Dispatch(int opcode, string? opcodeName, bool extraFlag, int len) { }
    public void UnknownOpcode(int opcode, bool extraFlag, int len) { }
    public void CompressedPacket(int len, bool extraFlag) { }
    public void ParserError(string stage, string reason) { }
    public void Damage(string kind, ParsedDamagePacket packet, bool saved, string? reason, int? mobCode) { }
    public void Battle(int target, int toggle, int? mobCode, string? mobName, bool accepted, string? reason) { }

    public void Meta(string type, params (string Key, object? Value)[] fields)
    {
        Dictionary<string, object?> d = fields.ToDictionary(f => f.Key, f => f.Value);
        int I(string k) => d.TryGetValue(k, out object? v) && v != null ? Convert.ToInt32(v, CultureInfo.InvariantCulture) : 0;
        string S(string k) => d.TryGetValue(k, out object? v) ? v?.ToString() ?? "" : "";

        if (type == "own_combat_power")
        {
            OwnPower.Add((I("uid"), I("power")));
        }
        else if (type == "nickname")
        {
            bool own = d.TryGetValue("own", out object? o) && o is true;
            if (own)
            {
                OwnNick.Add((I("uid"), S("nickname"), I("power")));
            }
            else
            {
                OtherNick.Add((I("uid"), S("nickname"), I("power")));
            }
        }
    }
}

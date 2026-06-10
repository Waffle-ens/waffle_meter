using System.Diagnostics;
using System.Threading.Channels;
using WaffleMeter.Capture;
using WaffleMeter.Capture.Live;
using WaffleMeter.Data;

// Headless live meter core: capture -> the verified Aligner/Assembler/StreamProcessor pipeline ->
// DataManager + DpsCalculator -> live per-player DPS. Run elevated (capture needs admin).
//   dotnet run --project <MeterCli> -c Release -- [windivert|npcap] [json-dir]

string backendName = args.Length >= 1 ? args[0].ToLowerInvariant() : "windivert";
string jsonDir = args.Length >= 2
    ? args[1]
    : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "src", "main", "resources", "json");

if (!Directory.Exists(jsonDir))
{
    Console.Error.WriteLine($"json dir not found: {jsonDir} — pass it as the 2nd argument.");
    return 1;
}

// Reference catalogs into the data layer (real wall clock for live capture).
var dm = new DataManager();
dm.LoadMobs(ReferenceJson.LoadMobs(Path.Combine(jsonDir, "mobs.json")));
dm.LoadSkills(ReferenceJson.LoadSkills(Path.Combine(jsonDir, "skills.json")));
foreach (string buffFile in new[] { "buff.json", "buff_custom.json" })
{
    string bp = Path.Combine(jsonDir, buffFile);
    if (File.Exists(bp))
    {
        dm.LoadBuffs(ReferenceJson.LoadBuffs(bp));
    }
}

if (File.Exists(Path.Combine(jsonDir, "buff_blacklist.json")))
{
    dm.LoadBuffBlacklist(ReferenceJson.LoadBuffBlacklist(Path.Combine(jsonDir, "buff_blacklist.json")));
}

// Pipeline (single consumer thread owns these — not thread-safe).
var aligner = new PacketAlignmenter();
StreamAssembler assembler = null!;
var calculator = new DpsCalculator(dm, () => { assembler.Flush(); aligner.Reset(); });
var processor = new StreamProcessor(NullStreamProcessorSink.Instance, dm);
assembler = new StreamAssembler((packet, at) => processor.OnPacketReceived(packet, at));

// Capture runs on its own thread; hand segments to the consumer via a channel (mirrors Main.kt).
Channel<CapturedSegment> channel = Channel.CreateUnbounded<CapturedSegment>(
    new UnboundedChannelOptions { SingleReader = true });

IPacketCaptureBackend backend = backendName == "npcap" ? new NpcapBackend() : new WinDivertBackend();
backend.SegmentReceived += seg => channel.Writer.TryWrite(seg);

bool stop = false;
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stop = true;
};

try
{
    backend.Start(new CaptureConfig());
}
catch (Exception ex)
{
    Console.Error.WriteLine($"capture start failed ({backendName}): {ex.Message}");
    return 2;
}

Console.WriteLine($"waffle_meter (headless, {backendName}) — capturing. Ctrl+C to stop.");

var sw = Stopwatch.StartNew();
long lastPrint = 0;
string currentIp = "";
ChannelReader<CapturedSegment> reader = channel.Reader;

while (!stop)
{
    while (reader.TryRead(out CapturedSegment seg))
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

    if (sw.ElapsedMilliseconds - lastPrint >= 500)
    {
        lastPrint = sw.ElapsedMilliseconds;
        PrintReport(calculator.GetDps());
    }

    Thread.Sleep(5);
}

backend.Stop();
backend.Dispose();
Console.WriteLine("stopped.");
return 0;

static void PrintReport(DpsReport report)
{
    Console.Clear();
    string target = report.Target?.Mob.Name ?? "-";
    long durMs = Math.Max(report.BattleEnd - report.BattleStart, 0);
    Console.WriteLine($"target: {target}    duration: {durMs / 1000.0:F1}s    players: {report.Information.Count}");
    Console.WriteLine(new string('-', 60));

    foreach (KeyValuePair<int, DpsInformation> kv in report.Information.OrderByDescending(kv => kv.Value.Amount))
    {
        User? u = report.Contributors.FirstOrDefault(c => c.Id == kv.Key);
        string name = u?.Nickname ?? kv.Key.ToString();
        string job = u?.Job?.ClassName() ?? "";
        Console.WriteLine($"{name,-14} {job,-6} {kv.Value.Amount,13:N0}  {kv.Value.Dps,11:N0}/s  {kv.Value.Contribution,5:F1}%");
    }
}

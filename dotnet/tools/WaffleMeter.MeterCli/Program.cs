using WaffleMeter.App.Core;
using WaffleMeter.Capture.Live;
using WaffleMeter.Data;
using WaffleMeter.Services;

// Headless live meter: builds the full backend via MeterServices, runs MeterEngine over a capture
// backend, and prints the live DPS report. Run elevated for in-process capture, or use the "helper"
// backend to talk to the elevated CaptureHost.
//   dotnet run --project <MeterCli> -c Release -- [windivert|npcap|helper|helper-npcap] [json-dir]

string backendName = args.Length >= 1 ? args[0].ToLowerInvariant() : "windivert";
string jsonDir = args.Length >= 2
    ? args[1]
    : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "src", "main", "resources", "json");

if (!Directory.Exists(jsonDir))
{
    Console.Error.WriteLine($"json dir not found: {jsonDir} — pass it as the 2nd argument.");
    return 1;
}

var services = new MeterServices(new PropertyHandler());
services.LoadCatalogs(jsonDir);

IPacketCaptureBackend backend = backendName switch
{
    "npcap" => new NpcapBackend(),
    "helper" => new NamedPipeCaptureClient("windivert"),
    "helper-npcap" => new NamedPipeCaptureClient("npcap"),
    _ => new WinDivertBackend(),
};

using var engine = new MeterEngine(services, backend);
engine.CaptureError += msg => Console.Error.WriteLine($"[helper] {msg}");
engine.ReportUpdated += PrintReport;

bool stop = false;
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stop = true;
};

try
{
    engine.Start();
}
catch (Exception ex)
{
    Console.Error.WriteLine($"capture start failed ({backendName}): {ex.Message}");
    return 2;
}

Console.WriteLine($"waffle_meter (headless, {backendName}) — capturing. Ctrl+C to stop.");
while (!stop)
{
    Thread.Sleep(50);
}

engine.Stop();
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

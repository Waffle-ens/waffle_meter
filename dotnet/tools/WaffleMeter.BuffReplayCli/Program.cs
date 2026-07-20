using System.Globalization;
using System.Text.Json;
using WaffleMeter.Capture;
using WaffleMeter.Capture.Corpus;
using WaffleMeter.Data;
using WaffleMeter.Stats;

// Replays a packet-debug corpus through the real capture -> DataManager -> DpsCalculator pipeline and dumps
// each saved battle's buff/debuff uptime rows, then audits the (baseCode, name, actor) merge:
//   - two rows sharing a group key -> the merge failed
//   - a self-cast job buff reported as source "other" -> the M-3 misclassification is back
// Usage: dotnet run --project <BuffReplayCli> -c Release -- <packet-debug.jsonl> <json-dir> [--payload] [--ip=A.B.C.D]

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: ... <packet-debug.jsonl> <json-dir> [--payload]");
    return 1;
}

string corpusPath = args[0];
string jsonDir = args[1];
bool withPayload = args.Contains("--payload");
string? onlyIp = args.FirstOrDefault(a => a.StartsWith("--ip=", StringComparison.Ordinal))?["--ip=".Length..];

if (!File.Exists(corpusPath))
{
    Console.Error.WriteLine($"corpus not found: {corpusPath}");
    return 1;
}

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

string blacklistPath = Path.Combine(jsonDir, "buff_blacklist.json");
if (File.Exists(blacklistPath))
{
    dm.LoadBuffBlacklist(ReferenceJson.LoadBuffBlacklist(blacklistPath));
}

long simNow = 0;
dm.Clock = () => simNow;

// One aligner + assembler PER SOURCE IP. A capture log interleaves a dozen streams (the game server, the
// client's own outbound, loopback, unrelated hosts); a single aligner reset on every IP change shreds the
// TCP reassembly and yields zero battles. The live meter keeps per-stream state for the same reason.
var streams = new Dictionary<string, (PacketAlignmenter Aligner, StreamAssembler Assembler)>();
var processor = new StreamProcessor(NullStreamProcessorSink.Instance, dm);
var calculator = new DpsCalculator(dm, () =>
{
    foreach ((PacketAlignmenter aligner, StreamAssembler assembler) in streams.Values)
    {
        assembler.Flush();
        aligner.Reset();
    }
});

var perIpLines = new Dictionary<string, long>();
long captureLines = 0;
foreach (string line in CaptureCorpusReader.ReadLines(corpusPath))
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

    string ip = root.TryGetProperty("ip", out JsonElement ipEl) ? ipEl.GetString() ?? "" : "";
    if (onlyIp != null && ip != onlyIp)
    {
        continue;
    }

    captureLines++;
    perIpLines[ip] = perIpLines.GetValueOrDefault(ip) + 1;
    long seq = root.GetProperty("seq").GetInt64();
    long at = root.GetProperty("at").GetInt64();
    byte[] data = Convert.FromBase64String(root.GetProperty("data").GetString() ?? "");

    simNow = at;
    if (!streams.TryGetValue(ip, out (PacketAlignmenter Aligner, StreamAssembler Assembler) stream))
    {
        stream = (new PacketAlignmenter(), new StreamAssembler((packet, arrivedAt) => processor.OnPacketReceived(packet, arrivedAt)));
        streams[ip] = stream;
    }

    foreach (AlignedChunk chunk in stream.Aligner.Feed(seq, data, at))
    {
        stream.Assembler.ProcessChunk(chunk.Data, chunk.ArrivedAt);
    }

    calculator.GetDps();
}

calculator.ResetDataStorage();

var battles = new List<DpsLog>();
for (int i = 0; i < dm.RecentBattleList().Count; i++)
{
    if (dm.BattleLog(i) is { } log)
    {
        battles.Add(log);
    }
}

Console.WriteLine($"=== {Path.GetFileName(corpusPath)} — capture lines {captureLines:N0}, streams {streams.Count}, saved battles {battles.Count} ===");
Console.WriteLine("    streams: " + string.Join(", ", perIpLines.OrderByDescending(kv => kv.Value).Take(5).Select(kv => $"{kv.Key}×{kv.Value:N0}")));
if (battles.Count == 0)
{
    Console.WriteLine("    !! 저장된 전투 0건 — 이 결과로는 아무것도 검증되지 않는다 (하네스 또는 로그 문제)");
}

Console.WriteLine();

int problems = 0;
foreach ((DpsLog log, int idx) in battles.Select((b, i) => (b, i)))
{
    DpsReport r = log.Report;
    Mob? mob = r.Target?.Mob;
    string kind = mob is null ? "(no target)" : mob.Boss ? (mob.IsDummy ? "허수아비" : "네임드 보스") : "일반 몹";
    long duration = r.BattleEnd - r.BattleStart;

    Console.WriteLine($"── battle #{idx}  [{kind}]  {mob?.Name ?? "?"} (code {mob?.Code})  {duration / 1000.0:F1}s  참가 {r.Contributors.Count}명");

    if (mob is null || !mob.Boss || mob.IsDummy)
    {
        Console.WriteLine();
        continue;
    }

    foreach (User u in r.Contributors.OrderByDescending(u => r.Information.TryGetValue(u.Id, out DpsInformation? i2) ? i2.Amount : 0))
    {
        List<OperatingData> rows = log.BuffRates.GetValueOrDefault(u.Id) ?? [];
        if (rows.Count == 0)
        {
            continue;
        }

        string nick = string.IsNullOrWhiteSpace(u.Nickname) ? $"uid {u.Id}" : u.Nickname!;
        string job = u.Job?.ClassName() ?? "?";
        int jobPrefix = u.Job != null ? JobClassInfo.BasicSkillCode(u.Job.Value) / 1_000_000 : -1;
        Console.WriteLine($"   ▸ {nick} ({job})  버프행 {rows.Count}");

        problems += AuditGroups(rows, "      ");

        foreach (OperatingData b in rows.OrderByDescending(b => b.OperatingRate))
        {
            string src = b.ActorId != u.Id ? "party" : (jobPrefix > 0 && b.EffectiveJobPrefix == jobPrefix ? "self " : "other");
            string section = src == "party" ? "파티원버프" : src == "self " ? "내버프  " : "그외    ";
            Console.WriteLine($"      {b.OperatingRate,6:F1}%  {b.Name,-18} code={b.Code,-10} base={b.BaseCode,-9} " +
                              $"src={src} → {section} actor={b.ActorId}");

            if (src == "other" && b.EffectiveJobPrefix > 0 && b.EffectiveJobPrefix == jobPrefix)
            {
                Console.WriteLine("        !! M-3 회귀: 자가 직업버프가 other로 분류됨");
                problems++;
            }
        }
    }

    if (log.BossBuffRates.Count > 0)
    {
        Console.WriteLine($"   ▸ 보스 디버프  {log.BossBuffRates.Count}행");
        problems += AuditGroups(log.BossBuffRates, "      ");
        foreach (OperatingData b in log.BossBuffRates.OrderByDescending(b => b.OperatingRate))
        {
            Console.WriteLine($"      {b.OperatingRate,6:F1}%  {b.Name,-18} code={b.Code,-10} base={b.BaseCode,-9} actor={b.ActorId}");
        }
    }

    if (withPayload)
    {
        PrintPayload(log);
    }

    Console.WriteLine();
}

Console.WriteLine(problems == 0
    ? "RESULT: OK — 그룹 키 중복 없음, self/other 오분류 없음"
    : $"RESULT: {problems} problem(s) — 위 !! 표시 참조");

return problems == 0 ? 0 : 2;

// A merged row set must have exactly one row per (baseCode, name, actorId). Anything else means the merge in
// GetBuffOperatingRate leaked a duplicate. A repeated NAME across different bases is fine (지연 피해).
int AuditGroups(List<OperatingData> rows, string indent)
{
    int found = 0;
    foreach (var g in rows.GroupBy(b => (b.BaseCode, b.Name, b.ActorId)).Where(g => g.Count() > 1))
    {
        Console.WriteLine($"{indent}!! 그룹 키 중복 {g.Count()}행: base={g.Key.BaseCode} \"{g.Key.Name}\" actor={g.Key.ActorId} " +
                          $"codes=[{string.Join(",", g.Select(x => x.Code))}]");
        found++;
    }

    return found;
}

void PrintPayload(DpsLog log)
{
    var builder = new StatsPayloadBuilder(dm, () => false, () => 1_700_000_000_000);
    BuildResult result = builder.Build(log, "buff-replay", killConfirmed: true);
    if (result is BuildResult.Skip skip)
    {
        Console.WriteLine($"   ▸ payload: SKIP ({skip.Reason})");
        return;
    }

    StatsUploadPayload p = ((BuildResult.Payload)result).Value;
    Console.WriteLine($"   ▸ payload: schemaVersion={p.SchemaVersion}  buffs={p.Buffs.Count}  bossDebuffs={p.BossDebuffs.Count}");

    foreach (StatsBuffPayload b in p.Buffs.OrderByDescending(b => b.OperatingRate).Take(30))
    {
        Console.WriteLine($"      {b.OperatingRate,6:F1}%  {b.BuffName,-18} buffCode={b.BuffCode,-10} baseCode={b.BaseCode?.ToString(CultureInfo.InvariantCulture) ?? "-",-9} " +
                          $"category={b.Category,-6} source={b.Source ?? "-"}");
    }

    var badCategory = p.Buffs.Where(b => b.Category != "buff").ToList();
    if (badCategory.Count > 0)
    {
        Console.WriteLine($"      !! participant category != \"buff\" ({badCategory.Count}행) — 통계웹 zod가 payload 전체를 거절한다");
    }

    if (p.SchemaVersion != 4)
    {
        Console.WriteLine($"      !! schemaVersion={p.SchemaVersion} — 라이브 웹은 2|3|4만 허용");
    }
}

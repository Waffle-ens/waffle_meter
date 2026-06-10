using System.Globalization;
using System.Text;

namespace WaffleMeter.Capture;

/// <summary>
/// .NET port of Kotlin <c>PacketDebugLogger</c> (src/main/kotlin/packet/PacketDebugLogger.kt): an
/// NDJSON corpus writer that doubles as the live <see cref="IStreamProcessorSink"/> plus capture /
/// assembled hooks, so the app can record its own packet-debug-logs corpus — replayable by
/// DpsReplayCli / the Kotlin generateGolden — instead of needing the Kotlin dev build.
///
/// The on-disk format is byte-compatible with the Kotlin logger: manual JSON with insertion-ordered
/// keys, lowercase booleans, <c>null</c> literals, raw UTF-8 for non-ASCII (Korean) text, and the same
/// string escaping; one object per line under
/// <c>%APPDATA%/waffle_meter.v1.4/packet-debug-logs/{yyyyMMdd-HHmmss}-packet-debug.jsonl</c>; flushed
/// every 200 lines and on stop. When no session is running every method is a cheap no-op, matching the
/// Kotlin object's <c>session == null</c> early return.
///
/// Faithfulness note: the .NET <see cref="IStreamProcessorSink"/> does not carry <c>arrivedAt</c> into
/// Dispatch/UnknownOpcode nor the packet into UnknownOpcode, so those two DIAGNOSTIC events use the wall
/// clock for "at" and omit "head". The replay-critical <c>capture</c> events and the parse-result
/// events (damage/meta/battle) are fully faithful, so corpora remain replayable and golden-compatible.
/// </summary>
public sealed class PacketDebugLogger : IStreamProcessorSink
{
    private const string AppName = "waffle_meter.v1.4";

    private sealed class Session(string path, StreamWriter writer, long startedAt)
    {
        public string Path { get; } = path;
        public StreamWriter Writer { get; } = writer;
        public long StartedAt { get; } = startedAt;
        public long Lines;
        public long CaptureCount;
        public long CaptureBytes;
        public long AssembledCount;
        public long DispatchCount;
        public long ParsedDamageCount;
        public long ParsedBattleCount;
        public long ParsedMetaCount;
        public long UnknownOpcodeCount;
        public long ErrorCount;
    }

    private readonly object _gate = new();
    private readonly string _logDir;
    private volatile Session? _session;
    private string _lastStatus;

    /// <param name="logDirectory">Override the output directory (tests); null uses the default
    /// <c>%APPDATA%/waffle_meter.v1.4/packet-debug-logs</c>.</param>
    public PacketDebugLogger(string? logDirectory = null)
    {
        _logDir = logDirectory ?? LogDir();
        _lastStatus = StatusJson(false, "", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
    }

    public bool IsRunning => _session != null;

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>Begins a new timestamped session (no-op if one is already running). Returns status JSON.</summary>
    public string Start()
    {
        lock (_gate)
        {
            if (_session is { } running)
            {
                return StatusJson(running, true);
            }

            string dir = _logDir;
            Directory.CreateDirectory(dir);
            string name = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-packet-debug.jsonl";
            string path = System.IO.Path.Combine(dir, name);
            var writer = new StreamWriter(
                new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read, 1024 * 1024),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            var next = new Session(path, writer, Now());
            _session = next;
            Write(next, [("type", Str("session_start")), ("at", Num(next.StartedAt)), ("path", Str(path))]);
            _lastStatus = StatusJson(next, true);
            return _lastStatus;
        }
    }

    /// <summary>Ends the current session (flushes + closes the file). Returns the final status JSON.</summary>
    public string Stop()
    {
        lock (_gate)
        {
            if (_session is not { } current)
            {
                return _lastStatus;
            }

            Write(current, [("type", Str("session_stop")), ("at", Num(Now()))]);
            current.Writer.Flush();
            current.Writer.Dispose();
            _session = null;
            _lastStatus = StatusJson(current, false);
            return _lastStatus;
        }
    }

    /// <summary>Flushes and returns the latest status JSON (running flag + counters + path).</summary>
    public string Status()
    {
        lock (_gate)
        {
            if (_session is { } s)
            {
                s.Writer.Flush();
                _lastStatus = StatusJson(s, true);
            }

            return _lastStatus;
        }
    }

    public static string LogDirectory() => LogDir();

    // ---- L0/L1 hooks (not part of IStreamProcessorSink) ----

    /// <summary>Logs one raw captured TCP segment (pre-alignment), matching Kotlin <c>capture()</c>.</summary>
    public void Capture(string ip, long seq, byte[] data, long arrivedAt)
    {
        if (_session == null)
        {
            return;
        }

        string encoded = Convert.ToBase64String(data);
        lock (_gate)
        {
            if (_session is not { } current)
            {
                return;
            }

            current.CaptureCount += 1;
            current.CaptureBytes += data.Length;
            Write(current,
            [
                ("type", Str("capture")),
                ("at", Num(arrivedAt)),
                ("ip", Str(ip)),
                ("seq", Num(seq)),
                ("len", Num(data.Length)),
                ("head", Str(HexHead(data))),
                ("data", Str(encoded)),
            ]);
        }
    }

    /// <summary>Logs one reassembled application packet, matching Kotlin <c>assembled()</c>.</summary>
    public void Assembled(byte[] packet, long arrivedAt)
    {
        if (_session == null)
        {
            return;
        }

        lock (_gate)
        {
            if (_session is not { } current)
            {
                return;
            }

            current.AssembledCount += 1;
            Write(current,
            [
                ("type", Str("assembled")),
                ("at", Num(arrivedAt)),
                ("len", Num(packet.Length)),
                ("head", Str(HexHead(packet))),
            ]);
        }
    }

    // ---- IStreamProcessorSink ----

    public void Dispatch(int opcode, string? opcodeName, bool extraFlag, int len)
    {
        if (_session == null)
        {
            return;
        }

        lock (_gate)
        {
            if (_session is not { } current)
            {
                return;
            }

            current.DispatchCount += 1;
            Write(current,
            [
                ("type", Str("dispatch")),
                ("at", Num(Now())),
                ("opcode", Num(opcode)),
                ("opcodeHex", Str(opcode.ToString("x4", CultureInfo.InvariantCulture))),
                ("opcodeName", NullableStr(opcodeName)),
                ("extraFlag", Bool(extraFlag)),
                ("len", Num(len)),
            ]);
        }
    }

    public void UnknownOpcode(int opcode, bool extraFlag, int len)
    {
        if (_session == null)
        {
            return;
        }

        lock (_gate)
        {
            if (_session is not { } current)
            {
                return;
            }

            current.UnknownOpcodeCount += 1;
            Write(current,
            [
                ("type", Str("unknown_opcode")),
                ("at", Num(Now())),
                ("opcode", Num(opcode)),
                ("opcodeHex", Str(opcode.ToString("x4", CultureInfo.InvariantCulture))),
                ("extraFlag", Bool(extraFlag)),
                ("len", Num(len)),
            ]);
        }
    }

    public void CompressedPacket(int len, bool extraFlag)
    {
        // Kotlin logs this via meta("compressed_packet", {len, extraFlag}); mirror that exact shape.
        Meta("compressed_packet", ("len", len), ("extraFlag", extraFlag));
    }

    public void ParserError(string stage, string reason)
    {
        if (_session == null)
        {
            return;
        }

        lock (_gate)
        {
            if (_session is not { } current)
            {
                return;
            }

            current.ErrorCount += 1;
            Write(current,
            [
                ("type", Str("parser_error")),
                ("at", Num(Now())),
                ("stage", Str(stage)),
                ("reason", Str(reason)),
            ]);
        }
    }

    public void Damage(string kind, ParsedDamagePacket packet, bool saved, string? reason, int? mobCode)
    {
        if (_session == null)
        {
            return;
        }

        lock (_gate)
        {
            if (_session is not { } current)
            {
                return;
            }

            current.ParsedDamageCount += 1;
            Write(current,
            [
                ("type", Str("damage")),
                ("kind", Str(kind)),
                ("at", Num(packet.Timestamp > 0 ? packet.Timestamp : Now())),
                ("saved", Bool(saved)),
                ("reason", NullableStr(reason)),
                ("actor", Num(packet.ActorId)),
                ("target", Num(packet.TargetId)),
                ("skill", Num(packet.SkillCode)),
                ("damage", Num(packet.Damage)),
                ("crit", Bool(packet.IsCrit)),
                ("dot", Bool(packet.Dot)),
                ("loop", Num(packet.Loop)),
                ("mobCode", NullableNum(mobCode)),
            ]);
        }
    }

    public void Battle(int target, int toggle, int? mobCode, string? mobName, bool accepted, string? reason)
    {
        if (_session == null)
        {
            return;
        }

        lock (_gate)
        {
            if (_session is not { } current)
            {
                return;
            }

            current.ParsedBattleCount += 1;
            Write(current,
            [
                ("type", Str("battle")),
                ("at", Num(Now())),
                ("target", Num(target)),
                ("toggle", Num(toggle)),
                ("mobCode", NullableNum(mobCode)),
                ("mobName", NullableStr(mobName)),
                ("accepted", Bool(accepted)),
                ("reason", NullableStr(reason)),
            ]);
        }
    }

    public void Meta(string type, params (string Key, object? Value)[] fields)
    {
        if (_session == null)
        {
            return;
        }

        lock (_gate)
        {
            if (_session is not { } current)
            {
                return;
            }

            current.ParsedMetaCount += 1;
            var kv = new (string, string)[fields.Length + 2];
            kv[0] = ("type", Str(type));
            kv[1] = ("at", Num(Now()));
            for (int i = 0; i < fields.Length; i++)
            {
                kv[i + 2] = (fields[i].Key, JsonValue(fields[i].Value));
            }

            Write(current, kv);
        }
    }

    // ---- internals (verbatim of Kotlin write/toJson/quote/hexHead) ----

    private static string LogDir()
    {
        string appData = Environment.GetEnvironmentVariable("APPDATA")
                         ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return System.IO.Path.Combine(appData, AppName, "packet-debug-logs");
    }

    private void Write(Session current, (string Key, string Raw)[] fields)
    {
        current.Writer.Write(ToJson(fields));
        current.Writer.Write(Environment.NewLine);
        current.Lines += 1;
        if (current.Lines % 200L == 0L)
        {
            current.Writer.Flush();
        }
    }

    private static string ToJson((string Key, string Raw)[] fields)
    {
        var sb = new StringBuilder();
        sb.Append('{');
        for (int i = 0; i < fields.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }

            sb.Append(Quote(fields[i].Key)).Append(':').Append(fields[i].Raw);
        }

        sb.Append('}');
        return sb.ToString();
    }

    private static string JsonValue(object? value) => value switch
    {
        null => "null",
        bool b => b ? "true" : "false",
        sbyte or byte or short or ushort or int or uint or long or ulong
            => Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
        _ => Quote(value.ToString() ?? ""),
    };

    private static string Str(string value) => Quote(value);

    private static string NullableStr(string? value) => value is null ? "null" : Quote(value);

    private static string Num(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Bool(bool value) => value ? "true" : "false";

    private static string NullableNum(int? value) => value is null ? "null" : Num(value.Value);

    private static string Quote(string value)
    {
        var sb = new StringBuilder(value.Length + 8);
        sb.Append('"');
        foreach (char ch in value)
        {
            switch (ch)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (ch < 0x20)
                    {
                        sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(ch);
                    }

                    break;
            }
        }

        sb.Append('"');
        return sb.ToString();
    }

    private static string HexHead(byte[] bytes, int maxBytes = 24)
    {
        int n = Math.Min(maxBytes, bytes.Length);
        var sb = new StringBuilder(n * 3);
        for (int i = 0; i < n; i++)
        {
            if (i > 0)
            {
                sb.Append(' ');
            }

            sb.Append(bytes[i].ToString("X2", CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    private static string StatusJson(Session current, bool running) => StatusJson(
        running,
        current.Path,
        current.StartedAt,
        current.Lines,
        current.CaptureCount,
        current.CaptureBytes,
        current.AssembledCount,
        current.DispatchCount,
        current.ParsedDamageCount,
        current.ParsedBattleCount,
        current.ParsedMetaCount,
        current.UnknownOpcodeCount,
        current.ErrorCount);

    private static string StatusJson(
        bool running, string path, long startedAt, long lines, long captureCount, long captureBytes,
        long assembledCount, long dispatchCount, long parsedDamageCount, long parsedBattleCount,
        long parsedMetaCount, long unknownOpcodeCount, long errorCount) => ToJson(
    [
        ("running", Bool(running)),
        ("path", Str(path)),
        ("startedAt", Num(startedAt)),
        ("lines", Num(lines)),
        ("captureCount", Num(captureCount)),
        ("captureBytes", Num(captureBytes)),
        ("assembledCount", Num(assembledCount)),
        ("dispatchCount", Num(dispatchCount)),
        ("parsedDamageCount", Num(parsedDamageCount)),
        ("parsedBattleCount", Num(parsedBattleCount)),
        ("parsedMetaCount", Num(parsedMetaCount)),
        ("unknownOpcodeCount", Num(unknownOpcodeCount)),
        ("errorCount", Num(errorCount)),
    ]);
}

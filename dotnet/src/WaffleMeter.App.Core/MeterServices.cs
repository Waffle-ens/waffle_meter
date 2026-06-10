using WaffleMeter.Capture;
using WaffleMeter.Capture.Live;
using WaffleMeter.Data;
using WaffleMeter.Services;
using WaffleMeter.Stats;

namespace WaffleMeter.App.Core;

/// <summary>
/// Composition root: builds and wires the entire ported backend object graph — config, the capture
/// pipeline (aligner -&gt; assembler -&gt; stream processor), the data + DPS layer, the official
/// lookup, and the stats consent/builder/queue — and resolves their dependency cycles. The WPF UI
/// (and <see cref="MeterEngine"/>) bind to the components exposed here. <see cref="Feed"/> processes
/// one captured segment; <see cref="GetReport"/> reads the live DPS report. Both must be called from
/// a single owner thread (the meter is not internally synchronized), mirroring the Kotlin consumer.
/// </summary>
public sealed class MeterServices
{
    public PropertyHandler Props { get; }
    public DataManager Data { get; }
    public DpsCalculator Calculator { get; }
    public OfficialCharacterLookup OfficialLookup { get; }
    public StatsApiClient StatsApi { get; }
    public StatsConsentManager Consent { get; }
    public StatsPayloadBuilder StatsBuilder { get; }
    public StatsUploadQueue UploadQueue { get; }
    public string Version { get; }

    /// <summary>Diagnostic packet-debug-logs writer (off by default). Doubles as the stream processor
    /// sink + capture/assembled hooks, so the app can record a replayable corpus without the Kotlin
    /// dev build. Toggle with <c>DebugLogger.Start()/Stop()</c>.</summary>
    public PacketDebugLogger DebugLogger { get; }

    // Per-connection stream demux (Kotlin Main.kt after dev d00c850): the game can flow over a local
    // proxy on loopback with dynamic ports where multiple connections share a srcPort, so streams are
    // keyed by the full 4-tuple; each owns its own aligner+assembler over ONE shared StreamProcessor.
    private const long IdleMs = 30_000;
    private const long EvictEvery = 1000;
    private readonly StreamProcessor _processor;
    private readonly Dictionary<string, StreamState> _streams = new();
    private long _processed;

    private sealed class StreamState(PacketAlignmenter aligner, StreamAssembler assembler)
    {
        public PacketAlignmenter Aligner { get; } = aligner;
        public StreamAssembler Assembler { get; } = assembler;
        public long LastSeen { get; set; }
    }

    public MeterServices(
        PropertyHandler props,
        StatsApiClient.RequestFunc? statsTransport = null,
        OfficialCharacterLookup? officialLookup = null,
        PacketDebugLogger? debugLogger = null)
    {
        Props = props;
        Version = VersionConfig.LoadFromProperties(props).Version;

        OfficialLookup = officialLookup ?? new OfficialCharacterLookup();
        Data = new DataManager { OfficialLookup = OfficialLookup };

        // Pipeline (single consumer owns these; the calculator's flush resets framing + ordering of
        // every live stream). The debug logger is the processor sink so a diagnostic session captures
        // dispatch/damage/meta/etc.; it is an inert no-op until DebugLogger.Start() is called.
        DebugLogger = debugLogger ?? new PacketDebugLogger();
        _processor = new StreamProcessor(DebugLogger, Data);
        Calculator = new DpsCalculator(Data, FlushAllStreams);

        // Stats stack. Break the consent <-> builder cycle with a deferred reference.
        StatsApi = new StatsApiClient(() => StatsInstall.InstallId(props), statsTransport);
        StatsConsentManager consent = null!;
        StatsBuilder = new StatsPayloadBuilder(Data, () => consent.GetInfo().PublicCharacter);
        consent = new StatsConsentManager(props, Data, StatsApi, () => StatsBuilder.OwnCharacter());
        Consent = consent;
        UploadQueue = new StatsUploadQueue(consent, StatsBuilder, StatsApi, Data, props);
        UploadQueue.Configure(Version);

        // The only Data -> Stats edge: a saved battle log is offered to the upload queue.
        Calculator.OnBattleLogged = log => UploadQueue.OfferIfEligible(log);
    }

    /// <summary>Loads the reference catalogs (mobs/skills/buffs/blacklist) from a json directory.</summary>
    public void LoadCatalogs(string jsonDir)
    {
        Data.LoadMobs(ReferenceJson.LoadMobs(Path.Combine(jsonDir, "mobs.json")));
        Data.LoadSkills(ReferenceJson.LoadSkills(Path.Combine(jsonDir, "skills.json")));
        foreach (string buffFile in new[] { "buff.json", "buff_custom.json" })
        {
            string path = Path.Combine(jsonDir, buffFile);
            if (File.Exists(path))
            {
                Data.LoadBuffs(ReferenceJson.LoadBuffs(path));
            }
        }

        string blacklist = Path.Combine(jsonDir, "buff_blacklist.json");
        if (File.Exists(blacklist))
        {
            Data.LoadBuffBlacklist(ReferenceJson.LoadBuffBlacklist(blacklist));
        }
    }

    /// <summary>Feeds one captured segment through its per-connection stream (Kotlin Main.kt consumer).</summary>
    public void Feed(CapturedSegment segment)
    {
        // L0: log the raw segment (pre-alignment) for a diagnostic session — replayable corpus input.
        DebugLogger.Capture(segment.SrcIp, segment.Seq, segment.Payload, segment.ArrivedAtMs);

        if (!_streams.TryGetValue(segment.StreamKey, out StreamState? state))
        {
            state = new StreamState(
                new PacketAlignmenter(),
                new StreamAssembler((packet, at) =>
                {
                    DebugLogger.Assembled(packet, at); // L1: reassembled application packet
                    _processor.OnPacketReceived(packet, at);
                }));
            _streams[segment.StreamKey] = state;
        }

        state.LastSeen = segment.ArrivedAtMs;
        foreach (AlignedChunk chunk in state.Aligner.Feed(segment.Seq, segment.Payload, segment.ArrivedAtMs))
        {
            state.Assembler.ProcessChunk(chunk.Data, chunk.ArrivedAt);
        }

        // Sampled idle eviction (every 1000th packet), clocked off the incoming packet like Kotlin.
        if (++_processed % EvictEvery == 0)
        {
            long cutoff = segment.ArrivedAtMs - IdleMs;
            foreach (string key in _streams.Where(kv => kv.Value.LastSeen < cutoff).Select(kv => kv.Key).ToList())
            {
                _streams.Remove(key);
            }
        }
    }

    private void FlushAllStreams()
    {
        foreach (StreamState state in _streams.Values)
        {
            try
            {
                state.Assembler.Flush();
                state.Aligner.Reset();
            }
            catch
            {
                // one stream's reset failure must not abort the rest
            }
        }
    }

    /// <summary>The live DPS report (must be called on the same thread as <see cref="Feed"/>).</summary>
    public DpsReport GetReport() => Calculator.GetDps();

    /// <summary>Builds capture config from settings (server.ip/port/timeout/maxSnapshotSize).</summary>
    public CaptureConfig BuildCaptureConfig() => CaptureConfig.FromProperties(Props.GetProperty);
}

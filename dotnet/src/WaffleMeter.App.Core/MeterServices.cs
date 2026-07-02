using WaffleMeter.Capture;
using WaffleMeter.Capture.Live;
using WaffleMeter.Data;
using WaffleMeter.Replay;
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

    /// <summary>Movement/positional replay engine, or null unless <c>replay.recordMovement=true</c> AND the
    /// private engine DLL is present (discovered at runtime — see <see cref="ReplayEngineLoader"/>). Records
    /// per-battle position timelines for the WCL-style replay. A PARALLEL tap on the assembled packet stream
    /// — fully decoupled from and unable to regress the parity-critical DPS path. Off by default. See
    /// docs/replay-feature-plan.md.</summary>
    public IReplayEngine? Movement { get; }
    public OfficialCharacterLookup OfficialLookup { get; }
    public StatsApiClient StatsApi { get; }
    public StatsConsentManager Consent { get; }
    public StatsPayloadBuilder StatsBuilder { get; }
    public StatsUploadQueue UploadQueue { get; }
    public string Version { get; }

    /// <summary>Pending party-join requests (Kotlin PacketEvent.JoinRequest family). The WPF layer
    /// subscribes to <see cref="JoinRequestStore.Changed"/> and renders the join panel.</summary>
    public JoinRequestStore JoinRequests { get; }

    /// <summary>Raised (on the consumer thread) with a fresh saved-battle snapshot whenever the history
    /// changes — a battle is saved or the meter is reset. The history panel caches the latest snapshot.
    /// Fires on the owner thread; the WPF layer marshals it.</summary>
    public event Action<List<(int Index, DpsReport Report)>>? BattleListChanged;

    /// <summary>Snapshot the saved-battle list and notify subscribers. MUST be called on the consumer
    /// (owner) thread — it reads the repository the parser writes to.</summary>
    public void NotifyBattleListChanged() => BattleListChanged?.Invoke(Data.RecentBattleList());

    /// <summary>Diagnostic packet-debug-logs writer (off by default). Doubles as the stream processor
    /// sink + capture/assembled hooks, so the app can record a replayable corpus without the Kotlin
    /// dev build. Toggle with <c>DebugLogger.Start()/Stop()</c>.</summary>
    public PacketDebugLogger DebugLogger { get; }

    // Per-connection stream demux (Kotlin Main.kt after dev d00c850): the game can flow over a local
    // proxy on loopback with dynamic ports where multiple connections share a srcPort, so streams are
    // keyed by the full 4-tuple; each owns its own aligner+assembler over ONE shared StreamProcessor.
    private const long IdleMs = 30_000;
    private const long EvictEvery = 1000;
    // P2P/streaming noise guard: a directional connection that has pushed this many bytes WITHOUT ever
    // yielding a recognizable game packet is noise (NAVER Live P2P, downloads, OBS…). We stop processing
    // it AND ask the elevated helper to drop it at capture, so a flood can't starve the game's
    // high-frequency damage stream. Content-based (never IP/port targeted) → loopback/booster game paths,
    // which DO yield game packets, are always kept (they earn GameSignal within the first few KB).
    private const long NoiseVolumeBytes = 2_000_000;
    // Require some FRAMED packets too, so a stalled aligner (which accumulates raw bytes but emits no
    // assembled packets — more likely under the very flood this fights) can never be misread as noise.
    private const int MinNoisePackets = 50;
    private const int MaxExcludedKeys = 16384;
    // Single-game-stream lock (dual-capture defense): a VPN/accelerator can expose the SAME plaintext
    // game bytes under TWO 4-tuples (dual tunnel, loopback relay, mid-session port rebind). Each StreamKey
    // owns its own aligner, so TCP-seq dedup can't collapse them and BOTH would feed the shared processor —
    // every damage event counted twice (~2x DPS, uniform across all rows/classes). Only the PRIMARY game
    // stream is fed; a concurrent duplicate is dropped. If the primary emits no game packet for this long,
    // the next game stream fails over (real reconnect / proxy port change). A lone game stream is always
    // primary, so single-stream (non-VPN) users are byte-for-byte unaffected.
    private const long GameStreamHandoverMs = 5_000;
    private readonly StreamProcessor _processor;
    private readonly Dictionary<string, StreamState> _streams = new();
    private readonly HashSet<string> _excludedKeys = new();
    private readonly bool _dedupeGameStreams;
    private string? _primaryGameKey;
    private long _primaryGameAt;
    private long _processed;

    /// <summary>Raised (consumer thread) when a connection is classified as high-volume non-game noise,
    /// so the capture helper can drop it at the source. <see cref="MeterEngine"/> forwards it to the
    /// backend (the pipe client relays it to the elevated helper).</summary>
    public event Action<ConnKey>? ConnectionExcludeRequested;

    private sealed class StreamState(PacketAlignmenter aligner, StreamAssembler assembler)
    {
        public PacketAlignmenter Aligner { get; } = aligner;
        public StreamAssembler Assembler { get; } = assembler;
        public long LastSeen { get; set; }
        public long Bytes { get; set; }              // raw payload volume seen on this directional connection
        public int EmittedPackets { get; set; }      // assembled packets the framer emitted (stall guard)
        public int GameSignal { get; set; }          // assembled packets that look like game packets (>0 => protected)
        public bool SuppressedDuplicate { get; set; } // a concurrent duplicate of the primary game stream — drop its packets
    }

    /// <summary>Re-admit every excluded connection (called from a user reset, on the consumer thread) so a
    /// misclassification recovers without an app relaunch. The helper's source-side drop set is cleared
    /// separately by <see cref="MeterEngine"/>.</summary>
    public void ClearExclusions() => _excludedKeys.Clear();

    public MeterServices(
        PropertyHandler props,
        StatsApiClient.RequestFunc? statsTransport = null,
        OfficialCharacterLookup? officialLookup = null,
        PacketDebugLogger? debugLogger = null,
        string? appVersion = null)
    {
        Props = props;
        // From the build (entry-assembly InformationalVersion = WaffleVersion), not a persisted
        // property — the old Kotlin value lingers in settings.properties. appVersion lets tests/CLI inject.
        Version = VersionConfig.Resolve(appVersion).Version;

        // Dual-capture defense (default on): collapse a game stream that a VPN/accelerator mirrors onto two
        // 4-tuples down to one, so damage isn't double-counted. Escape hatch: capture.dedupeGameStreams=false.
        _dedupeGameStreams = props.GetProperty("capture.dedupeGameStreams", "true") != "false";

        OfficialLookup = officialLookup ?? new OfficialCharacterLookup();
        Data = new DataManager { OfficialLookup = OfficialLookup };

        // Pipeline (single consumer owns these; the calculator's flush resets framing + ordering of
        // every live stream). The debug logger is the processor sink so a diagnostic session captures
        // dispatch/damage/meta/etc.; it is an inert no-op until DebugLogger.Start() is called.
        DebugLogger = debugLogger ?? new PacketDebugLogger();
        JoinRequests = new JoinRequestStore();
        _processor = new StreamProcessor(DebugLogger, Data, new JoinRequestSinkAdapter(JoinRequests, Data));
        Calculator = new DpsCalculator(Data, FlushAllStreams);

        // Movement/positional replay (opt-in, default OFF): a parallel tap that records per-battle position
        // timelines. Never on the DPS path; resolves entity ids via Data for non-contributor (support) movers.
        // The engine is a private, runtime-loaded DLL — absent in an open-source build, in which case
        // TryLoad returns null and replay stays unavailable (the flag still reads false-safe).
        Movement = props.GetProperty("replay.recordMovement", "false") == "true"
            ? ReplayEngineLoader.TryLoad()?.Create(new DataManagerIdentitySource(Data), Path.Combine(props.AppDirectory(), "replays"))
            : null;

        // Stats stack. Break the consent <-> builder cycle with a deferred reference. The install key signs
        // every write (reports / consent events) from the first run per §2.1/§2.5 — the server takes signed
        // writes in warn mode and gates public transitions on the resulting grant.
        StatsApi = new StatsApiClient(() => StatsInstall.InstallId(props), statsTransport, new StatsInstallKey(props));
        StatsConsentManager consent = null!;
        StatsBuilder = new StatsPayloadBuilder(Data, () => consent.GetInfo().PublicCharacter);
        consent = new StatsConsentManager(props, Data, StatsApi, () => StatsBuilder.OwnCharacter());
        Consent = consent;
        UploadQueue = new StatsUploadQueue(consent, StatsBuilder, StatsApi, Data, props);
        UploadQueue.Configure(Version);

        // The only Data -> Stats edge: a saved battle log is offered to the upload queue. Also refresh
        // the history-panel snapshot (both run on the consumer thread inside the save).
        Calculator.OnBattleLogged = log =>
        {
            UploadQueue.OfferIfEligible(log);
            NotifyBattleListChanged();
            // build the battle's position replay (kills AND wipes/직전 전투), scoped to the party/raid roster
            Movement?.OnBattleLogged(log, Data.PartyMemberIdentities(30 * 60 * 1000L));
        };
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
        // Known noise (P2P/streaming flood): already classified — drop locally. The elevated helper also
        // drops it at the source, so these segments stop arriving shortly after the exclusion is sent.
        if (_excludedKeys.Contains(segment.StreamKey))
        {
            return;
        }

        // L0: log the raw segment (pre-alignment) for a diagnostic session — replayable corpus input.
        DebugLogger.Capture(segment.SrcIp, segment.Seq, segment.Payload, segment.ArrivedAtMs);

        if (!_streams.TryGetValue(segment.StreamKey, out StreamState? state))
        {
            string streamKey = segment.StreamKey; // captured for the closure (the key this state is stored under)
            StreamState? created = null;
            var assembler = new StreamAssembler((packet, at) =>
            {
                DebugLogger.Assembled(packet, at); // L1: reassembled application packet
                bool isGame = StreamProcessor.LooksLikeGamePacket(packet);
                if (created is not null)
                {
                    created.EmittedPackets++;
                    if (isGame)
                    {
                        created.GameSignal++; // content signal: this connection carries the game stream — protect it
                    }
                }

                // Single-game-stream lock: claim primary for the first/live game stream; suppress a
                // concurrent SECOND game stream (a VPN/accelerator mirroring the same plaintext bytes onto
                // another 4-tuple) so its damage isn't double-counted. Fail over to it only if the primary
                // has gone quiet for GameStreamHandoverMs (real reconnect / proxy port change). A lone game
                // stream always satisfies the first branch, so non-VPN users are unaffected.
                if (_dedupeGameStreams && isGame && created is not null)
                {
                    if (_primaryGameKey is null || _primaryGameKey == streamKey
                        || at - _primaryGameAt > GameStreamHandoverMs)
                    {
                        _primaryGameKey = streamKey;
                        _primaryGameAt = at;
                        created.SuppressedDuplicate = false;
                    }
                    else
                    {
                        if (!created.SuppressedDuplicate)
                        {
                            // Breadcrumb the 0->1 transition only (not every packet): a debug session now
                            // shows whether dual-capture is happening WITHOUT disabling the VPN.
                            DebugLogger.Meta("dup_game_stream_dropped",
                                ("key", streamKey), ("primary", _primaryGameKey));
                        }

                        created.SuppressedDuplicate = true;
                    }
                }

                if (created is { SuppressedDuplicate: true })
                {
                    return; // duplicate capture of the game stream — already counted on the primary
                }

                Movement?.Scan(packet, at); // parallel positional-replay tap (no-op unless enabled)
                _processor.OnPacketReceived(packet, at);
            });
            created = new StreamState(new PacketAlignmenter(), assembler);
            state = created;
            _streams[segment.StreamKey] = state;
        }

        state.LastSeen = segment.ArrivedAtMs;
        state.Bytes += segment.Payload.Length;
        foreach (AlignedChunk chunk in state.Aligner.Feed(segment.Seq, segment.Payload, segment.ArrivedAtMs))
        {
            state.Assembler.ProcessChunk(chunk.Data, chunk.ArrivedAt);
        }

        // Classify AFTER processing (so this segment's packets count first): a connection that has pushed
        // a lot of bytes AND emitted enough framed packets, none of which look like the game, is noise.
        // The game stream earns GameSignal within the first few KB, so it is protected long before this.
        if (state.GameSignal == 0 && state.EmittedPackets >= MinNoisePackets && state.Bytes >= NoiseVolumeBytes)
        {
            // Always drop it locally (decoupled from the notify cap). Breadcrumb it so a wrongful exclusion
            // is diagnosable in a debug session instead of a silent blackout.
            _streams.Remove(segment.StreamKey);
            if (_primaryGameKey == segment.StreamKey) _primaryGameKey = null; // free the lock if (defensively) it was primary
            DebugLogger.Meta("conn_excluded",
                ("key", segment.StreamKey), ("bytes", state.Bytes), ("packets", state.EmittedPackets));

            // Tell the helper to drop it at the source too (capped to bound the set under peer churn).
            if (_excludedKeys.Count < MaxExcludedKeys)
            {
                _excludedKeys.Add(segment.StreamKey);
                if (ConnKey.TryFrom(segment, out ConnKey key))
                {
                    ConnectionExcludeRequested?.Invoke(key);
                }
            }

            return;
        }

        // Sampled idle eviction (every 1000th packet), clocked off the incoming packet like Kotlin.
        if (++_processed % EvictEvery == 0)
        {
            long cutoff = segment.ArrivedAtMs - IdleMs;
            foreach (string key in _streams.Where(kv => kv.Value.LastSeen < cutoff).Select(kv => kv.Key).ToList())
            {
                _streams.Remove(key);
                if (_primaryGameKey == key) _primaryGameKey = null; // primary went idle — let the next game stream claim it
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
                state.SuppressedDuplicate = false;
            }
            catch
            {
                // one stream's reset failure must not abort the rest
            }
        }

        _primaryGameKey = null; // a user reset re-selects the primary game stream from scratch
        Movement?.Reset(); // drop buffered movement + stored replays on a user reset
    }

    /// <summary>The live DPS report (must be called on the same thread as <see cref="Feed"/>).</summary>
    public DpsReport GetReport() => Calculator.GetDps();

    /// <summary>Builds capture config from settings (server.ip/port/timeout/maxSnapshotSize).</summary>
    public CaptureConfig BuildCaptureConfig() => CaptureConfig.FromProperties(Props.GetProperty);
}

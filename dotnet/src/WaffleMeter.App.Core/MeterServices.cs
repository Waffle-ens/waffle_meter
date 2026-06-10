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

    private readonly PacketAlignmenter _aligner = new();
    private readonly StreamAssembler _assembler;
    private readonly StreamProcessor _processor;
    private string _currentIp = string.Empty;

    public MeterServices(
        PropertyHandler props,
        StatsApiClient.RequestFunc? statsTransport = null,
        OfficialCharacterLookup? officialLookup = null)
    {
        Props = props;
        Version = VersionConfig.LoadFromProperties(props).Version;

        OfficialLookup = officialLookup ?? new OfficialCharacterLookup();
        Data = new DataManager { OfficialLookup = OfficialLookup };

        // Pipeline (single consumer owns these; the calculator's flush resets framing + ordering).
        StreamAssembler assembler = null!;
        Calculator = new DpsCalculator(Data, () =>
        {
            assembler.Flush();
            _aligner.Reset();
        });
        _processor = new StreamProcessor(NullStreamProcessorSink.Instance, Data);
        assembler = new StreamAssembler((packet, at) => _processor.OnPacketReceived(packet, at));
        _assembler = assembler;

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

    /// <summary>Feeds one captured segment through the pipeline (Kotlin Main.kt consumer body).</summary>
    public void Feed(CapturedSegment segment)
    {
        if (segment.SrcIp != _currentIp)
        {
            _currentIp = segment.SrcIp;
            _aligner.Reset();
        }

        foreach (AlignedChunk chunk in _aligner.Feed(segment.Seq, segment.Payload, segment.ArrivedAtMs))
        {
            _assembler.ProcessChunk(chunk.Data, chunk.ArrivedAt);
        }
    }

    /// <summary>The live DPS report (must be called on the same thread as <see cref="Feed"/>).</summary>
    public DpsReport GetReport() => Calculator.GetDps();

    /// <summary>Builds capture config from settings (server.ip/port/timeout/maxSnapshotSize).</summary>
    public CaptureConfig BuildCaptureConfig() => CaptureConfig.FromProperties(Props.GetProperty);
}

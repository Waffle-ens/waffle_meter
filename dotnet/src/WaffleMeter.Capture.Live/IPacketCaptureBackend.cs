using WaffleMeter.Capture;

namespace WaffleMeter.Capture.Live;

/// <summary>
/// Capture configuration (Kotlin PcapCapturerConfig). Defaults match the AION2 server filter.
/// ServerIp may be a CIDR (used verbatim in the BPF filter; its network part resolves the NIC).
/// </summary>
public sealed record CaptureConfig(
    string ServerIp = "206.127.156.0/24",
    string ServerPort = "13328",
    int TimeoutMs = 10,
    int SnapshotSize = 65536);

/// <summary>
/// A live packet-capture source. Implementations (WinDivert default, Npcap option) sniff the
/// server's TCP stream and raise one <see cref="CapturedSegment"/> per non-empty TCP payload — the
/// same shape the Phase 0 corpus replays, so the downstream pipeline is identical live or recorded.
/// </summary>
public interface IPacketCaptureBackend : IDisposable
{
    /// <summary>Raised on the capture thread for each captured segment.</summary>
    event Action<CapturedSegment>? SegmentReceived;

    void Start(CaptureConfig config);
    void Stop();
}

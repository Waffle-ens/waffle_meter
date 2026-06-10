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
    int SnapshotSize = 65536)
{
    /// <summary>
    /// Port of Kotlin <c>PcapCapturerConfig.loadFromProperties</c>. Takes a property getter (so this
    /// layer needs no dependency on the Services config project) and applies the same keys/defaults:
    /// server.ip, server.port, server.timeout, server.maxSnapshotSize.
    /// </summary>
    public static CaptureConfig FromProperties(Func<string, string?> getProperty)
    {
        string ip = getProperty("server.ip") ?? "206.127.156.0/24";
        string port = getProperty("server.port") ?? "13328";
        int timeout = getProperty("server.timeout") is { } t
            ? int.Parse(t, System.Globalization.CultureInfo.InvariantCulture)
            : 10;
        int snap = getProperty("server.maxSnapshotSize") is { } s
            ? int.Parse(s, System.Globalization.CultureInfo.InvariantCulture)
            : 65536;
        return new CaptureConfig(ip, port, timeout, snap);
    }
}

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

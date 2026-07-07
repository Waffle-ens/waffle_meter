using System.Collections.Concurrent;
using System.ComponentModel;
using System.Net;
using System.Runtime.InteropServices;
using PacketDotNet;
using PacketDotNet.Utils;
using WaffleMeter.Capture;

namespace WaffleMeter.Capture.Live;

/// <summary>
/// Default capture backend using WinDivert (WFP, embedded driver). Captures at the NETWORK layer in
/// SNIFF mode (passive — does not block/reinject the game's traffic) and emits each non-empty TCP
/// segment from the server.
///
/// Requires <c>WinDivert.dll</c> + the signed <c>WinDivert64.sys</c> next to the executable, and
/// administrator rights to load the driver (this backend runs inside the elevated capture helper).
/// The HVCI/driver-load behaviour is the go/no-go gating spike — validate on a clean Win11 machine.
/// </summary>
public sealed class WinDivertBackend : IPacketCaptureBackend, ISupportsConnectionExclusion
{
    // Connections the app classified as high-volume non-game noise (P2P / streaming). Their packets are
    // dropped early in the receive loop (before the PacketDotNet parse + pipe write), which keeps the
    // kernel SNIFF queue draining fast so a flood can't crowd out the game's high-frequency damage
    // stream. Written by the serve thread (on a FrameExclude), read by the capture thread — concurrent.
    private const int MaxExcluded = 16384;
    private readonly ConcurrentDictionary<ConnKey, byte> _excluded = new();

    private const int LayerNetwork = 0;
    private const ulong FlagSniff = 0x0001;
    private const ulong FlagRecvOnly = 0x0004; // WinDivert 2.x RECV_ONLY (0x0008 is SEND_ONLY — the bug that captured 0)
    private static readonly IntPtr InvalidHandle = new(-1);

    // WINDIVERT_ADDRESS (2.x): INT64 Timestamp @0, UINT8 Layer @8, UINT8 Event @9, then a flags bitfield
    // byte @10 (bit1 = Outbound, bit2 = Loopback). We only read the flags byte for RTT direction; the rest
    // is the 64-byte opaque tail. Verified live (the offline corpus carries no address bits).
    [StructLayout(LayoutKind.Sequential, Size = 64)]
    private struct WinDivertAddress
    {
        public long Timestamp;
        public byte Layer;
        public byte Event;
        public byte Flags;
    }

    private const byte AddrOutbound = 0x02;
    private const byte AddrLoopback = 0x04;

    // WINDIVERT_PARAM enum (kernel queue tuning). Defaults are 4096 packets / 2000 ms / 4 MB — small
    // enough that a zone-entry / login packet burst can overflow the queue before the receive loop drains
    // it. Load-time SINGLE packets (own-nickname 0x3633, boss-spawn 0x3641) fire inside those bursts and
    // have no re-send, so a queue overflow intermittently loses self-recognition and first-boss registration.
    private const int ParamQueueLength = 0;   // packets — max 16384
    private const int ParamQueueTime = 1;     // ms — max 16000
    private const int ParamQueueSize = 2;     // bytes — max 33554432 (32 MB)

    [DllImport("WinDivert.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern IntPtr WinDivertOpen(string filter, int layer, short priority, ulong flags);

    [DllImport("WinDivert.dll", SetLastError = true)]
    private static extern bool WinDivertSetParam(IntPtr handle, int param, ulong value);

    [DllImport("WinDivert.dll", SetLastError = true)]
    private static extern bool WinDivertRecv(IntPtr handle, byte[] pPacket, uint packetLen, out uint pRecvLen, ref WinDivertAddress pAddr);

    [DllImport("WinDivert.dll", SetLastError = true)]
    private static extern bool WinDivertShutdown(IntPtr handle, uint how);

    [DllImport("WinDivert.dll", SetLastError = true)]
    private static extern bool WinDivertClose(IntPtr handle);

    private IntPtr _handle = InvalidHandle;
    private Thread? _thread;
    private volatile bool _running;

    public event Action<CapturedSegment>? SegmentReceived;

    // ---- passive RTT (server latency) — a parallel, isolated tap; NEVER on the segment/damage path ----
    /// <summary>Raised when an inbound ack resolves a round-trip time. The ConnKey is the inbound
    /// (server→client) 4-tuple so the app can match it to the game stream. isLoopback = a VPN/booster local
    /// hop (not the real server RTT).</summary>
    public event Action<ConnKey, double, bool>? RttResolved;

    private readonly Dictionary<ulong, PassiveRttEstimator> _rtt = new();
    private readonly Dictionary<ulong, long> _lastPingMs = new();
    private static readonly long RttTicksPerSecond = System.Diagnostics.Stopwatch.Frequency;
    private const long PingThrottleMs = 250; // don't emit a ping frame more than ~4x/sec per connection

    public void Start(CaptureConfig config)
    {
        EnsureBinariesPresent();
        string filter = BuildFilter(config);
        _handle = WinDivertOpen(filter, LayerNetwork, 0, FlagSniff | FlagRecvOnly);
        if (_handle == InvalidHandle)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "WinDivertOpen failed — run elevated, ensure WinDivert.dll/.sys are present, and check HVCI/driver policy.");
        }

        // Max out the kernel SNIFF queue so a zone-entry / login burst can't overflow it before RecvLoop
        // drains it (see the WINDIVERT_PARAM note above). Best-effort — on failure the driver keeps its
        // defaults, so we don't gate startup on it.
        WinDivertSetParam(_handle, ParamQueueLength, 16384);
        WinDivertSetParam(_handle, ParamQueueTime, 16000);
        WinDivertSetParam(_handle, ParamQueueSize, 33554432);

        _running = true;
        _thread = new Thread(RecvLoop) { IsBackground = true, Name = "windivert-capture" };
        _thread.Start();
    }

    // Distinguish "binaries not bundled" from "driver failed to load" (HVCI/signing) so the UI can
    // show an actionable message instead of a generic Win32 error.
    private static void EnsureBinariesPresent()
    {
        string dir = AppContext.BaseDirectory;
        string dll = Path.Combine(dir, "WinDivert.dll");
        string sys = Path.Combine(dir, "WinDivert64.sys");
        if (!File.Exists(dll) || !File.Exists(sys))
        {
            throw new FileNotFoundException(
                "WinDivert binaries missing: place WinDivert.dll + WinDivert64.sys next to " +
                "WaffleMeter.CaptureHost.exe (see dotnet/third-party/windivert/README.md), or use the Npcap backend.");
        }
    }

    /// <summary>Exclude a connection from capture (P2P/streaming noise guard). Idempotent; capped so a
    /// churning peer set can't grow the set without bound.</summary>
    public void ExcludeConnection(ConnKey key)
    {
        if (_excluded.Count < MaxExcluded)
        {
            _excluded.TryAdd(key, 0);
        }
    }

    /// <summary>Re-admit all dropped connections (from a user reset on the client side).</summary>
    public void ClearExclusions() => _excluded.Clear();

    private void RecvLoop()
    {
        var buffer = new byte[65535];
        var addr = default(WinDivertAddress);
        while (_running)
        {
            if (!WinDivertRecv(_handle, buffer, (uint)buffer.Length, out uint recvLen, ref addr))
            {
                if (!_running)
                {
                    break;
                }

                continue;
            }

            if (recvLen >= 20)
            {
                // Drop excluded (noise) connections before the expensive parse + pipe write, so the
                // receive loop stays fast and the kernel queue keeps draining for the game stream.
                if (!_excluded.IsEmpty
                    && TryReadConnKey(buffer, (int)recvLen, out ConnKey key)
                    && _excluded.ContainsKey(key))
                {
                    continue;
                }

                Parse(buffer, (int)recvLen, addr.Flags);
            }
        }
    }

    /// <summary>Cheap, allocation-free 4-tuple read straight from the IPv4+TCP header (the same value
    /// space as <see cref="ConnKey.TryFrom"/>'s dotted-quad parse), so the exclusion match agrees with
    /// what the app sent. Returns false for non-IPv4 / non-TCP / truncated headers.</summary>
    internal static bool TryReadConnKey(byte[] b, int len, out ConnKey key)
    {
        key = default;
        if (len < 20 || (b[0] >> 4) != 4)
        {
            return false;
        }

        int ihl = (b[0] & 0x0F) * 4;
        if (ihl < 20 || len < ihl + 4 || b[9] != 6 /* TCP */)
        {
            return false;
        }

        uint src = ((uint)b[12] << 24) | ((uint)b[13] << 16) | ((uint)b[14] << 8) | b[15];
        uint dst = ((uint)b[16] << 24) | ((uint)b[17] << 16) | ((uint)b[18] << 8) | b[19];
        var sport = (ushort)((b[ihl] << 8) | b[ihl + 1]);
        var dport = (ushort)((b[ihl + 2] << 8) | b[ihl + 3]);
        key = new ConnKey(src, sport, dst, dport);
        return true;
    }

    private void Parse(byte[] buffer, int len, byte flags)
    {
        if ((buffer[0] >> 4) != 4)
        {
            return; // IPv4 only
        }

        try
        {
            var ip = new IPv4Packet(new ByteArraySegment(buffer, 0, len));
            if (ip.Extract<TcpPacket>() is not { } tcp)
            {
                return;
            }

            // Passive RTT tap — runs BEFORE the payload gate (a data ack can ride an empty-payload segment)
            // and is fully isolated: it never touches the segment/damage path and can't throw into it.
            FeedRtt(buffer, len, tcp, flags);

            byte[]? payload = tcp.PayloadData;
            if (payload is null || payload.Length == 0)
            {
                return;
            }

            long seq = (long)tcp.SequenceNumber & 0xffffffffL;
            SegmentReceived?.Invoke(new CapturedSegment(
                seq,
                payload,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ip.SourceAddress.ToString(),
                tcp.SourcePort,
                ip.DestinationAddress.ToString(),
                tcp.DestinationPort));
        }
        catch
        {
            // malformed packet — skip
        }
    }

    // Feed one TCP segment to the per-connection RTT estimator. Outbound (client→server) data segments are
    // tracked; an inbound (server→client) ack resolves the RTT. Best-effort, self-contained, and — on this
    // latency-sensitive capture thread — allocation-free: it reads the 4-tuple + payload length straight from
    // the header bytes (no IPAddress.GetAddressBytes / TcpPacket.PayloadData materialization).
    private void FeedRtt(byte[] b, int len, TcpPacket tcp, byte flags)
    {
        if (RttResolved is null || len < 20)
        {
            return;
        }

        try
        {
            int ihl = (b[0] & 0x0F) * 4;
            if (ihl < 20 || len < ihl + 20)
            {
                return;
            }

            uint src = ((uint)b[12] << 24) | ((uint)b[13] << 16) | ((uint)b[14] << 8) | b[15];
            uint dst = ((uint)b[16] << 24) | ((uint)b[17] << 16) | ((uint)b[18] << 8) | b[19];
            int totalLen = (b[2] << 8) | b[3];
            int tcpHeaderLen = ((b[ihl + 12] >> 4) & 0x0F) * 4;
            int payloadLen = Math.Max(0, Math.Min(totalLen, len) - ihl - tcpHeaderLen);

            ulong conn = Canonical(src, tcp.SourcePort, dst, tcp.DestinationPort);
            if (!_rtt.TryGetValue(conn, out PassiveRttEstimator? est))
            {
                if (_rtt.Count >= 128)
                {
                    _rtt.Clear(); // bound the map (connections are few; a hard clear is fine and rare)
                    _lastPingMs.Clear();
                }

                est = new PassiveRttEstimator(RttTicksPerSecond);
                _rtt[conn] = est;
            }

            long ts = System.Diagnostics.Stopwatch.GetTimestamp();
            bool outbound = (flags & AddrOutbound) != 0;
            if (outbound)
            {
                est.TrackOutbound(tcp.SequenceNumber, payloadLen, ts);
            }
            else if (est.TryResolveInbound(tcp.AcknowledgmentNumber, ts, out double ms))
            {
                long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                if (!_lastPingMs.TryGetValue(conn, out long last) || nowMs - last >= PingThrottleMs)
                {
                    _lastPingMs[conn] = nowMs;
                    // inbound direction (src=server, dst=client) — matches the app's game-stream key
                    RttResolved?.Invoke(new ConnKey(src, tcp.SourcePort, dst, tcp.DestinationPort), ms, (flags & AddrLoopback) != 0);
                }
            }
        }
        catch
        {
            // RTT is best-effort — never disturb capture
        }
    }

    // A direction-independent connection id: the two endpoints ordered, so outbound and inbound of the same
    // connection map to the same estimator.
    private static ulong Canonical(uint aIp, int aPort, uint bIp, int bPort)
    {
        ulong a = ((ulong)aIp << 16) | (ushort)aPort;
        ulong b = ((ulong)bIp << 16) | (ushort)bPort;
        return a <= b ? (a * 2862933555777941757UL) ^ b : (b * 2862933555777941757UL) ^ a;
    }

    /// <summary>
    /// Content-based capture (Kotlin d00c850): no IP/port/interface targeting. WinDivert is a
    /// WFP/network-layer engine that already sees every interface incl. loopback, so dropping
    /// <c>inbound</c> and the CIDR/src-port constraints captures all TCP in both directions; the
    /// only filter is a load guard excluding web/HTTPS bulk (80/443) that would flood the parser and
    /// hurt overlay FPS. The game stream (13328 / dynamic loopback) is identified by opcode content
    /// downstream. <paramref name="config"/> is unused for filtering (kept for the back-compat signature).
    /// </summary>
    private static string BuildFilter(CaptureConfig config) =>
        "ip and tcp and tcp.SrcPort != 80 and tcp.DstPort != 80 and tcp.SrcPort != 443 and tcp.DstPort != 443";

    public void Stop()
    {
        _running = false;
        if (_handle != InvalidHandle)
        {
            try
            {
                WinDivertShutdown(_handle, 0x2 /* WINDIVERT_SHUTDOWN_BOTH */);
            }
            catch
            {
                // ignore
            }
        }

        _thread?.Join(1000);
    }

    public void Dispose()
    {
        Stop();
        if (_handle != InvalidHandle)
        {
            WinDivertClose(_handle);
            _handle = InvalidHandle;
        }
    }
}

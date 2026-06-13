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

    [StructLayout(LayoutKind.Sequential, Size = 64)]
    private struct WinDivertAddress
    {
    }

    [DllImport("WinDivert.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern IntPtr WinDivertOpen(string filter, int layer, short priority, ulong flags);

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

                Parse(buffer, (int)recvLen);
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

    private void Parse(byte[] buffer, int len)
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

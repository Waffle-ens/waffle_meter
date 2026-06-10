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
public sealed class WinDivertBackend : IPacketCaptureBackend
{
    private const int LayerNetwork = 0;
    private const ulong FlagSniff = 0x0001;
    private const ulong FlagRecvOnly = 0x0008;
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
                Parse(buffer, (int)recvLen);
            }
        }
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
                seq, payload, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), ip.SourceAddress.ToString()));
        }
        catch
        {
            // malformed packet — skip
        }
    }

    /// <summary>
    /// Translate the BPF "src net &lt;cidr&gt; and port &lt;port&gt;" into a WinDivert filter:
    /// inbound IPv4 TCP whose source address is in the CIDR range and whose source port == port.
    /// </summary>
    private static string BuildFilter(CaptureConfig config)
    {
        (string lo, string hi) = CidrRange(config.ServerIp);
        return $"inbound and ip and tcp and ip.SrcAddr >= {lo} and ip.SrcAddr <= {hi} and tcp.SrcPort == {config.ServerPort}";
    }

    private static (string Lo, string Hi) CidrRange(string cidr)
    {
        string[] parts = cidr.Split('/');
        IPAddress baseIp = IPAddress.Parse(parts[0]);
        uint ipv4 = (uint)IPAddress.NetworkToHostOrder(BitConverter.ToInt32(baseIp.GetAddressBytes(), 0));
        if (parts.Length < 2)
        {
            return (baseIp.ToString(), baseIp.ToString());
        }

        int prefix = int.Parse(parts[1]);
        uint mask = prefix == 0 ? 0 : 0xFFFFFFFF << (32 - prefix);
        uint lo = ipv4 & mask;
        uint hi = lo | ~mask;
        return (ToDotted(lo), ToDotted(hi));
    }

    private static string ToDotted(uint v) =>
        new IPAddress(BitConverter.GetBytes(IPAddress.HostToNetworkOrder((int)v))).ToString();

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

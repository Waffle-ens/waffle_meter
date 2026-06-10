using System.Net;
using System.Net.Sockets;
using PacketDotNet;
using SharpPcap;
using SharpPcap.LibPcap;
using WaffleMeter.Capture;

namespace WaffleMeter.Capture.Live;

/// <summary>
/// Optional Npcap/SharpPcap capture backend — verbatim port of Kotlin PcapCapturer: resolve the
/// routed local IP via a UDP-connect, pick the matching device, open promiscuous with the BPF
/// filter, and emit each non-empty TCP segment. Requires Npcap installed (admin to capture).
/// </summary>
public sealed class NpcapBackend : IPacketCaptureBackend
{
    private LibPcapLiveDevice? _device;

    public event Action<CapturedSegment>? SegmentReceived;

    public void Start(CaptureConfig config)
    {
        string localIp = ResolveLocalIp(config.ServerIp.Split('/')[0], int.Parse(config.ServerPort));
        LibPcapLiveDevice device = FindDevice(localIp)
            ?? throw new InvalidOperationException($"no capture device found for local IP {localIp} (is Npcap installed?)");

        device.Open(new DeviceConfiguration
        {
            Mode = DeviceModes.Promiscuous,
            ReadTimeout = config.TimeoutMs,
            Snaplen = config.SnapshotSize,
        });
        device.Filter = $"src net {config.ServerIp} and port {config.ServerPort}";
        device.OnPacketArrival += OnPacketArrival;
        _device = device;
        device.StartCapture();
    }

    private void OnPacketArrival(object sender, PacketCapture e)
    {
        RawCapture raw = e.GetPacket();
        Packet packet = Packet.ParsePacket(raw.LinkLayerType, raw.Data);
        if (packet.Extract<TcpPacket>() is not { } tcp)
        {
            return;
        }

        byte[]? payload = tcp.PayloadData;
        if (payload is null || payload.Length == 0)
        {
            return;
        }

        if (packet.Extract<IPv4Packet>() is not { } ip)
        {
            return;
        }

        long seq = (long)tcp.SequenceNumber & 0xffffffffL;
        SegmentReceived?.Invoke(new CapturedSegment(
            seq, payload, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), ip.SourceAddress.ToString()));
    }

    // UDP connect sends no packets; it just resolves the OS's routed source address for that dest.
    private static string ResolveLocalIp(string serverIp, int port)
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, System.Net.Sockets.ProtocolType.Udp);
        socket.Connect(IPAddress.Parse(serverIp), port);
        return ((IPEndPoint)socket.LocalEndPoint!).Address.ToString();
    }

    private static LibPcapLiveDevice? FindDevice(string localIp)
    {
        foreach (LibPcapLiveDevice device in LibPcapLiveDeviceList.Instance)
        {
            foreach (PcapAddress addr in device.Addresses)
            {
                if (addr.Addr?.ipAddress != null && addr.Addr.ipAddress.ToString() == localIp)
                {
                    return device;
                }
            }
        }

        return null;
    }

    public void Stop()
    {
        if (_device != null)
        {
            try
            {
                _device.StopCapture();
            }
            catch
            {
                // ignore
            }

            _device.OnPacketArrival -= OnPacketArrival;
        }
    }

    public void Dispose()
    {
        Stop();
        _device?.Dispose();
        _device = null;
    }
}

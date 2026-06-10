using System.Collections.Concurrent;
using PacketDotNet;
using SharpPcap;
using SharpPcap.LibPcap;
using WaffleMeter.Capture;

namespace WaffleMeter.Capture.Live;

/// <summary>
/// Optional Npcap/SharpPcap capture backend — port of the reworked Kotlin PcapCapturer (dev commit
/// d00c850). No IP/port/interface assumptions: opens EVERY device (incl. the Npcap loopback adapter),
/// re-enumerates every 5s so adapters appearing at runtime (VPN/accelerator TUN-TAP connect) are added
/// without a restart, and applies a single load-guard filter <c>tcp and not (port 443 or port 80)</c>
/// (keeps web/HTTPS bulk out of the parser; the game stream is identified by opcode content downstream).
/// Promiscuous → non-promiscuous open fallback for adapters that reject promiscuous mode. Each segment
/// carries the full TCP 4-tuple so the consumer can demux per connection.
/// </summary>
public sealed class NpcapBackend : IPacketCaptureBackend
{
    private const string Filter = "tcp and not (port 443 or port 80)";
    private const int ReenumIntervalMs = 5000;

    private readonly ConcurrentDictionary<string, byte> _capturing = new();
    private readonly List<LibPcapLiveDevice> _open = new();
    private readonly object _openLock = new();
    private Thread? _enumThread;
    private volatile bool _running;
    private CaptureConfig _config = new();

    public event Action<CapturedSegment>? SegmentReceived;

    public void Start(CaptureConfig config)
    {
        _config = config;
        _running = true;
        _enumThread = new Thread(EnumerateLoop) { IsBackground = true, Name = "npcap-enumerate" };
        _enumThread.Start();
    }

    private void EnumerateLoop()
    {
        while (_running)
        {
            try
            {
                LibPcapLiveDeviceList devices = LibPcapLiveDeviceList.Instance;
                devices.Refresh(); // pick up adapters added at runtime (VPN connect)
                foreach (LibPcapLiveDevice device in devices)
                {
                    if (!_running)
                    {
                        break;
                    }

                    string name = device.Name;
                    if (!string.IsNullOrEmpty(name) && _capturing.TryAdd(name, 0))
                    {
                        OpenAndCapture(device, name);
                    }
                }
            }
            catch
            {
                // enumeration failed this pass; retry next interval
            }

            for (int slept = 0; slept < ReenumIntervalMs && _running; slept += 100)
            {
                Thread.Sleep(100);
            }
        }
    }

    private void OpenAndCapture(LibPcapLiveDevice device, string name)
    {
        if (!TryOpen(device))
        {
            _capturing.TryRemove(name, out _); // let a later sweep retry this adapter
            return;
        }

        try
        {
            device.Filter = Filter;
            device.OnPacketArrival += OnPacketArrival;
            device.StartCapture();
            lock (_openLock)
            {
                _open.Add(device);
            }
        }
        catch
        {
            _capturing.TryRemove(name, out _);
            try
            {
                device.Close();
            }
            catch
            {
                // ignore
            }
        }
    }

    private bool TryOpen(LibPcapLiveDevice device)
    {
        foreach (DeviceModes mode in new[] { DeviceModes.Promiscuous, DeviceModes.None })
        {
            try
            {
                device.Open(new DeviceConfiguration
                {
                    Mode = mode,
                    ReadTimeout = _config.TimeoutMs,
                    Snaplen = _config.SnapshotSize,
                });
                return true;
            }
            catch
            {
                // try the next mode (VPN TUN/TAP adapters often reject promiscuous)
            }
        }

        return false;
    }

    private void OnPacketArrival(object sender, PacketCapture e)
    {
        try
        {
            RawCapture raw = e.GetPacket();
            Packet packet = Packet.ParsePacket(raw.LinkLayerType, raw.Data);
            if (packet.Extract<TcpPacket>() is not { } tcp || packet.Extract<IPv4Packet>() is not { } ip)
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

    public void Stop()
    {
        _running = false;
        _enumThread?.Join(1000);
        lock (_openLock)
        {
            foreach (LibPcapLiveDevice device in _open)
            {
                try
                {
                    device.StopCapture();
                }
                catch
                {
                    // ignore
                }

                try
                {
                    device.Close();
                }
                catch
                {
                    // ignore
                }
            }

            _open.Clear();
        }

        _capturing.Clear();
    }

    public void Dispose() => Stop();
}

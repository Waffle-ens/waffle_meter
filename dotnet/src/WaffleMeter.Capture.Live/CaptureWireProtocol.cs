using System.Buffers.Binary;
using System.Text;
using WaffleMeter.Capture;

namespace WaffleMeter.Capture.Live;

/// <summary>
/// Framing for the elevated-capture-helper named pipe (docs/wpf-migration-plan.md §0.2). The user
/// chose to isolate elevation in a separate process: the elevated <c>WaffleMeter.CaptureHost</c>
/// runs WinDivert/Npcap and streams <see cref="CapturedSegment"/>s over this pipe to the unelevated
/// main app, which feeds them into the exact same downstream pipeline as corpus replay.
///
/// Wire format — every message is one length-prefixed, type-tagged frame:
///   [4 bytes LE: bodyLen][1 byte: type][bodyLen bytes: body]
/// The outer length lets the reader frame without knowing the body shape; the type selects the
/// decoder. A Segment body packs its own payload length implicitly (it is the frame remainder).
/// All integers are little-endian to match the rest of the packet layer.
/// </summary>
public static class CaptureWireProtocol
{
    public const string DefaultPipeName = "waffle_meter_capture";

    // server -> client
    public const byte FrameSegment = 0x01; // body = encoded CapturedSegment
    public const byte FrameError = 0x02;   // body = UTF-8 message (capture failed; helper will close)
    public const byte FrameStarted = 0x03; // body = empty (backend started OK)

    // client -> server
    public const byte FrameStart = 0x10;   // body = encoded Start (backend + CaptureConfig)
    public const byte FrameStop = 0x11;    // body = empty (graceful stop request)

    private const int HeaderLength = 5;

    public static void WriteFrame(Stream stream, byte type, ReadOnlySpan<byte> body)
    {
        Span<byte> header = stackalloc byte[HeaderLength];
        BinaryPrimitives.WriteInt32LittleEndian(header, body.Length);
        header[4] = type;
        stream.Write(header);
        if (body.Length > 0)
        {
            stream.Write(body);
        }

        stream.Flush();
    }

    /// <summary>Reads one frame; returns null on a clean EOF (peer closed between frames).</summary>
    public static (byte Type, byte[] Body)? ReadFrame(Stream stream)
    {
        byte[]? header = ReadExactly(stream, HeaderLength);
        if (header is null)
        {
            return null;
        }

        int bodyLen = BinaryPrimitives.ReadInt32LittleEndian(header);
        byte type = header[4];
        if (bodyLen < 0 || bodyLen > 64 * 1024 * 1024)
        {
            throw new InvalidDataException($"capture frame body length out of range: {bodyLen}");
        }

        byte[] body = bodyLen == 0
            ? Array.Empty<byte>()
            : ReadExactly(stream, bodyLen) ?? throw new EndOfStreamException("truncated capture frame body");
        return (type, body);
    }

    // Layout: [i64 Seq][i64 ArrivedAtMs][u16 srcIpLen][srcIp][i32 SrcPort][u16 dstIpLen][dstIp]
    //         [i32 DstPort][payload remainder]. The full 4-tuple travels so the consumer can demux
    //         streams per connection (Kotlin CapturedPacket after dev d00c850).
    public static byte[] EncodeSegment(in CapturedSegment segment)
    {
        byte[] src = Encoding.UTF8.GetBytes(segment.SrcIp);
        byte[] dst = Encoding.UTF8.GetBytes(segment.DstIp);
        var buffer = new byte[8 + 8 + 2 + src.Length + 4 + 2 + dst.Length + 4 + segment.Payload.Length];
        Span<byte> span = buffer;
        int offset = 0;
        BinaryPrimitives.WriteInt64LittleEndian(span[offset..], segment.Seq);
        offset += 8;
        BinaryPrimitives.WriteInt64LittleEndian(span[offset..], segment.ArrivedAtMs);
        offset += 8;
        BinaryPrimitives.WriteUInt16LittleEndian(span[offset..], (ushort)src.Length);
        offset += 2;
        src.CopyTo(span[offset..]);
        offset += src.Length;
        BinaryPrimitives.WriteInt32LittleEndian(span[offset..], segment.SrcPort);
        offset += 4;
        BinaryPrimitives.WriteUInt16LittleEndian(span[offset..], (ushort)dst.Length);
        offset += 2;
        dst.CopyTo(span[offset..]);
        offset += dst.Length;
        BinaryPrimitives.WriteInt32LittleEndian(span[offset..], segment.DstPort);
        offset += 4;
        segment.Payload.CopyTo(span[offset..]);
        return buffer;
    }

    public static CapturedSegment DecodeSegment(ReadOnlySpan<byte> body)
    {
        int offset = 0;
        long seq = BinaryPrimitives.ReadInt64LittleEndian(body[offset..]);
        offset += 8;
        long arrivedAt = BinaryPrimitives.ReadInt64LittleEndian(body[offset..]);
        offset += 8;
        int srcLen = BinaryPrimitives.ReadUInt16LittleEndian(body[offset..]);
        offset += 2;
        string srcIp = Encoding.UTF8.GetString(body.Slice(offset, srcLen));
        offset += srcLen;
        int srcPort = BinaryPrimitives.ReadInt32LittleEndian(body[offset..]);
        offset += 4;
        int dstLen = BinaryPrimitives.ReadUInt16LittleEndian(body[offset..]);
        offset += 2;
        string dstIp = Encoding.UTF8.GetString(body.Slice(offset, dstLen));
        offset += dstLen;
        int dstPort = BinaryPrimitives.ReadInt32LittleEndian(body[offset..]);
        offset += 4;
        byte[] payload = body[offset..].ToArray();
        return new CapturedSegment(seq, payload, arrivedAt, srcIp, srcPort, dstIp, dstPort);
    }

    public static byte[] EncodeStart(string backend, CaptureConfig config)
    {
        byte backendId = backend.Equals("npcap", StringComparison.OrdinalIgnoreCase) ? (byte)1 : (byte)0;
        byte[] ip = Encoding.UTF8.GetBytes(config.ServerIp);
        byte[] port = Encoding.UTF8.GetBytes(config.ServerPort);
        var buffer = new byte[1 + 2 + ip.Length + 2 + port.Length + 4 + 4];
        Span<byte> span = buffer;
        span[0] = backendId;
        BinaryPrimitives.WriteUInt16LittleEndian(span[1..], (ushort)ip.Length);
        ip.CopyTo(span[3..]);
        int offset = 3 + ip.Length;
        BinaryPrimitives.WriteUInt16LittleEndian(span[offset..], (ushort)port.Length);
        port.CopyTo(span[(offset + 2)..]);
        offset += 2 + port.Length;
        BinaryPrimitives.WriteInt32LittleEndian(span[offset..], config.TimeoutMs);
        BinaryPrimitives.WriteInt32LittleEndian(span[(offset + 4)..], config.SnapshotSize);
        return buffer;
    }

    public static (string Backend, CaptureConfig Config) DecodeStart(ReadOnlySpan<byte> body)
    {
        string backend = body[0] == 1 ? "npcap" : "windivert";
        int ipLen = BinaryPrimitives.ReadUInt16LittleEndian(body[1..]);
        string ip = Encoding.UTF8.GetString(body.Slice(3, ipLen));
        int offset = 3 + ipLen;
        int portLen = BinaryPrimitives.ReadUInt16LittleEndian(body[offset..]);
        string port = Encoding.UTF8.GetString(body.Slice(offset + 2, portLen));
        offset += 2 + portLen;
        int timeoutMs = BinaryPrimitives.ReadInt32LittleEndian(body[offset..]);
        int snapshotSize = BinaryPrimitives.ReadInt32LittleEndian(body[(offset + 4)..]);
        return (backend, new CaptureConfig(ip, port, timeoutMs, snapshotSize));
    }

    private static byte[]? ReadExactly(Stream stream, int count)
    {
        var buffer = new byte[count];
        int read = 0;
        while (read < count)
        {
            int n = stream.Read(buffer, read, count - read);
            if (n == 0)
            {
                return read == 0 ? null : throw new EndOfStreamException("capture pipe closed mid-frame");
            }

            read += n;
        }

        return buffer;
    }
}

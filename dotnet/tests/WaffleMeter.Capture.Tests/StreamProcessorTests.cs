using K4os.Compression.LZ4;
using WaffleMeter.Capture;
using Xunit;

namespace WaffleMeter.Capture.Tests;

/// <summary>
/// Parity spec for the L3a dispatch + decompression core of <see cref="StreamProcessor"/>.
/// Covers opcode routing (known/unknown), the 3-byte skip, and a compressed-bundle round trip
/// (K4os encode -> our decode -> inner re-framing -> dispatch).
/// </summary>
public class StreamProcessorTests
{
    private sealed class RecordingSink : IStreamProcessorSink
    {
        public readonly List<int> Dispatched = [];
        public int Unknown;
        public int Compressed;
        public int ParserErrors;

        public void Dispatch(int opcode, string? opcodeName, bool extraFlag, int len) => Dispatched.Add(opcode);
        public void UnknownOpcode(int opcode, bool extraFlag, int len) => Unknown++;
        public void CompressedPacket(int len, bool extraFlag) => Compressed++;
        public void ParserError(string stage, string reason) => ParserErrors++;
        public void Damage(string kind, ParsedDamagePacket packet, bool saved, string? reason, int? mobCode) { }
    }

    private const int DamageKey = 0x04 | (0x38 << 8); // 0x3804 = "Damage" (known)
    private const int UnknownKey = 0x03 | (0x36 << 8); // 0x3603 (seen as unknown in real corpus)

    [Fact]
    public void Skips_three_byte_packet()
    {
        var sink = new RecordingSink();
        new StreamProcessor(sink).OnPacketReceived(new byte[] { 0x06, 0x00, 0x36 }, 0);
        Assert.Empty(sink.Dispatched);
        Assert.Equal(0, sink.Unknown);
    }

    [Fact]
    public void Routes_known_opcode_without_marking_unknown()
    {
        var sink = new RecordingSink();
        // [varint len=1][0x04][0x38][..] -> opcode 0x3804
        new StreamProcessor(sink).OnPacketReceived(new byte[] { 0x01, 0x04, 0x38, 0x00, 0x00 }, 0);
        Assert.Equal(new[] { DamageKey }, sink.Dispatched);
        Assert.Equal(0, sink.Unknown);
    }

    [Fact]
    public void Routes_unknown_opcode_and_flags_it()
    {
        var sink = new RecordingSink();
        new StreamProcessor(sink).OnPacketReceived(new byte[] { 0x01, 0x03, 0x36, 0x00, 0x00 }, 0);
        Assert.Equal(new[] { UnknownKey }, sink.Dispatched);
        Assert.Equal(1, sink.Unknown);
    }

    [Fact]
    public void Decompresses_ff_ff_bundle_and_dispatches_inner_packets()
    {
        // Two inner frames, each: [varint len][0x04][0x38][marker...] with realLength = value + 1 - 4.
        // For a 1-byte varint (length 1): value = realLength + 3.
        static byte[] InnerFrame(int realLength)
        {
            var f = new byte[realLength];
            f[0] = (byte)(realLength + 3); // varint value so realLength = value + 1 - 4
            f[1] = 0x04;                   // opcode low
            f[2] = 0x38;                   // opcode high  -> 0x3804
            return f;
        }

        byte[] restored = [.. InnerFrame(5), .. InnerFrame(6)];

        // LZ4 BLOCK encode the inner stream (what the server would have compressed).
        var compressed = new byte[LZ4Codec.MaximumOutputSize(restored.Length)];
        int clen = LZ4Codec.Encode(restored, 0, restored.Length, compressed, 0, compressed.Length);
        Assert.True(clen > 0);

        // Outer packet: [varint len=0x01][FF FF][originLength u32 LE][compressed...]
        var outer = new List<byte> { 0x01, 0xFF, 0xFF };
        int n = restored.Length;
        outer.Add((byte)(n & 0xFF));
        outer.Add((byte)((n >> 8) & 0xFF));
        outer.Add((byte)((n >> 16) & 0xFF));
        outer.Add((byte)((n >> 24) & 0xFF));
        outer.AddRange(compressed[..clen]);

        var sink = new RecordingSink();
        new StreamProcessor(sink).OnPacketReceived(outer.ToArray(), 0);

        Assert.Equal(1, sink.Compressed);
        Assert.Equal(new[] { DamageKey, DamageKey }, sink.Dispatched); // both inner frames dispatched
        Assert.Equal(0, sink.Unknown);
        Assert.Equal(0, sink.ParserErrors);
    }
}

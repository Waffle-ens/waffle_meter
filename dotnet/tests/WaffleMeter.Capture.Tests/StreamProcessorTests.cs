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
        public void Meta(string type, params (string Key, object? Value)[] fields) { }
        public void Battle(int target, int toggle, int? mobCode, string? mobName, bool accepted, string? reason) { }
    }

    private const int DamageKey = 0x04 | (0x38 << 8); // 0x3804 = "Damage" (known)
    private const int DoTKey = 0x05 | (0x38 << 8);    // 0x3805 = "DoT" (known)
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

    [Fact]
    public void Truncated_dot_packet_is_swallowed_not_thrown()
    {
        // Regression for the consumer-thread crash: content-based capture lets non-game / TCP-truncated
        // data frame as a known opcode. A DoT packet ending before its 4-byte skill code made
        // ParseDoTPacket -> ParseUInt32Le throw ("패킷 길이가 필요길이보다 짧음"); that propagated out of
        // the opcode dispatch, through StreamAssembler -> MeterServices.Feed -> MeterEngine.ConsumeLoop,
        // and killed the single consumer thread (the whole app terminated). The dispatch must now
        // swallow it as a parser error instead of throwing.
        //
        // Layout: [varint len=6][0x05][0x38]=DoT [target=1][bitflag 0x02][actor=3][unk=4][skillcode truncated]
        byte[] truncated = { 0x06, 0x05, 0x38, 0x01, 0x02, 0x03, 0x04, 0x00 };

        var sink = new RecordingSink();
        var processor = new StreamProcessor(sink);

        Exception? ex = Record.Exception(() => processor.OnPacketReceived(truncated, 0));

        Assert.Null(ex);                            // must NOT throw (this is the crash)
        Assert.Contains(DoTKey, sink.Dispatched);   // it WAS routed to the DoT handler
        Assert.Equal(1, sink.ParserErrors);         // and the parse failure was recorded, not fatal
    }
}

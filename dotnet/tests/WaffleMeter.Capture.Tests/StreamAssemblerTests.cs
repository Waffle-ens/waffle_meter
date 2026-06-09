using WaffleMeter.Capture;
using Xunit;

namespace WaffleMeter.Capture.Tests;

/// <summary>
/// Parity spec for the verbatim <see cref="StreamAssembler"/> port, including the documented
/// framing edge cases (leading zero skip, invalid-length flush, split-body wait, and the
/// split-varint flush trap). See docs/phase-0-parity-harness.md §5.
/// </summary>
public class StreamAssemblerTests
{
    /// <summary>
    /// Builds a framed packet of total length <paramref name="realLength"/> using a 1-byte varint.
    /// realLength = value + length - 4, length = 1  =>  value = realLength + 3 (must be &lt;= 127).
    /// The whole frame (length prefix + body) is what the assembler emits.
    /// </summary>
    private static byte[] BuildFrame(int realLength, byte marker)
    {
        Assert.InRange(realLength, 1, 124); // value = realLength+3 must fit a 1-byte varint (<=127)
        var frame = new byte[realLength];
        frame[0] = (byte)(realLength + 3);
        for (int i = 1; i < realLength; i++)
        {
            frame[i] = marker;
        }

        return frame;
    }

    private static (StreamAssembler asm, List<byte[]> emitted) NewAssembler()
    {
        var emitted = new List<byte[]>();
        var asm = new StreamAssembler((packet, _) => emitted.Add(packet));
        return (asm, emitted);
    }

    [Fact]
    public void Emits_single_complete_frame_including_length_prefix()
    {
        var (asm, emitted) = NewAssembler();
        byte[] frame = BuildFrame(10, 0xA1);

        asm.ProcessChunk(frame, arrivedAt: 100);

        Assert.Single(emitted);
        Assert.Equal(frame, emitted[0]);
    }

    [Fact]
    public void Emits_multiple_frames_from_one_chunk_in_order()
    {
        var (asm, emitted) = NewAssembler();
        byte[] f1 = BuildFrame(10, 0xA1);
        byte[] f2 = BuildFrame(8, 0xB2);
        byte[] combined = [.. f1, .. f2];

        asm.ProcessChunk(combined, 1);

        Assert.Equal(2, emitted.Count);
        Assert.Equal(f1, emitted[0]);
        Assert.Equal(f2, emitted[1]);
    }

    [Fact]
    public void Waits_for_split_packet_body_then_emits_once_complete()
    {
        var (asm, emitted) = NewAssembler();
        byte[] frame = BuildFrame(20, 0xCC); // header is the single first byte; body completes the 20

        asm.ProcessChunk(frame[0..12], 1); // header present, only 12 of 20 bytes -> wait
        Assert.Empty(emitted);

        asm.ProcessChunk(frame[12..20], 2); // remaining bytes arrive -> emit
        Assert.Single(emitted);
        Assert.Equal(frame, emitted[0]);
    }

    [Fact]
    public void Skips_leading_zero_length_byte()
    {
        var (asm, emitted) = NewAssembler();
        byte[] frame = BuildFrame(10, 0xD3);
        byte[] chunk = [0x00, .. frame]; // a 0x00 -> value 0 -> discard 1 byte, then frame parses

        asm.ProcessChunk(chunk, 1);

        Assert.Single(emitted);
        Assert.Equal(frame, emitted[0]);
    }

    [Fact]
    public void Flushes_and_stops_on_non_positive_real_length()
    {
        var (asm, emitted) = NewAssembler();
        // value 1, length 1 -> realLength = 1 + 1 - 4 = -2 (<= 0) -> flush + stop
        asm.ProcessChunk(new byte[] { 0x01 }, 1);
        Assert.Empty(emitted);

        // buffer was flushed; a subsequent valid frame parses cleanly
        byte[] frame = BuildFrame(6, 0xEE);
        asm.ProcessChunk(frame, 2);
        Assert.Single(emitted);
        Assert.Equal(frame, emitted[0]);
    }

    [Fact]
    public void Split_varint_header_flushes_buffer_verbatim_known_trap()
    {
        var (asm, emitted) = NewAssembler();
        // A lone 0x80 (continuation bit set, no following byte) -> ReadVarInt returns -1 -> flush.
        // This is the documented "split-varint flush-everything" behavior; ported verbatim (not fixed).
        asm.ProcessChunk(new byte[] { 0x80 }, 1);
        Assert.Empty(emitted);
    }
}

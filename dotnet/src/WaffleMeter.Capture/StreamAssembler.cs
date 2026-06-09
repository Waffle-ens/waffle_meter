namespace WaffleMeter.Capture;

/// <summary>
/// Verbatim port of Kotlin <c>StreamAssembler</c> (src/main/kotlin/packet/StreamAssembler.kt).
/// Frames length-prefixed application packets out of the reassembled TCP byte stream and hands
/// each complete packet to <see cref="_onPacket"/> (the eventual StreamProcessor.OnPacketReceived).
///
/// Wire framing: <c>realLength = varint.value + varint.length - 4</c>. The "-4" is part of the
/// format and is ported verbatim. The emitted packet INCLUDES the varint length prefix (the
/// parser re-reads it), matching Kotlin <c>buffer.slice(0, realLength)</c>.
///
/// Behaviors ported verbatim — do NOT "fix" here; any correction belongs behind a flag after
/// parity is proven (plan risk register):
///  - value == 0   → discard 1 byte and continue
///  - value == -1  → flush the WHOLE buffer and stop. This is the "split-varint flush-everything"
///                   trap: if a varint header is split across segments so that peek(8) lacks the
///                   full varint, ReadVarInt returns -1 and the buffer is flushed (data loss).
///  - realLength&lt;=0 → flush and stop
///  - Size &lt; realLength → wait for more bytes (correct path for a split packet BODY)
/// </summary>
public sealed class StreamAssembler
{
    private readonly PacketAccumulator _buffer = new();
    private readonly Action<byte[], long> _onPacket;

    public StreamAssembler(Action<byte[], long> onPacket)
    {
        _onPacket = onPacket;
    }

    public void Flush() => _buffer.Flush();

    public void ProcessChunk(byte[] chunk, long arrivedAt)
    {
        _buffer.Append(chunk);

        while (_buffer.Size > 0)
        {
            byte[] header = _buffer.Peek(8);
            if (header.Length == 0)
            {
                return;
            }

            VarIntOutput lengthInfo = PacketPrimitives.ReadVarInt(header);
            if (lengthInfo.Value == 0)
            {
                _buffer.DiscardBytes(1);
                continue;
            }

            if (lengthInfo.Value == -1)
            {
                // invalid_varint: flush everything (verbatim — known data-loss trap on split varint header)
                _buffer.Flush();
                break;
            }

            int realLength = lengthInfo.Value + lengthInfo.Length - 4;
            if (realLength <= 0)
            {
                // invalid_length
                _buffer.Flush();
                break;
            }

            // 아직 패킷 전체가 도착하지 않음 — 더 기다리기
            if (_buffer.Size < realLength)
            {
                break;
            }

            byte[] packet = _buffer.Slice(0, realLength);
            _onPacket(packet, arrivedAt);
            _buffer.DiscardBytes(realLength);
        }
    }
}

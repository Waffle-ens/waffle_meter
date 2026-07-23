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
                // 길이 varint이 청크 경계에서 잘렸다(continuation 비트가 켜진 채 버퍼가 끝남). ReadVarInt는
                // 완전한 varint에 필요한 바이트가 모자랄 때만 -1을 돌려주는데, varint는 최대 5바이트라 이 경우
                // 버퍼에는 4바이트 이하만 남아 있다. 종전에는 이걸 통째로 flush 했지만, 그건 잘린 앞부분을 버릴
                // 뿐 아니라 다음 청크의 뒷부분을 새 패킷 시작으로 오정렬시킨다 — 단발 패킷(보스 스폰 0x3641·본인
                // 로드 0x3633·로스터 0x9702·버프)이 청크 경계에 걸리면 그대로 유실됐다("3번째 네임드 미집계"·
                // "버프 오버레이 빔"·"로스터 놓침"의 한 갈래). 잘린 '몸통'을 기다리는 아래 경로(Size<realLength)와
                // 대칭으로, 잘린 '헤더'도 더 기다린다. 무한 성장은 PacketAccumulator의 2MB 상한이 막는다.
                if (_buffer.Size < 5)
                {
                    break; // 다음 청크가 varint을 완성한다
                }

                // 5바이트 이상인데도 varint이 안 풀리는 건 정상적으로는 불가능하다(진짜 오정렬/손상) — resync.
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

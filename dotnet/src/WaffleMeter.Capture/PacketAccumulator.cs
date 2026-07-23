namespace WaffleMeter.Capture;

/// <summary>
/// Verbatim port of Kotlin <c>PacketAccumulator</c> (src/main/kotlin/packet/PacketAccumulator.kt).
/// Index-based growable byte buffer with read/write cursors, half-point compaction, x2 growth,
/// and a 2MB force-reset guard.
///
/// THREADING: Kotlin marks every method <c>@Synchronized</c> (the aligner consumer writes; a DPS
/// reset path may call <c>flush()</c> from another thread). This port is currently lock-free for
/// the single-consumer replay/test path. Resolve the reset/flush threading (plan risk register)
/// before wiring live capture so a reset mid-frame cannot corrupt the buffer.
/// </summary>
public sealed class PacketAccumulator
{
    // 2026-07-23 상향(구 2MB): 던전 존/보스룸 로드 스냅샷 프레임 하나가 이 상한을 넘으면 Append가 버퍼를 통째로
    // 강제 리셋(아래)해서, 그 안에 실렸거나 뒤에 큐잉된 보스의 단발·앱레벨 무재전송 0x3641 스폰을 유실한다 —
    // 시련 바크론 "됐다 안됐다" 보스 인식의 1순위 원인. StreamAssembler는 매 청크마다 완성 프레임을 전부 소진하므로
    // (greedy drain), 버퍼가 2MB에 남아 있다는 건 realLength ≥ ~2MB짜리 프레임 몸통을 기다리는 중이란 뜻 = 큰
    // 스냅샷 프레임. 14:44에 accumulator_2mb가 7연발(≈14MB) 찍힌 직후 보스가 간신히 등록된 게 마진이 종잇장임을
    // 보여준다. 바크론패턴강화(덩굴 대량 소환)가 스냅샷을 키워 초과 확률을 밀어올린다. 상한을 32MB로 올려 정상
    // 스냅샷이 리셋 없이 프레이밍되게 한다(무한 성장 방어는 유지). 값은 실킬 packet 로그로 재튜닝 예정.
    private const int MaxBufferSize = 32 * 1024 * 1024;  // 32MB
    private const int InitialCapacity = 64 * 1024;       // 64KB
    // NOTE: Kotlin also has WARN_BUFFER_SIZE (1MB) used only for a log line — intentionally omitted.

    private byte[] _buffer = new byte[InitialCapacity];
    private int _readPos;
    private int _writePos;

    public int Size => _writePos - _readPos;

    public void Append(byte[] data)
    {
        int currentSize = _writePos - _readPos;
        if (currentSize >= MaxBufferSize)
        {
            // 버퍼 용량 제한 초과, 강제 초기화 (drop the incoming chunk, reset cursors)
            _readPos = 0;
            _writePos = 0;
            return;
        }

        EnsureCapacity(data.Length);
        Array.Copy(data, 0, _buffer, _writePos, data.Length);
        _writePos += data.Length;
    }

    /// <summary>버퍼 앞에서 최대 <paramref name="length"/> 바이트를 복사 (길이 헤더 읽기용).</summary>
    public byte[] Peek(int length)
    {
        int count = Math.Min(length, _writePos - _readPos);
        if (count <= 0)
        {
            return Array.Empty<byte>();
        }

        return _buffer[_readPos..(_readPos + count)];
    }

    /// <summary>readPos 기준 [start, start+length) 범위를 복사 (패킷 하나 분량 잘라낼 때).</summary>
    public byte[] Slice(int start, int length)
    {
        int from = _readPos + start;
        int to = from + length;
        if (to > _writePos || from < _readPos)
        {
            return Array.Empty<byte>();
        }

        return _buffer[from..to];
    }

    /// <summary>읽기 포인터만 전진; readPos가 버퍼 절반을 넘으면 compact.</summary>
    public void DiscardBytes(int length)
    {
        _readPos = Math.Min(_readPos + length, _writePos);
        if (_readPos >= _buffer.Length / 2)
        {
            Compact();
        }
    }

    public void DiscardLastBytes(int length)
    {
        _writePos = Math.Max(_readPos, _writePos - length);
    }

    public void Flush()
    {
        _readPos = 0;
        _writePos = 0;
    }

    /// <summary>외부 호출용. endExclusive 미지정 시 남은 전체 범위.</summary>
    public byte[] GetRange(int start, int endExclusive) => Slice(start, endExclusive - start);

    public byte[] GetRange(int start) => Slice(start, (_writePos - _readPos) - start);

    /// <summary>매직 패킷 탐색용 (포인터 기반). 일치 인덱스(readPos 기준) 또는 -1.</summary>
    public int IndexOf(byte[] target)
    {
        int available = _writePos - _readPos;
        if (available < target.Length)
        {
            return -1;
        }

        for (int i = 0; i <= available - target.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < target.Length; j++)
            {
                if (_buffer[_readPos + i + j] != target[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return i;
            }
        }

        return -1;
    }

    private void Compact()
    {
        int remaining = _writePos - _readPos;
        if (remaining > 0)
        {
            // Array.Copy handles overlapping source/destination correctly (like System.arraycopy).
            Array.Copy(_buffer, _readPos, _buffer, 0, remaining);
        }

        _readPos = 0;
        _writePos = remaining;
    }

    private void EnsureCapacity(int needed)
    {
        if (_writePos + needed <= _buffer.Length)
        {
            return;
        }

        Compact(); // 먼저 앞 공간 확보 시도
        if (_writePos + needed > _buffer.Length)
        {
            int newSize = _buffer.Length * 2;
            while (newSize < _writePos + needed)
            {
                newSize *= 2;
            }

            Array.Resize(ref _buffer, newSize);
        }
    }
}

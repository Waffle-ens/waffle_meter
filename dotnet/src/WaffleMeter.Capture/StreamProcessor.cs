using K4os.Compression.LZ4;

namespace WaffleMeter.Capture;

/// <summary>
/// Diagnostics/observation hooks mirroring the Kotlin <c>PacketDebugLogger</c> calls inside
/// <c>StreamProcessor</c>. The host (live app, replay CLI, tests) supplies a sink to observe
/// dispatch/decompression decisions; the parser logic itself stays pure.
/// </summary>
public interface IStreamProcessorSink
{
    void Dispatch(int opcode, string? opcodeName, bool extraFlag, int len);
    void UnknownOpcode(int opcode, bool extraFlag, int len);
    void CompressedPacket(int len, bool extraFlag);
    void ParserError(string stage, string reason);
}

/// <summary>No-op sink (default).</summary>
public sealed class NullStreamProcessorSink : IStreamProcessorSink
{
    public static readonly NullStreamProcessorSink Instance = new();
    public void Dispatch(int opcode, string? opcodeName, bool extraFlag, int len) { }
    public void UnknownOpcode(int opcode, bool extraFlag, int len) { }
    public void CompressedPacket(int len, bool extraFlag) { }
    public void ParserError(string stage, string reason) { }
}

/// <summary>
/// Verbatim port of the dispatch + decompression core of Kotlin <c>StreamProcessor</c>
/// (src/main/kotlin/packet/StreamProcessor.kt — onPacketReceived 91-129, decompressPacket 131-172).
///
/// L3a scope: opcode routing + FF FF LZ4 (K4os BLOCK) decompression with inner re-framing.
/// The actual opcode HANDLERS (damage/buff/nickname/...) are deferred to L3b/L3c — known opcodes
/// are recognized here (so the dispatch-vs-unknown_opcode split matches Kotlin exactly), but their
/// bodies are no-ops for now. This is enough to reproduce the full dispatch opcode sequence,
/// including every packet recovered from compressed bundles.
///
/// CORRECTNESS-CRITICAL: the extraFlag test, the FF FF compressed-marker detection, the offset
/// math, and the inner re-framing (realLength = value + length - 4) must match Kotlin exactly.
/// </summary>
public sealed class StreamProcessor
{
    // Opcode key = b1 | (b2 << 8). Kotlin Opcode sealed class (StreamProcessor.kt:32-51).
    private static int Key(int b1, int b2) => b1 | (b2 << 8);

    // handlers and opcodeNames share the SAME key set in Kotlin, so "name == null" <=> "no handler"
    // <=> unknown opcode.
    private static readonly Dictionary<int, string> OpcodeNames = new()
    {
        [Key(0x33, 0x36)] = "OwnNickname",
        [Key(0x55, 0x36)] = "OwnCombatPower",
        [Key(0x44, 0x36)] = "OtherNickname",
        [Key(0x40, 0x36)] = "Summon",
        [Key(0x04, 0x38)] = "Damage",
        [Key(0x05, 0x38)] = "DoT",
        [Key(0x2A, 0x38)] = "BuffApply",
        [Key(0x2B, 0x38)] = "BuffApply2",
        [Key(0x21, 0x8D)] = "BattleToggle",
        [Key(0x00, 0x8D)] = "RemainHp",
        [Key(0x07, 0x97)] = "JoinRequest",
        [Key(0x25, 0x97)] = "CancelJoin",
        [Key(0x0B, 0x97)] = "AdmitJoin",
        [Key(0x09, 0x97)] = "RefuseJoin",
        [Key(0x18, 0x97)] = "InstanceStart",
        [Key(0x1D, 0x97)] = "ExitParty",
    };

    private readonly IStreamProcessorSink _sink;

    public StreamProcessor(IStreamProcessorSink? sink = null)
    {
        _sink = sink ?? NullStreamProcessorSink.Instance;
    }

    /// <summary>Entry point for one fully-framed application packet (Kotlin onPacketReceived).</summary>
    public void OnPacketReceived(byte[] packet, long arrivedAt)
    {
        if (packet.Length == 3)
        {
            return;
        }

        // Kotlin also does DataManager.saveRawPacket(...) and reads currentEpoch() here;
        // neither affects routing/decompression, so they are deferred to the data-layer phase.

        VarIntOutput lengthInfo = PacketPrimitives.ReadVarInt(packet);
        if (lengthInfo.Length < 0 || lengthInfo.Length >= packet.Length)
        {
            _sink.ParserError("processor", "invalid_packet_length_varint");
            return;
        }

        // Kotlin: packet[len] >= 0xf0.toByte() && packet[len] < 0xff.toByte() (SIGNED byte compare).
        // The signed range [-16,-2] is exactly the unsigned range [0xF0,0xFE], so the unsigned test
        // below is identical.
        int flagByte = packet[lengthInfo.Length];
        bool extraFlag = flagByte >= 0xF0 && flagByte < 0xFF;

        if (extraFlag)
        {
            if (lengthInfo.Length + 2 < packet.Length
                && packet[lengthInfo.Length + 1] == 0xFF
                && packet[lengthInfo.Length + 2] == 0xFF)
            {
                _sink.CompressedPacket(packet.Length, true);
                DecompressPacket(packet, lengthInfo.Length, true, arrivedAt);
                return;
            }
        }
        else
        {
            if (lengthInfo.Length + 1 < packet.Length
                && packet[lengthInfo.Length] == 0xFF
                && packet[lengthInfo.Length + 1] == 0xFF)
            {
                _sink.CompressedPacket(packet.Length, false);
                DecompressPacket(packet, lengthInfo.Length, false, arrivedAt);
                return;
            }
        }

        int opcodeOffset = lengthInfo.Length + (extraFlag ? 1 : 0);
        if (opcodeOffset + 1 >= packet.Length)
        {
            return;
        }

        int opcodeKey = (packet[opcodeOffset] & 0xFF) | ((packet[opcodeOffset + 1] & 0xFF) << 8);
        OpcodeNames.TryGetValue(opcodeKey, out string? name);
        _sink.Dispatch(opcodeKey, name, extraFlag, packet.Length);

        if (name is null)
        {
            _sink.UnknownOpcode(opcodeKey, extraFlag, packet.Length);
            return;
        }

        // L3b/L3c: invoke the matching handler here (parsingDamage / parseBuffPacket / ...).
        // For L3a the known-opcode handler is intentionally a no-op.
    }

    private void DecompressPacket(byte[] packet, int headerLength, bool extraFlag, long arrivedAt)
    {
        try
        {
            int offset = headerLength + 2;
            if (extraFlag)
            {
                offset += 1;
            }

            int originLength = PacketPrimitives.ParseUInt32Le(packet, offset);
            offset += 4;

            var restored = new byte[originLength];
            // lz4-java fastDecompressor.decompress(src, srcOff, dst, 0, originLength) == K4os BLOCK
            // decode with a known output length.
            LZ4Codec.Decode(packet.AsSpan(offset, packet.Length - offset), restored.AsSpan(0, originLength));

            int innerOffset = 0;
            while (innerOffset < restored.Length)
            {
                int pastInnerOffset = innerOffset;
                VarIntOutput lengthInfo = PacketPrimitives.ReadVarInt(restored, innerOffset);
                if (lengthInfo.Value == 0)
                {
                    innerOffset += 1;
                    continue;
                }

                int realLength = lengthInfo.Value + lengthInfo.Length - 4;
                if (realLength <= 0)
                {
                    _sink.ParserError("decompress", "invalid_inner_length");
                    break;
                }

                OnPacketReceived(restored[pastInnerOffset..(pastInnerOffset + realLength)], arrivedAt);
                innerOffset += realLength;
            }
        }
        catch (Exception e)
        {
            _sink.ParserError("decompress", e.GetType().Name);
        }
    }
}

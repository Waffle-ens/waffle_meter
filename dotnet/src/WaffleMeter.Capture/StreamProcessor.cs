using K4os.Compression.LZ4;

namespace WaffleMeter.Capture;

/// <summary>
/// Diagnostics/observation hooks mirroring the Kotlin <c>PacketDebugLogger</c> calls inside
/// <c>StreamProcessor</c>. The host (live app, replay CLI, tests) supplies a sink to observe
/// dispatch/decompression/damage decisions; the parser logic itself stays pure.
/// </summary>
public interface IStreamProcessorSink
{
    void Dispatch(int opcode, string? opcodeName, bool extraFlag, int len);
    void UnknownOpcode(int opcode, bool extraFlag, int len);
    void CompressedPacket(int len, bool extraFlag);
    void ParserError(string stage, string reason);

    /// <summary>A parsed direct/DoT damage event (Kotlin PacketDebugLogger.damage). reason is set
    /// only when saved == false; mobCode requires runtime mob state (deferred — null for now).</summary>
    void Damage(string kind, ParsedDamagePacket packet, bool saved, string? reason, int? mobCode);
}

/// <summary>No-op sink (default).</summary>
public sealed class NullStreamProcessorSink : IStreamProcessorSink
{
    public static readonly NullStreamProcessorSink Instance = new();
    public void Dispatch(int opcode, string? opcodeName, bool extraFlag, int len) { }
    public void UnknownOpcode(int opcode, bool extraFlag, int len) { }
    public void CompressedPacket(int len, bool extraFlag) { }
    public void ParserError(string stage, string reason) { }
    public void Damage(string kind, ParsedDamagePacket packet, bool saved, string? reason, int? mobCode) { }
}

/// <summary>
/// Verbatim port of the dispatch + decompression + damage core of Kotlin <c>StreamProcessor</c>
/// (src/main/kotlin/packet/StreamProcessor.kt).
///
/// Ported so far:
///  - onPacketReceived (91-129): opcode routing + FF FF LZ4 (K4os BLOCK) decompression (L3a).
///  - parsingDamage (593-712), parseDoTPacket (367-448): direct/DoT damage parsing (L3b).
///  - via <see cref="DamageParsing"/>: tryParseMultiHit, parseSpecialDamageFlags, normalizeDamageSkillCode.
///
/// Deferred to later phases (no-ops here): the non-damage handlers (nickname/buff/summon/battle/...
/// L3c), and the data-layer side effects DataManager.saveRawPacket / saveDamage / mobId(target) /
/// touchDummyBattle (Phase 3). Consequently the damage event's mobCode is reported as null.
///
/// CORRECTNESS-CRITICAL: offsets, the signed-byte extraFlag range, FF FF detection, the
/// switch&amp;0x0F -> field-width map, the Restoration +2 shift, and all bounds guards must match
/// Kotlin exactly — a divergence silently mis-counts or drops damage.
/// </summary>
public sealed class StreamProcessor
{
    private const int Mask = 0x0F;

    // Opcode key = b1 | (b2 << 8). Kotlin Opcode sealed class (StreamProcessor.kt:32-51).
    private static int Key(int b1, int b2) => b1 | (b2 << 8);

    private const int DamageKey = 0x04 | (0x38 << 8); // 0x3804
    private const int DoTKey = 0x05 | (0x38 << 8);    // 0x3805

    private static readonly Dictionary<int, string> OpcodeNames = new()
    {
        [Key(0x33, 0x36)] = "OwnNickname",
        [Key(0x55, 0x36)] = "OwnCombatPower",
        [Key(0x44, 0x36)] = "OtherNickname",
        [Key(0x40, 0x36)] = "Summon",
        [DamageKey] = "Damage",
        [DoTKey] = "DoT",
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
    private readonly Func<long, bool> _skillExists;

    /// <param name="sink">Observation hooks (default no-op).</param>
    /// <param name="skillExists">Skill-code catalog membership for normalizeDamageSkillCode
    /// (skills.json). Default always-false makes normalization fall back, matching a meter with no
    /// skill table loaded.</param>
    public StreamProcessor(IStreamProcessorSink? sink = null, Func<long, bool>? skillExists = null)
    {
        _sink = sink ?? NullStreamProcessorSink.Instance;
        _skillExists = skillExists ?? (_ => false);
    }

    public void OnPacketReceived(byte[] packet, long arrivedAt)
    {
        if (packet.Length == 3)
        {
            return;
        }

        VarIntOutput lengthInfo = PacketPrimitives.ReadVarInt(packet);
        if (lengthInfo.Length < 0 || lengthInfo.Length >= packet.Length)
        {
            _sink.ParserError("processor", "invalid_packet_length_varint");
            return;
        }

        // Kotlin: packet[len] >= 0xf0.toByte() && packet[len] < 0xff.toByte() (signed byte). The
        // signed range [-16,-2] equals the unsigned range [0xF0,0xFE], so this unsigned test matches.
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

        switch (opcodeKey)
        {
            case DamageKey:
                ParsingDamage(packet, extraFlag, arrivedAt);
                break;
            case DoTKey:
                ParseDoTPacket(packet, extraFlag, arrivedAt);
                break;
            // Other known opcodes (nickname/buff/summon/battle/join/...) are ported in L3c.
        }
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

    /// <summary>Direct damage (opcode 0x3804). Verbatim port of Kotlin parsingDamage (593-712).</summary>
    private void ParsingDamage(byte[] packet, bool extraFlag, long arrivedAt)
    {
        if (packet[0] == 0x20)
        {
            return;
        }

        int offset = 0;
        VarIntOutput packetLengthInfo = PacketPrimitives.ReadVarInt(packet);
        if (packetLengthInfo.Length < 0)
        {
            return;
        }

        var pdp = new ParsedDamagePacket();
        offset += packetLengthInfo.Length;
        if (extraFlag)
        {
            offset += 1;
        }

        if (offset >= packet.Length) return;
        if (packet[offset] != 0x04) return;
        if (packet[offset + 1] != 0x38) return;
        offset += 2;
        if (offset >= packet.Length) return;

        VarIntOutput targetInfo = PacketPrimitives.ReadVarInt(packet, offset);
        if (targetInfo.Length < 0) return;
        pdp.TargetId = targetInfo.Value;
        offset += targetInfo.Length;
        if (offset >= packet.Length) return;

        VarIntOutput switchInfo = PacketPrimitives.ReadVarInt(packet, offset);
        if (switchInfo.Length < 0) return;
        pdp.SwitchVariable = switchInfo.Value;
        offset += switchInfo.Length;
        if (offset >= packet.Length) return;

        VarIntOutput flagInfo = PacketPrimitives.ReadVarInt(packet, offset);
        if (flagInfo.Length < 0) return;
        pdp.Flag = flagInfo.Value;
        offset += flagInfo.Length;
        if (offset >= packet.Length) return;

        VarIntOutput actorInfo = PacketPrimitives.ReadVarInt(packet, offset);
        if (actorInfo.Length < 0) return;
        pdp.ActorId = actorInfo.Value;
        offset += actorInfo.Length;
        if (offset >= packet.Length) return;

        if (offset + 5 >= packet.Length) return;

        int temp = offset;
        int skillCodeCandidate = PacketPrimitives.ParseUInt32Le(packet, offset);
        pdp.SkillCode = DamageParsing.NormalizeDamageSkillCode(skillCodeCandidate, (skillCodeCandidate / 10) * 10, _skillExists);
        offset = temp + 5;

        VarIntOutput typeInfo = PacketPrimitives.ReadVarInt(packet, offset);
        if (typeInfo.Length < 0) return;
        pdp.Type = typeInfo.Value;
        offset += typeInfo.Length;
        if (offset >= packet.Length) return;

        // Kotlin reads packet[offset] into an unused 'damageType' local here — no effect, skipped.

        int andResult = switchInfo.Value & Mask;
        int start = offset;
        int tempV = andResult switch
        {
            4 => 8,
            5 => 12,
            6 => 10,
            7 => 14,
            _ => -1, // Kotlin: else -> return false
        };
        if (tempV < 0) return;
        if (start + tempV > packet.Length) return;

        pdp.Specials = DamageParsing.ParseSpecialDamageFlags(packet[start..(start + tempV)]);
        if (pdp.Specials.Contains(SpecialDamage.Restoration))
        {
            offset += 2;
        }

        offset += tempV;
        if (offset >= packet.Length) return;

        VarIntOutput unknownInfo = PacketPrimitives.ReadVarInt(packet, offset);
        if (unknownInfo.Length < 0) return;
        pdp.Unknown = unknownInfo.Value;
        offset += unknownInfo.Length;
        if (offset >= packet.Length) return;

        VarIntOutput damageInfo = PacketPrimitives.ReadVarInt(packet, offset);
        if (damageInfo.Length < 0) return;
        pdp.Damage = damageInfo.Value;
        offset += damageInfo.Length;
        if (offset >= packet.Length) return;

        MultiHitOutput multiHitInfo = DamageParsing.TryParseMultiHit(packet, offset);
        pdp.Loop = multiHitInfo.Time;
        offset = multiHitInfo.NewOffset + 2; // vestigial; offset unused afterwards

        pdp.Timestamp = arrivedAt;
        if (pdp.ActorId == pdp.TargetId)
        {
            _sink.Damage("direct", pdp, false, "actor_equals_target", null);
            return;
        }

        if (pdp.Damage >= 10000000)
        {
            _sink.Damage("direct", pdp, false, "damage_guard", null);
            return;
        }

        // Kotlin here: mobCode = DataManager.mobId(target); saveDamage(...); touchDummyBattle(...)
        // — runtime data-layer side effects deferred to Phase 3, so mobCode is null for now.
        _sink.Damage("direct", pdp, true, null, null);
    }

    /// <summary>Damage-over-time (opcode 0x3805). Verbatim port of Kotlin parseDoTPacket (367-448).</summary>
    private void ParseDoTPacket(byte[] packet, bool extraFlag, long arrivedAt)
    {
        int offset = 0;
        var pdp = new ParsedDamagePacket { Dot = true };
        VarIntOutput packetLengthInfo = PacketPrimitives.ReadVarInt(packet);
        if (packetLengthInfo.Length < 0) return;
        offset += packetLengthInfo.Length;
        if (extraFlag)
        {
            offset += 1;
        }

        if (packet[offset] != 0x05) return;
        if (packet[offset + 1] != 0x38) return;
        offset += 2;
        if (packet.Length < offset) return;

        VarIntOutput targetInfo = PacketPrimitives.ReadVarInt(packet, offset);
        if (targetInfo.Length < 0) return;
        offset += targetInfo.Length;
        if (packet.Length < offset) return;
        pdp.TargetId = targetInfo.Value;

        int unknownBitFlagByte = packet[offset];
        if ((unknownBitFlagByte & 0x02) == 0) return; // not a damage-bearing DoT -> no record
        offset++;
        if (packet.Length < offset) return;

        VarIntOutput actorInfo = PacketPrimitives.ReadVarInt(packet, offset);
        if (actorInfo.Length < 0) return;
        if (actorInfo.Value == targetInfo.Value) return; // no record (early return false in Kotlin)
        offset += actorInfo.Length;
        if (packet.Length < offset) return;
        pdp.ActorId = actorInfo.Value;

        VarIntOutput unknownInfo = PacketPrimitives.ReadVarInt(packet, offset);
        if (unknownInfo.Length < 0) return;
        offset += unknownInfo.Length;

        int skillCodeCandidate = PacketPrimitives.ParseUInt32Le(packet, offset);
        pdp.SkillCode = DamageParsing.NormalizeDamageSkillCode(skillCodeCandidate, skillCodeCandidate / 100, _skillExists);
        offset += 4;
        if (packet.Length <= offset) return;

        VarIntOutput damageInfo = PacketPrimitives.ReadVarInt(packet, offset);
        if (damageInfo.Length < 0) return;
        pdp.Damage = damageInfo.Value;

        // actor != target is guaranteed above; the Kotlin else branch is dead code.
        pdp.Timestamp = arrivedAt;
        _sink.Damage("dot", pdp, true, null, null);
    }
}

using System.Globalization;
using System.Text;
using K4os.Compression.LZ4;

namespace WaffleMeter.Capture;

/// <summary>
/// Diagnostics/observation hooks mirroring the Kotlin <c>PacketDebugLogger</c> calls inside
/// <c>StreamProcessor</c>. The host (live app, replay CLI, tests) supplies a sink to observe
/// dispatch/decompression/damage/meta/battle decisions; the parser logic itself stays pure.
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

    /// <summary>A non-damage state event (Kotlin PacketDebugLogger.meta). The fields mirror the
    /// exact key/value set Kotlin logs for that meta type.</summary>
    void Meta(string type, params (string Key, object? Value)[] fields);

    /// <summary>A battle start/end/rejection event (Kotlin PacketDebugLogger.battle).</summary>
    void Battle(int target, int toggle, int? mobCode, string? mobName, bool accepted, string? reason);
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
    public void Meta(string type, params (string Key, object? Value)[] fields) { }
    public void Battle(int target, int toggle, int? mobCode, string? mobName, bool accepted, string? reason) { }
}

/// <summary>
/// Verbatim port of the dispatch + decompression + parsing core of Kotlin <c>StreamProcessor</c>
/// (src/main/kotlin/packet/StreamProcessor.kt). Opcode routing + FF FF LZ4 decompression (L3a),
/// direct/DoT damage (L3b), and the meta scanners — nickname/own-power (L3c byte-derived) and
/// summon/mob_spawn/remain_hp/battle/buff (L3c via the data context).
///
/// Deferred data-layer side effects (Phase 3 wiring elsewhere): saveNickname / saveUserPower /
/// saveDamage / mobId(target) for the damage mobCode / saveSummon / mobHp / saveUseBuff /
/// startBattle / endBattle / touchDummyBattle. The parser only needs the mob catalog, the runtime
/// instanceId->mobCode map, and skill-code membership, supplied via <see cref="ICaptureGameData"/>.
///
/// CORRECTNESS-CRITICAL: offsets, the signed-byte extraFlag range, FF FF detection, the heuristic
/// scans, and all bounds guards must match Kotlin exactly.
/// </summary>
public sealed class StreamProcessor
{
    private const int Mask = 0x0F;

    private static int Key(int b1, int b2) => b1 | (b2 << 8);

    private const int DamageKey = 0x04 | (0x38 << 8);          // 0x3804
    private const int DoTKey = 0x05 | (0x38 << 8);             // 0x3805
    // 2026-06-10 server patch inserted a message into the 0x36 category, shifting every 0x36 opcode
    // whose first byte >= 0x40 by +1 (Kotlin StreamProcessor fix 88ca14e / release v1.7.9). Other
    // categories (0x38 damage, 0x8D battle) are untouched. OwnNickname (0x33 < 0x40) is unchanged.
    private const int OwnNicknameKey = 0x33 | (0x36 << 8);     // 0x3633 (unchanged)
    private const int OtherNicknameKey = 0x45 | (0x36 << 8);   // 0x3645 (was 0x3644)
    private const int OwnCombatPowerKey = 0x56 | (0x36 << 8);  // 0x3656 (was 0x3655)
    private const int SummonKey = 0x41 | (0x36 << 8);          // 0x3641 (was 0x3640)
    private const int BuffApplyKey = 0x2A | (0x38 << 8);       // 0x382A
    private const int BuffApply2Key = 0x2B | (0x38 << 8);      // 0x382B
    private const int BattleToggleKey = 0x21 | (0x8D << 8);    // 0x8D21
    private const int RemainHpKey = 0x00 | (0x8D << 8);        // 0x8D00
    // Party join-request family (0x97 category — untouched by the 2026-06-10 0x36 shift).
    private const int JoinRequestKey = 0x07 | (0x97 << 8);     // 0x9707
    private const int CancelJoinKey = 0x25 | (0x97 << 8);      // 0x9725
    private const int AdmitJoinKey = 0x0B | (0x97 << 8);       // 0x970B
    private const int RefuseJoinKey = 0x09 | (0x97 << 8);      // 0x9709
    private const int InstanceStartKey = 0x18 | (0x97 << 8);   // 0x9718
    private const int ExitPartyKey = 0x1D | (0x97 << 8);       // 0x971D
    private const int PartyRosterKey = 0x02 | (0x97 << 8);     // 0x9702 — full party/raid roster snapshot

    private static readonly Dictionary<int, string> OpcodeNames = new()
    {
        [OwnNicknameKey] = "OwnNickname",
        [OwnCombatPowerKey] = "OwnCombatPower",
        [OtherNicknameKey] = "OtherNickname",
        [SummonKey] = "Summon",
        [DamageKey] = "Damage",
        [DoTKey] = "DoT",
        [BuffApplyKey] = "BuffApply",
        [BuffApply2Key] = "BuffApply2",
        [BattleToggleKey] = "BattleToggle",
        [RemainHpKey] = "RemainHp",
        [Key(0x07, 0x97)] = "JoinRequest",
        [Key(0x25, 0x97)] = "CancelJoin",
        [Key(0x0B, 0x97)] = "AdmitJoin",
        [Key(0x09, 0x97)] = "RefuseJoin",
        [Key(0x18, 0x97)] = "InstanceStart",
        [Key(0x1D, 0x97)] = "ExitParty",
        [Key(0x02, 0x97)] = "PartyRoster",
    };

    private static readonly byte[] PowerMarker = { 0xF4, 0xCB, 0x1F };

    private readonly IStreamProcessorSink _sink;
    private readonly ICaptureGameData _data;
    private readonly IJoinRequestSink _joinSink;

    // Mirrors DataManager.executorId(): set from the own-nickname snapshot, read by 0x3655.
    private int _executorId;

    // Last own combat power seen from 0x3656 + the nickname it belonged to; carried onto a new executor
    // uid only on re-entry of the SAME character (req 3), so a different character never inherits it.
    private int _lastOwnPower;
    private string _lastOwnNickname = string.Empty;

    public StreamProcessor(IStreamProcessorSink? sink = null, ICaptureGameData? data = null, IJoinRequestSink? joinSink = null)
    {
        _sink = sink ?? NullStreamProcessorSink.Instance;
        _data = data ?? NullCaptureGameData.Instance;
        _joinSink = joinSink ?? NullJoinRequestSink.Instance;
    }

    /// <summary>
    /// Cheap structural check used by the connection classifier (NOT by the parser): does this assembled
    /// packet look like a GAME packet — a known opcode key, or an LZ4 (FF FF) compressed game packet?
    /// Lets the app tell the game stream apart from high-volume non-game noise (P2P/streaming) purely by
    /// content, so loopback/booster game paths — which DO yield game packets — are never excluded.
    /// Mirrors the header walk in <see cref="OnPacketReceived"/>; a false negative only delays a
    /// connection earning "game" status, a false positive only spares a noisy connection.
    /// </summary>
    public static bool LooksLikeGamePacket(byte[] packet)
    {
        if (packet.Length < 4)
        {
            return false;
        }

        VarIntOutput lengthInfo = PacketPrimitives.ReadVarInt(packet);
        if (lengthInfo.Length < 0 || lengthInfo.Length >= packet.Length)
        {
            return false;
        }

        int flagByte = packet[lengthInfo.Length];
        bool extraFlag = flagByte >= 0xF0 && flagByte < 0xFF;

        if (extraFlag)
        {
            if (lengthInfo.Length + 2 < packet.Length
                && packet[lengthInfo.Length + 1] == 0xFF && packet[lengthInfo.Length + 2] == 0xFF)
            {
                return true; // FF FF LZ4-compressed game packet
            }
        }
        else if (lengthInfo.Length + 1 < packet.Length
            && packet[lengthInfo.Length] == 0xFF && packet[lengthInfo.Length + 1] == 0xFF)
        {
            return true;
        }

        int opcodeOffset = lengthInfo.Length + (extraFlag ? 1 : 0);
        if (opcodeOffset + 1 >= packet.Length)
        {
            return false;
        }

        int opcodeKey = (packet[opcodeOffset] & 0xFF) | ((packet[opcodeOffset + 1] & 0xFF) << 8);
        return OpcodeNames.ContainsKey(opcodeKey);
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

        // Content-based capture feeds non-game / truncated TCP through here; a handler that reads past
        // a short buffer must be IGNORED (counted as a parser error), never crash the consumer. The
        // game stream is identified by which bytes parse cleanly as known opcodes.
        try
        {
            switch (opcodeKey)
            {
                case DamageKey:
                    ParsingDamage(packet, extraFlag, arrivedAt);
                    break;
                case DoTKey:
                    ParseDoTPacket(packet, extraFlag, arrivedAt);
                    break;
                case OwnNicknameKey:
                    SearchOwnNickname(packet, lengthInfo, arrivedAt);
                    break;
                case OtherNicknameKey:
                    SearchOtherNickname(packet, lengthInfo, arrivedAt);
                    break;
                case OwnCombatPowerKey:
                    ParseOwnCombatPower(packet, lengthInfo, extraFlag, arrivedAt);
                    break;
                case SummonKey:
                    ParseSummonPacket(packet, extraFlag);
                    break;
                case BattleToggleKey:
                    ParseBattlePacket(packet, lengthInfo, extraFlag);
                    break;
                case RemainHpKey:
                    ParseRemainHp(packet, lengthInfo, extraFlag);
                    break;
                case BuffApplyKey:
                case BuffApply2Key:
                    ParseBuffPacket(packet, lengthInfo, extraFlag, arrivedAt);
                    break;
                case JoinRequestKey:
                    ParseJoinRequest(packet, lengthInfo, extraFlag, arrivedAt);
                    break;
                case CancelJoinKey:
                    ParseCancelJoin(packet, lengthInfo, extraFlag);
                    break;
                case AdmitJoinKey:
                    ParseAdmitJoin(packet, lengthInfo, extraFlag);
                    break;
                case RefuseJoinKey:
                    ParseRefuseJoin(packet, lengthInfo, extraFlag);
                    break;
                case InstanceStartKey:
                    ParseInstanceStart(packet, lengthInfo, extraFlag);
                    break;
                case ExitPartyKey:
                    ParseExitParty(packet, lengthInfo, extraFlag);
                    break;
                case PartyRosterKey:
                    ParsePartyRoster(packet, lengthInfo, extraFlag);
                    break;
            }
        }
        catch (Exception e)
        {
            _sink.ParserError("dispatch", e.GetType().Name);
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
        pdp.SkillCode = DamageParsing.NormalizeDamageSkillCode(skillCodeCandidate, (skillCodeCandidate / 10) * 10, _data.SkillExists);
        offset = temp + 5;

        VarIntOutput typeInfo = PacketPrimitives.ReadVarInt(packet, offset);
        if (typeInfo.Length < 0) return;
        pdp.Type = typeInfo.Value;
        offset += typeInfo.Length;
        if (offset >= packet.Length) return;

        int andResult = switchInfo.Value & Mask;
        int start = offset;
        int tempV = andResult switch
        {
            4 => 8,
            5 => 12,
            6 => 10,
            7 => 14,
            _ => -1,
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

        _data.SaveDamage(pdp, _data.CurrentEpoch());
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
        if ((unknownBitFlagByte & 0x02) == 0) return;
        offset++;
        if (packet.Length < offset) return;

        VarIntOutput actorInfo = PacketPrimitives.ReadVarInt(packet, offset);
        if (actorInfo.Length < 0) return;
        if (actorInfo.Value == targetInfo.Value) return;
        offset += actorInfo.Length;
        if (packet.Length < offset) return;
        pdp.ActorId = actorInfo.Value;

        VarIntOutput unknownInfo = PacketPrimitives.ReadVarInt(packet, offset);
        if (unknownInfo.Length < 0) return;
        offset += unknownInfo.Length;

        int skillCodeCandidate = PacketPrimitives.ParseUInt32Le(packet, offset);
        pdp.SkillCode = DamageParsing.NormalizeDamageSkillCode(skillCodeCandidate, skillCodeCandidate / 100, _data.SkillExists);
        offset += 4;
        if (packet.Length <= offset) return;

        VarIntOutput damageInfo = PacketPrimitives.ReadVarInt(packet, offset);
        if (damageInfo.Length < 0) return;
        pdp.Damage = damageInfo.Value;

        pdp.Timestamp = arrivedAt;
        _data.SaveDamage(pdp, _data.CurrentEpoch());
        _sink.Damage("dot", pdp, true, null, null);
    }

    /// <summary>Own combat-power packet 0x3655. Kotlin parseOwnCombatPower (177-190).</summary>
    private void ParseOwnCombatPower(byte[] packet, VarIntOutput lengthInfo, bool extraFlag, long arrivedAt)
    {
        try
        {
            int opcodeOffset = lengthInfo.Length + (extraFlag ? 1 : 0);
            int valueOffset = opcodeOffset + 2;
            if (valueOffset + 4 > packet.Length) return;
            int power = PacketPrimitives.ParseUInt32Le(packet, valueOffset);
            if (power <= 0 || power > 10_000_000) return;
            int executor = _executorId;
            if (executor <= 0) return;
            _lastOwnPower = power; // remember for carry-forward onto re-entry uids (req 3)
            _data.SaveUserPower(executor, power);
            _sink.Meta("own_combat_power", ("uid", executor), ("power", power));
        }
        catch
        {
            // swallowed (matches Kotlin)
        }
    }

    // ---- party join-request handlers (Kotlin parseJoinRequest/CancelJoin/AdmitJoin/RefuseJoin/
    //      InstanceStart/ExitParty). Skill enrichment (official lookup) + rememberUserPower are deferred;
    //      the join UI carries nickname/server/job/power from the packet itself. ----

    /// <summary>0x9707: a party-join applicant. Byte layout corpus-verified.</summary>
    private void ParseJoinRequest(byte[] packet, VarIntOutput lengthInfo, bool extraFlag, long arrivedAt)
    {
        int offset = lengthInfo.Length;
        if (extraFlag) offset++;
        if (packet.Length < offset + 2) return;
        if (packet[offset] != 0x07 || packet[offset + 1] != 0x97) return;
        offset += 2;

        _ = PacketPrimitives.ParseUInt32Le(packet, offset); offset += 4; // roomNum (unused, parity)
        int requester = PacketPrimitives.ParseUInt32Le(packet, offset); offset += 4;
        _ = PacketPrimitives.ParseUInt32Le(packet, offset); offset += 4; // unknown2
        int jobCode = PacketPrimitives.ParseUInt32Le(packet, offset); offset += 4;
        _ = PacketPrimitives.ParseUInt32Le(packet, offset); offset += 4; // unknown4
        _ = PacketPrimitives.ParseUInt32Le(packet, offset); offset += 4; // unknown5

        VarIntOutput nameLen = PacketPrimitives.ReadVarInt(packet, offset);
        offset += nameLen.Length;
        string nickname = Encoding.UTF8.GetString(packet, offset, nameLen.Value);
        offset += nameLen.Value;

        int server = PacketPrimitives.ParseUInt16Le(packet, offset);
        offset += 6; // Kotlin skips 6 after reading the 2-byte server
        int power = PacketPrimitives.ParseUInt32Le(packet, offset);

        _sink.Meta("join_request", ("requester", requester), ("nickname", nickname), ("server", server), ("job", jobCode), ("power", power));
        _joinSink.OnJoinRequest(requester, nickname, jobCode, server, power, arrivedAt);
    }

    /// <summary>0x9725 cancel / 0x970B admit — both remove the request by requester id.</summary>
    /// <remarks>
    /// Byte-exact port of Kotlin parseCancelJoinRequest/parseAdmitJoinRequest (offset+2 past opcode,
    /// then a u32). NOTE: a corpus AdmitJoin frame (<c>35 0b 97 3a 02 aa 72 01 00 ...</c>) carries the
    /// real requester (<c>aa 72 01 00</c> = 94890) at offset+4, not offset+0 — Kotlin reads the two
    /// leading <c>3a 02</c> bytes into the id, so admit emits a non-matching id and the card is NOT
    /// removed on accept; it instead expires via the 20s timeout. This is faithful dev-app parity, not
    /// a fix. If instant removal-on-accept is wanted, re-derive the offset from the 3 corpus admits.
    /// </remarks>
    private void ParseCancelJoin(byte[] packet, VarIntOutput lengthInfo, bool extraFlag) =>
        RemoveByRequester(packet, lengthInfo, extraFlag, 0x25);

    private void ParseAdmitJoin(byte[] packet, VarIntOutput lengthInfo, bool extraFlag) =>
        RemoveByRequester(packet, lengthInfo, extraFlag, 0x0B);

    private void RemoveByRequester(byte[] packet, VarIntOutput lengthInfo, bool extraFlag, byte opcodeLow)
    {
        int offset = lengthInfo.Length;
        if (extraFlag) offset++;
        if (packet.Length < offset + 2) return;
        if (packet[offset] != opcodeLow || packet[offset + 1] != 0x97) return;
        offset += 2;
        int requester = PacketPrimitives.ParseUInt32Le(packet, offset);
        _joinSink.OnJoinRequestRemove(requester);
    }

    /// <summary>0x9709 refuse (no id) — drop the oldest pending request.</summary>
    private void ParseRefuseJoin(byte[] packet, VarIntOutput lengthInfo, bool extraFlag)
    {
        int offset = lengthInfo.Length;
        if (extraFlag) offset++;
        if (packet.Length < offset + 2) return;
        if (packet[offset] != 0x09 || packet[offset + 1] != 0x97) return;
        _joinSink.OnRefuseJoinRequest();
    }

    /// <summary>0x9718 instance-start / 0x971D exit-party — clear all pending requests.</summary>
    private void ParseInstanceStart(byte[] packet, VarIntOutput lengthInfo, bool extraFlag) =>
        ClearOnOpcode(packet, lengthInfo, extraFlag, 0x18);

    private void ParseExitParty(byte[] packet, VarIntOutput lengthInfo, bool extraFlag) =>
        ClearOnOpcode(packet, lengthInfo, extraFlag, 0x1D);

    private void ClearOnOpcode(byte[] packet, VarIntOutput lengthInfo, bool extraFlag, byte opcodeLow)
    {
        int offset = lengthInfo.Length;
        if (extraFlag) offset++;
        if (packet.Length < offset + 2) return;
        if (packet[offset] != opcodeLow || packet[offset + 1] != 0x97) return;
        _joinSink.OnExitPartyUi();
    }

    /// <summary>0x9702 full party/raid roster snapshot. Every member is encoded as a
    /// [serverId u16 LE][nameLen u8][name UTF-8] record (RE'd against a live party-join capture: the roster
    /// grows 2→3→4 as members join). Scan the packet for those records — gated on a valid server + a
    /// plausible name — and hand the whole set to the data layer, which matches each member to a known uid
    /// (by name+server, the same identity our 0x3645/0x3633 snapshots carry) for the pre-combat party
    /// preview. A full snapshot, so it REPLACES the roster.</summary>
    private void ParsePartyRoster(byte[] packet, VarIntOutput lengthInfo, bool extraFlag)
    {
        int offset = lengthInfo.Length + (extraFlag ? 1 : 0);
        if (offset + 2 > packet.Length || packet[offset] != 0x02 || packet[offset + 1] != 0x97)
        {
            return;
        }

        var members = new List<(string Nickname, int Server, int Slot)>();
        var seen = new HashSet<string>();
        for (int n = offset + 2; n + 3 < packet.Length; n++)
        {
            int server = PacketPrimitives.ParseUInt16Le(packet, n);
            if (!IsPartyServer(server))
            {
                continue;
            }

            int len = packet[n + 2] & 0xFF;
            if (len < 1 || len > 30 || n + 3 + len > packet.Length)
            {
                continue;
            }

            string name = Encoding.UTF8.GetString(packet, n + 3, len);
            if (Encoding.UTF8.GetByteCount(name) != len || !IsValidNickname(name))
            {
                continue;
            }

            if (seen.Add(name + " " + server.ToString(CultureInfo.InvariantCulture)))
            {
                members.Add((name, server, MemberSlot(packet, n)));
            }

            n += 2 + len; // skip past this record (the loop's n++ steps over the final byte)
        }

        if (members.Count == 0)
        {
            return;
        }

        _data.SavePartyRoster(members);

        var sb = new StringBuilder();
        foreach ((string nick, int srv, int slot) in members)
        {
            if (sb.Length > 0)
            {
                sb.Append(',');
            }

            sb.Append(nick).Append('[').Append(srv);
            if (slot > 0)
            {
                sb.Append('#').Append(slot);
            }

            sb.Append(']');
        }

        _sink.Meta("party_roster", ("count", members.Count), ("members", sb.ToString()));
    }

    // Sub-group slot (1-8) for a 0x9702 member, read from the fixed header preceding the matched server:
    // [7A/7E marker][slot 1-8][handle u16][00 00 00 00][server u16]. Slots 1-4 = party 1, 5-8 = party 2 for
    // an 8-인 공대 (see DataManager.CurrentPartySlots). 0 = header didn't match (slot unknown).
    private static int MemberSlot(byte[] packet, int serverOffset) =>
        serverOffset >= 8
        && packet[serverOffset - 4] == 0 && packet[serverOffset - 3] == 0
        && packet[serverOffset - 2] == 0 && packet[serverOffset - 1] == 0
        && packet[serverOffset - 7] is >= 1 and <= 8
            ? packet[serverOffset - 7]
            : 0;

    // Valid Aion2 server-id range for a party member record (same range our nickname snapshots use, so a
    // matched member's server lines up with its 0x3645/0x3633 identity). Tight enough to reject the random
    // [server][len][name]-shaped byte runs that would otherwise false-match inside the packet body.
    private static bool IsPartyServer(int server) =>
        (server is >= 1001 and <= 1021) || (server is >= 2001 and <= 2021);

    /// <summary>Own nickname snapshot 0x3633. Kotlin searchOwnNickname (192-250).</summary>
    private void SearchOwnNickname(byte[] packet, VarIntOutput lengthInfo, long arrivedAt)
    {
        int offset = lengthInfo.Length;
        if (packet[offset] != 0x33) return;
        if (packet[offset + 1] != 0x36) return;
        offset += 2;
        if (packet.Length < offset) return;

        VarIntOutput userInfo = PacketPrimitives.ReadVarInt(packet, offset);
        if (userInfo.Length < 0) return;
        offset += userInfo.Length;
        if (offset >= packet.Length) return;

        if (packet.Length < offset + 10) return;
        int spliterIdx = FindArrayIndex(packet[offset..(offset + 10)], 0x07);
        if (spliterIdx == -1) return;
        offset += spliterIdx + 1;

        VarIntOutput nameLengthInfo = PacketPrimitives.ReadVarInt(packet, offset);
        offset += nameLengthInfo.Length;
        if (nameLengthInfo.Length > 71) return;
        if (offset >= packet.Length) return;

        int server = -1;
        int job = -1;
        byte[] np = packet[offset..(offset + nameLengthInfo.Value)];
        string nickname = Encoding.UTF8.GetString(np);
        if (!IsValidNickname(nickname)) return;

        offset += nameLengthInfo.Value;
        if (packet.Length >= offset + 2)
        {
            server = PacketPrimitives.ParseUInt16Le(packet, offset);
            offset += 2;
            if (packet.Length >= offset + 1)
            {
                job = packet[offset] & 0xFF;
            }
        }

        // req 3 fix: own power comes from the live 0x3656 packet, which is keyed to the executor uid. On
        // re-entry the executor gets a NEW uid and 0x3656 often does not arrive again in-session, leaving
        // own power at 0 (the reported "my power shows wrong/0" bug). The own-nickname snapshot's marker
        // points at a different field (not the real power — verified ~106k vs the true ~380k), so it is
        // NOT a usable source. Instead carry the last known own power forward onto the new executor uid —
        // but only for a re-entry of the SAME character, so switching characters never inherits a stale
        // value. A fresh 0x3656 still refines it.
        bool sameCharacter = nickname == _lastOwnNickname;
        _lastOwnNickname = nickname;
        _executorId = userInfo.Value;
        _data.SaveNickname(userInfo.Value, nickname, true, server, job);
        if (server > 0)
        {
            _data.RequestOfficialCharacterLookup(userInfo.Value);
        }

        if (sameCharacter && _lastOwnPower > 0)
        {
            _data.SaveUserPower(userInfo.Value, _lastOwnPower);
        }
        else if (!sameCharacter)
        {
            _lastOwnPower = 0; // a different character: forget the old power; its own 0x3656/API sets it
        }

        _sink.Meta("nickname", ("own", true), ("uid", userInfo.Value), ("nickname", nickname), ("server", server), ("job", job), ("power", sameCharacter ? _lastOwnPower : 0));
    }

    /// <summary>Other-player nickname snapshot 0x3644. Kotlin searchOtherNickname (252-348).</summary>
    private void SearchOtherNickname(byte[] packet, VarIntOutput lengthInfo, long arrivedAt)
    {
        int offset = lengthInfo.Length;
        if (packet[offset] != 0x45) return; // was 0x44 (2026-06-10 0x36 +1 shift)
        if (packet[offset + 1] != 0x36) return;
        offset += 2;
        if (packet.Length < offset) return;

        VarIntOutput userInfo = PacketPrimitives.ReadVarInt(packet, offset);
        offset += userInfo.Length;
        if (packet.Length < offset) return;

        VarIntOutput unknownInfo1 = PacketPrimitives.ReadVarInt(packet, offset);
        offset += unknownInfo1.Length;
        if (packet.Length < offset) return;

        VarIntOutput unknownInfo2 = PacketPrimitives.ReadVarInt(packet, offset);
        offset += unknownInfo2.Length;
        if (packet.Length < offset) return;

        if (packet.Length - offset <= 2) return;
        offset += 1;
        int probeBase = offset;

        string? nickname = null;
        int nickEndOffset = -1;
        for (int i = 0; i < 5; i++)
        {
            offset = probeBase + i;
            if (packet.Length < offset) continue;
            VarIntOutput nicknameLengthInfo = PacketPrimitives.ReadVarInt(packet, offset);
            if (nicknameLengthInfo.Length <= 0) continue;
            offset += nicknameLengthInfo.Length;
            if (nicknameLengthInfo.Value < 1 || nicknameLengthInfo.Value > 71) continue;
            if (packet.Length < offset) continue;
            if (packet.Length < offset + nicknameLengthInfo.Value) continue;
            byte[] np = packet[offset..(offset + nicknameLengthInfo.Value)];
            string candidate = Encoding.UTF8.GetString(np);
            offset += nicknameLengthInfo.Value;
            if (!IsValidNickname(candidate)) continue;
            nickname = candidate;
            nickEndOffset = offset;
            break;
        }

        if (nickname is null || nickEndOffset == -1) return;
        offset = nickEndOffset;

        int job = packet[offset] & 0xFF;
        offset += 1;
        if (packet.Length < offset) return;
        int serverBase = offset;

        int server = -1;
        int i2 = 0;
        while (true)
        {
            offset = serverBase + i2;
            i2++;
            if (packet.Length < offset + 2) break;
            int serverCandidate = PacketPrimitives.ParseUInt16Le(packet, offset);
            if (!((serverCandidate is >= 1001 and <= 1021) || (serverCandidate is >= 2001 and <= 2021))) continue;
            offset += 2;
            if (packet.Length < offset) continue;
            VarIntOutput legionNameLengthInfo = PacketPrimitives.ReadVarInt(packet, offset);
            if (legionNameLengthInfo.Value < 2 || legionNameLengthInfo.Value > 24) continue;
            offset += legionNameLengthInfo.Length;
            if (packet.Length < offset + legionNameLengthInfo.Value) continue;
            byte[] lnp = packet[offset..(offset + legionNameLengthInfo.Value)];
            string legionNameCandidate = Encoding.UTF8.GetString(lnp);
            if (legionNameCandidate.Any(c => !char.IsDigit(c)))
            {
                server = serverCandidate;
            }
        }

        _data.SaveNickname(userInfo.Value, nickname, false, server, job);
        int? power = ParseSnapshotPower(packet);
        if (power != null)
        {
            _data.SaveUserPower(userInfo.Value, power.Value);
        }

        _sink.Meta("nickname", ("own", false), ("uid", userInfo.Value), ("nickname", nickname), ("server", server), ("job", job), ("power", power ?? 0));
    }

    /// <summary>Snapshot combat-power scan. Kotlin parseSnapshotPower (810-822).</summary>
    private static int? ParseSnapshotPower(byte[] packet)
    {
        int markerIdx = LastIndexOf(packet, PowerMarker);
        if (markerIdx < 0) return null;
        int offset = markerIdx + 11;
        while (offset + 8 <= packet.Length)
        {
            int power = PacketPrimitives.ParseUInt32Le(packet, offset);
            if (power is >= 1 and <= 10_000_000 && PacketPrimitives.ParseUInt32Le(packet, offset + 4) == 0)
            {
                return power;
            }

            offset += 1;
        }

        return null;
    }

    /// <summary>Summon / mob-spawn packet 0x3640. Kotlin parseSummonPacket (502-559). Emits a
    /// mob_spawn meta (and records the instanceId-&gt;mobCode map) plus a summon_map meta.</summary>
    private void ParseSummonPacket(byte[] packet, bool extraFlag)
    {
        int offset = 0;
        VarIntOutput packetLengthInfo = PacketPrimitives.ReadVarInt(packet);
        if (packetLengthInfo.Length < 0) return;
        offset += packetLengthInfo.Length;
        if (extraFlag)
        {
            offset += 1;
        }

        if (packet[offset] != 0x41) return; // was 0x40 (2026-06-10 0x36 +1 shift)
        if (packet[offset + 1] != 0x36) return;
        offset += 2;

        VarIntOutput summonInfo = PacketPrimitives.ReadVarInt(packet, offset);
        if (summonInfo.Length < 0) return;

        int codeMarkerIdx = FindArrayIndex(packet, 0x00, 0x40, 0x02);
        if (codeMarkerIdx == -1)
        {
            codeMarkerIdx = FindArrayIndex(packet, 0x00, 0x00, 0x02);
        }

        if (codeMarkerIdx != -1)
        {
            int mobCode = ((packet[codeMarkerIdx - 1] & 0xFF) << 16)
                          | ((packet[codeMarkerIdx - 2] & 0xFF) << 8)
                          | (packet[codeMarkerIdx - 3] & 0xFF);
            _data.SaveMobId(summonInfo.Value, mobCode);
            Mob? mob = _data.GetMob(mobCode);
            _sink.Meta("mob_spawn",
                ("instanceId", summonInfo.Value),
                ("mobCode", mobCode),
                ("mobName", mob?.Name),
                ("boss", mob?.Boss ?? false));
            // if (mob?.Boss == true) addon.parsingMobSpawnAddon(...) — deferred
        }

        int keyIdx = FindArrayIndex(packet, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF);
        if (keyIdx == -1) return;
        byte[] afterPacket = packet[(keyIdx + 8)..];

        int opcodeIdx = FindArrayIndex(afterPacket, 0x07, 0x02, 0x06);
        if (opcodeIdx == -1) return;
        offset = keyIdx + opcodeIdx + 11;

        if (offset + 2 > packet.Length) return;
        int realActorId = PacketPrimitives.ParseUInt16Le(packet, offset);

        _data.SaveSummon(summonInfo.Value, realActorId);
        _sink.Meta("summon_map", ("summonId", summonInfo.Value), ("ownerId", realActorId));
    }

    /// <summary>Boss remaining-HP packet 0x8D00. Kotlin parseRemainHp (1044-1073).</summary>
    private void ParseRemainHp(byte[] packet, VarIntOutput lengthInfo, bool extraFlag)
    {
        int offset = lengthInfo.Length;
        if (extraFlag)
        {
            offset++;
        }

        if (packet.Length < offset + 2) return;
        if (packet[offset] != 0x00) return;
        if (packet[offset + 1] != 0x8D) return;
        offset += 2;

        VarIntOutput mobIdInfo = PacketPrimitives.ReadVarInt(packet, offset);
        offset += mobIdInfo.Length;
        int? mobCode = _data.GetMobId(mobIdInfo.Value);
        if (mobCode is null) return;
        Mob? mob = _data.GetMob(mobCode.Value);
        if (mob is null) return;
        if (!mob.Boss) return;

        offset += PacketPrimitives.ReadVarInt(packet, offset).Length;
        offset += PacketPrimitives.ReadVarInt(packet, offset).Length;
        offset += PacketPrimitives.ReadVarInt(packet, offset).Length;

        int mobHp = PacketPrimitives.ParseUInt32Le(packet, offset);
        _data.SaveMobHp(mobIdInfo.Value, mobHp);
        _sink.Meta("remain_hp",
            ("target", mobIdInfo.Value),
            ("mobCode", mobCode.Value),
            ("mobName", mob.Name),
            ("hp", mobHp));
    }

    /// <summary>Battle start/end toggle 0x8D21. Kotlin parseBattlePacket (878-924).</summary>
    private void ParseBattlePacket(byte[] packet, VarIntOutput lengthInfo, bool extraFlag)
    {
        int offset = lengthInfo.Length;
        if (extraFlag)
        {
            offset++;
        }

        if (packet.Length < offset + 2) return;
        if (packet[offset] != 0x21) return;
        if (packet[offset + 1] != 0x8D) return;
        offset += 2;

        VarIntOutput battleInfo = PacketPrimitives.ReadVarInt(packet, offset);
        if (battleInfo.Length <= 0) return;
        offset += battleInfo.Length;

        offset += PacketPrimitives.ReadVarInt(packet, offset).Length;
        VarIntOutput toggleInfo = PacketPrimitives.ReadVarInt(packet, offset);

        int? mobCode = _data.GetMobId(battleInfo.Value);
        if (mobCode is null)
        {
            _sink.Battle(battleInfo.Value, toggleInfo.Value, null, null, false, "mob_code_missing");
            return;
        }

        Mob? mob = _data.GetMob(mobCode.Value);
        if (mob is null)
        {
            _sink.Battle(battleInfo.Value, toggleInfo.Value, mobCode, null, false, "mob_missing");
            return;
        }

        if (!mob.Boss || mob.IsDummy)
        {
            _sink.Battle(battleInfo.Value, toggleInfo.Value, mobCode, mob.Name, false, "not_boss_or_dummy");
            return;
        }

        switch (toggleInfo.Value)
        {
            case 1:
                _data.StartBattle(battleInfo.Value);
                _sink.Battle(battleInfo.Value, toggleInfo.Value, mobCode, mob.Name, true, "start");
                break;
            case 0:
                _data.EndBattle(battleInfo.Value);
                _sink.Battle(battleInfo.Value, toggleInfo.Value, mobCode, mob.Name, true, "end");
                break;
            default:
                _sink.Battle(battleInfo.Value, toggleInfo.Value, mobCode, mob.Name, false, "unknown_toggle");
                break;
        }
    }

    /// <summary>Buff/debuff apply 0x382A/0x382B. Kotlin parseBuffPacket (1075-1130).</summary>
    private void ParseBuffPacket(byte[] packet, VarIntOutput lengthInfo, bool extraFlag, long arrivedAt)
    {
        try
        {
            int offset = lengthInfo.Length;
            if (extraFlag)
            {
                offset++;
            }

            if (packet[offset] != 0x2A && packet[offset] != 0x2B) return;
            if (packet[offset + 1] != 0x38) return;
            offset += 2;

            VarIntOutput targetInfo = PacketPrimitives.ReadVarInt(packet, offset);
            offset += targetInfo.Length + 2;

            offset += PacketPrimitives.ReadVarInt(packet, offset).Length;

            int skillCode = PacketPrimitives.ParseUInt32Le(packet, offset);
            offset += 4;

            if (skillCode < 110000000 || skillCode > 190000000)
            {
                if (skillCode >= 30000000 || skillCode < 20000000)
                {
                    return;
                }
            }

            long duration = PacketPrimitives.ReadUInt32LeAsLong(packet, offset);
            offset += 8;

            long serverTime = PacketPrimitives.ReadUInt64Le(packet, offset);
            offset += 8;

            VarIntOutput actorInfo = PacketPrimitives.ReadVarInt(packet, offset);

            if (duration == 4294967295L)
            {
                return;
            }

            _data.SaveUseBuff(targetInfo.Value, skillCode, arrivedAt, arrivedAt + duration, duration, actorInfo.Value);
            _sink.Meta("buff",
                ("target", targetInfo.Value),
                ("actor", actorInfo.Value),
                ("skill", skillCode),
                ("duration", duration),
                ("serverTime", serverTime));
        }
        catch
        {
            // swallowed (matches Kotlin's try/catch around parseBuffPacket)
        }
    }

    /// <summary>Kotlin isValidNickname (867-876).</summary>
    private static bool IsValidNickname(string str)
    {
        bool hasKoreanOrEnglish = str.Any(c =>
            (c >= '가' && c <= '힣') || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'));
        bool allValid = str.All(c =>
            (c >= '가' && c <= '힣') || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || char.IsDigit(c));
        return hasKoreanOrEnglish && allValid;
    }

    /// <summary>KMP first-occurrence index of a byte pattern (Kotlin findArrayIndex varargs).</summary>
    private static int FindArrayIndex(byte[] data, params int[] pattern)
    {
        if (pattern.Length == 0) return 0;

        var p = new byte[pattern.Length];
        for (int i = 0; i < pattern.Length; i++)
        {
            p[i] = (byte)pattern[i];
        }

        var lps = new int[p.Length];
        int len = 0;
        for (int i = 1; i < p.Length; i++)
        {
            while (len > 0 && p[i] != p[len]) len = lps[len - 1];
            if (p[i] == p[len]) len++;
            lps[i] = len;
        }

        int ii = 0, j = 0;
        while (ii < data.Length)
        {
            if (data[ii] == p[j])
            {
                ii++;
                j++;
                if (j == p.Length) return ii - j;
            }
            else if (j > 0)
            {
                j = lps[j - 1];
            }
            else
            {
                ii++;
            }
        }

        return -1;
    }

    /// <summary>Last-occurrence index of a byte pattern (Kotlin lastIndexOf, 786-799).</summary>
    private static int LastIndexOf(byte[] data, byte[] pattern)
    {
        if (pattern.Length == 0 || data.Length < pattern.Length) return -1;
        for (int i = data.Length - pattern.Length; i >= 0; i--)
        {
            bool matched = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (data[i + j] != pattern[j])
                {
                    matched = false;
                    break;
                }
            }

            if (matched) return i;
        }

        return -1;
    }
}

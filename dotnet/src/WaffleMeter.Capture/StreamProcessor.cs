using System.Text;
using K4os.Compression.LZ4;

namespace WaffleMeter.Capture;

/// <summary>
/// Diagnostics/observation hooks mirroring the Kotlin <c>PacketDebugLogger</c> calls inside
/// <c>StreamProcessor</c>. The host (live app, replay CLI, tests) supplies a sink to observe
/// dispatch/decompression/damage/meta decisions; the parser logic itself stays pure.
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
}

/// <summary>
/// Verbatim port of the dispatch + decompression + parsing core of Kotlin <c>StreamProcessor</c>
/// (src/main/kotlin/packet/StreamProcessor.kt).
///
/// Ported so far:
///  - onPacketReceived (91-129): opcode routing + FF FF LZ4 (K4os BLOCK) decompression (L3a).
///  - parsingDamage / parseDoTPacket (L3b): direct/DoT damage.
///  - searchOwnNickname / searchOtherNickname / parseOwnCombatPower (L3c, byte-derived): the
///    nickname snapshots (uid/nickname/server/job[/power], Hangul validation, snapshot-power marker
///    scan) and the own combat-power packet (0x3655).
///
/// Deferred: the catalog/runtime-state-dependent handlers (summon/mob_spawn, remain_hp, battle,
/// buff) which need mobs.json + the runtime mobId map + buff blacklist (Phase 3 data layer), and
/// the data-layer side effects (saveNickname/saveDamage/saveUserPower/mobId/touchDummyBattle). The
/// own combat-power packet needs the executor id; we track it locally from the own-nickname
/// snapshot (mirroring DataManager.executorId()).
///
/// CORRECTNESS-CRITICAL: offsets, the signed-byte extraFlag range, FF FF detection, the heuristic
/// nickname probing (Hangul ranges, server-code ranges, marker bytes), and all bounds guards must
/// match Kotlin exactly — a divergence silently mis-parses or drops events.
/// </summary>
public sealed class StreamProcessor
{
    private const int Mask = 0x0F;

    private static int Key(int b1, int b2) => b1 | (b2 << 8);

    private const int DamageKey = 0x04 | (0x38 << 8);          // 0x3804
    private const int DoTKey = 0x05 | (0x38 << 8);             // 0x3805
    private const int OwnNicknameKey = 0x33 | (0x36 << 8);     // 0x3633
    private const int OtherNicknameKey = 0x44 | (0x36 << 8);   // 0x3644
    private const int OwnCombatPowerKey = 0x55 | (0x36 << 8);  // 0x3655

    private static readonly Dictionary<int, string> OpcodeNames = new()
    {
        [OwnNicknameKey] = "OwnNickname",
        [OwnCombatPowerKey] = "OwnCombatPower",
        [OtherNicknameKey] = "OtherNickname",
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

    // Combat-power marker [F4 CB 1F] in nickname snapshots (Kotlin powerMarker).
    private static readonly byte[] PowerMarker = { 0xF4, 0xCB, 0x1F };

    private readonly IStreamProcessorSink _sink;
    private readonly Func<long, bool> _skillExists;

    // Mirrors DataManager.executorId(): set from the own-nickname snapshot, read by the own
    // combat-power packet. Single-threaded replay, so a plain field is sufficient.
    private int _executorId;

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

        // Kotlin signed-byte range [0xF0,0xFE]; unsigned test is identical.
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
            case OwnNicknameKey:
                SearchOwnNickname(packet, lengthInfo, arrivedAt);
                break;
            case OtherNicknameKey:
                SearchOtherNickname(packet, lengthInfo, arrivedAt);
                break;
            case OwnCombatPowerKey:
                ParseOwnCombatPower(packet, lengthInfo, extraFlag, arrivedAt);
                break;
            // summon/buff/battle/remain-hp/join handlers are ported with the data layer (Phase 3).
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

        // Kotlin: mobCode = DataManager.mobId(target); saveDamage(...); touchDummyBattle(...) — deferred (Phase 3).
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

        pdp.Timestamp = arrivedAt;
        _sink.Damage("dot", pdp, true, null, null);
    }

    /// <summary>Own combat-power packet 0x3655. Verbatim port of Kotlin parseOwnCombatPower (177-190).</summary>
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
            // DataManager.saveUserPower deferred.
            _sink.Meta("own_combat_power", ("uid", executor), ("power", power));
        }
        catch
        {
            // swallowed (matches Kotlin)
        }
    }

    /// <summary>Own nickname snapshot 0x3633. Verbatim port of Kotlin searchOwnNickname (192-250).</summary>
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

        // DataManager.saveNickname / requestOfficialCharacterLookup deferred. Track executor for 0x3655.
        _executorId = userInfo.Value;
        _sink.Meta("nickname", ("own", true), ("uid", userInfo.Value), ("nickname", nickname), ("server", server), ("job", job));
    }

    /// <summary>Other-player nickname snapshot 0x3644. Verbatim port of Kotlin searchOtherNickname (252-348).</summary>
    private void SearchOtherNickname(byte[] packet, VarIntOutput lengthInfo, long arrivedAt)
    {
        int offset = lengthInfo.Length;
        if (packet[offset] != 0x44) return;
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

            // legion-name break and PacketAddonManager.parse are commented out / deferred in Kotlin.
        }

        // DataManager.saveNickname deferred.
        int? power = ParseSnapshotPower(packet);
        _sink.Meta("nickname", ("own", false), ("uid", userInfo.Value), ("nickname", nickname), ("server", server), ("job", job), ("power", power ?? 0));
    }

    /// <summary>Snapshot combat-power scan. Verbatim port of Kotlin parseSnapshotPower (810-822).</summary>
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

    /// <summary>Kotlin isValidNickname (867-876): has Hangul/letter and all chars are Hangul/letter/digit.</summary>
    private static bool IsValidNickname(string str)
    {
        bool hasKoreanOrEnglish = str.Any(c =>
            (c >= '가' && c <= '힣') || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'));
        bool allValid = str.All(c =>
            (c >= '가' && c <= '힣') || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || char.IsDigit(c));
        return hasKoreanOrEnglish && allValid;
    }

    /// <summary>KMP first-occurrence index of a byte pattern (Kotlin findArrayIndex varargs, 450-476).</summary>
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

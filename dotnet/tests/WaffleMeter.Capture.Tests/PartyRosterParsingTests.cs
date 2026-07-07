using System.Text;
using WaffleMeter.Capture;
using Xunit;

namespace WaffleMeter.Capture.Tests;

/// <summary>
/// Spec for the 0x9702 party/raid roster snapshot parser. The member encoding
/// ([serverId u16 LE][nameLen u8][name UTF-8]) was reverse-engineered against a live party-join capture
/// (20260618-094041), where the roster grew 플러시 → +Wildz → +으니야 → +컬리 as members joined. The
/// sub-group slot (party 1 = slots 1-4, party 2 = slots 5-8) lives in the fixed record header that precedes
/// each member's server — RE'd against an 8-인 공대 capture (20260611-201458, replayed verbatim below).
/// </summary>
public sealed class PartyRosterParsingTests
{
    private sealed class RecordingData : ICaptureGameData
    {
        public IReadOnlyList<(string Nickname, int Server, int Slot)>? Last;

        public void SavePartyRoster(IReadOnlyList<(string Nickname, int Server, int Slot)> members) => Last = members;
        public void SaveAetherStatus(bool split, int baseVal, int bonus, int total) { }
        public void SaveFieldBossTimers(IReadOnlyList<(int Code, long TargetMs)> timers) { }

        public Mob? GetMob(int code) => null;
        public int? GetMobId(int instanceId) => null;
        public void SaveMobId(int instanceId, int mobCode) { }
        public bool SkillExists(long code) => false;
        public long CurrentEpoch() => 0;
        public void SaveDamage(ParsedDamagePacket pdp, long epoch) { }
        public void StartBattle(int target) { }
        public void EndBattle(int target) { }
        public void SaveNickname(int uid, string nickname, bool isExecutor, int server, int jobByte) { }
        public void SaveUserPower(int uid, int power) { }
        public void SaveSummon(int summonId, int ownerId) { }
        public void SaveMobHp(int instanceId, int hp) { }
        public void SaveUseBuff(int uid, int skillCode, long buffStart, long buffEnd, long duration, int actorId) { }
        public void RequestOfficialCharacterLookup(int uid) { }
        public void TouchDummyBattle(int target, long epoch) { }
    }

    private sealed class NullSink : IStreamProcessorSink
    {
        public void Dispatch(int opcode, string? opcodeName, bool extraFlag, int len) { }
        public void UnknownOpcode(int opcode, bool extraFlag, int len) { }
        public void CompressedPacket(int len, bool extraFlag) { }
        public void ParserError(string stage, string reason) { }
        public void Damage(string kind, ParsedDamagePacket packet, bool saved, string? reason, int? mobCode) { }
        public void Battle(int target, int toggle, int? mobCode, string? mobName, bool accepted, string? reason) { }
        public void Meta(string type, params (string Key, object? Value)[] fields) { }
    }

    private static void AddMember(List<byte> body, string name, int server)
    {
        byte[] nm = Encoding.UTF8.GetBytes(name);
        body.Add((byte)(server & 0xFF));
        body.Add((byte)((server >> 8) & 0xFF));
        body.Add((byte)nm.Length);
        body.AddRange(nm);
    }

    // Full member record incl. the fixed header the parser reads the sub-group slot from:
    // [marker][slot 1-8][handle 6-byte LE][server u16][len][name]. The handle is a full SIX-byte field; its
    // magnitude is exactly what broke the old slot reader (high bytes != 0 when handle >= 0x10000), so it is
    // parameterized here. marker 0x7A/0x7E = existing member, 0x3A = just joined this snapshot.
    private static void AddMemberWithHeader(List<byte> body, string name, int server, int slot,
        byte marker = 0x7A, long handle = 0x0DB1)
    {
        byte[] nm = Encoding.UTF8.GetBytes(name);
        body.Add(marker);                                              // record marker
        body.Add((byte)slot);                                         // sub-group slot 1-8
        for (int i = 0; i < 6; i++)                                   // 6-byte LE handle
        {
            body.Add((byte)((handle >> (8 * i)) & 0xFF));
        }

        body.Add((byte)(server & 0xFF));
        body.Add((byte)((server >> 8) & 0xFF));
        body.Add((byte)nm.Length);
        body.AddRange(nm);
    }

    private static byte[] Packet(List<byte> body)
    {
        var packet = new byte[body.Count + 1];
        packet[0] = (byte)body.Count; // 1-byte length varint (value unchecked; only the prefix length matters)
        body.CopyTo(packet, 1);
        return packet;
    }

    private static byte[] Hex(string hex)
    {
        string[] tokens = hex.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        var bytes = new byte[tokens.Length];
        for (int i = 0; i < tokens.Length; i++)
        {
            bytes[i] = Convert.ToByte(tokens[i], 16);
        }

        return bytes;
    }

    [Fact]
    public void ParsePartyRoster_extracts_members_from_0x9702()
    {
        var data = new RecordingData();
        var proc = new StreamProcessor(new NullSink(), data, null);

        // [02 97][header bytes that must NOT false-match][member records...]
        var body = new List<byte> { 0x02, 0x97, 0xb4, 0x36, 0x05, 0x00, 0x04 };
        AddMember(body, "Wildz", 1014);
        AddMember(body, "플러시", 2003);
        AddMember(body, "으니야", 1010);

        proc.OnPacketReceived(Packet(body), 1000);

        Assert.NotNull(data.Last);
        Assert.Equal(3, data.Last!.Count);
        Assert.Contains(("Wildz", 1014, 0), data.Last);    // these test members have no record header -> slot 0
        Assert.Contains(("플러시", 2003, 0), data.Last);
        Assert.Contains(("으니야", 1010, 0), data.Last);
    }

    [Fact]
    public void ParsePartyRoster_rejects_out_of_range_servers_and_bad_lengths()
    {
        var data = new RecordingData();
        var proc = new StreamProcessor(new NullSink(), data, null);

        var body = new List<byte> { 0x02, 0x97 };
        AddMember(body, "Far", 5000);     // server out of the valid party range -> rejected
        AddMember(body, "Wildz", 1014);   // valid
        // a [server in range][len=39 > 30] sequence must be skipped (the real packet has these).
        body.Add(0xf6); body.Add(0x03); body.Add(0x27); body.Add(0xAA);

        proc.OnPacketReceived(Packet(body), 1000);

        Assert.NotNull(data.Last);
        Assert.Equal(new[] { ("Wildz", 1014, 0) }, data.Last);
    }

    [Fact]
    public void ParsePartyRoster_recovers_sub_party_slot_from_record_header()
    {
        var data = new RecordingData();
        var proc = new StreamProcessor(new NullSink(), data, null);

        // 4 members spanning both sub-parties — slots 1,2 (party 1) and 5,8 (party 2). Short ASCII names keep
        // the body < 128 so the 1-byte length prefix stays a single varint byte (names are arbitrary here).
        var body = new List<byte> { 0x02, 0x97, 0xb4, 0x36, 0x05, 0x00, 0x04 };
        AddMemberWithHeader(body, "Me", 2003, 1);
        AddMemberWithHeader(body, "Bb", 2003, 2);
        AddMemberWithHeader(body, "Cc", 1019, 5);
        AddMemberWithHeader(body, "Dd", 2005, 8);

        proc.OnPacketReceived(Packet(body), 1000);

        Assert.NotNull(data.Last);
        Assert.Equal(4, data.Last!.Count);
        Assert.Equal(new[] { 1, 2, 5, 8 }, new[] { data.Last[0].Slot, data.Last[1].Slot, data.Last[2].Slot, data.Last[3].Slot });
        Assert.Contains(("Me", 2003, 1), data.Last);   // slot 1 -> party 1
        Assert.Contains(("Dd", 2005, 8), data.Last);   // slot 8 -> party 2
    }

    [Fact]
    public void ParsePartyRoster_recovers_slot_for_large_handles_and_3A_marker()
    {
        var data = new RecordingData();
        var proc = new StreamProcessor(new NullSink(), data, null);

        // Real 06-23 members the old four-zero guard dropped to slot 0: their 6-byte handle exceeds 0x10000
        // (so the handle's high bytes are non-zero), and 플러시 carries the 0x3A "just joined" marker. The old
        // reader only recovered 가별 (handle fits in 2 bytes); now all three resolve.
        var body = new List<byte> { 0x02, 0x97, 0xb4, 0x36, 0x05, 0x00, 0x04 };
        AddMemberWithHeader(body, "가별", 2016, 1, marker: 0x7E, handle: 0x8287);    // 2-byte handle (old logic PASS)
        AddMemberWithHeader(body, "커스틴", 1007, 2, marker: 0x7A, handle: 0x17F86); // 3-byte handle (old logic FAIL)
        AddMemberWithHeader(body, "플러시", 2003, 3, marker: 0x3A, handle: 0x1D381); // 3-byte handle + 3A marker

        proc.OnPacketReceived(Packet(body), 1000);

        Assert.NotNull(data.Last);
        Assert.Equal(3, data.Last!.Count);
        Assert.Equal(new[] { 1, 2, 3 }, new[] { data.Last[0].Slot, data.Last[1].Slot, data.Last[2].Slot });
        Assert.Contains(("가별", 2016, 1), data.Last);
        Assert.Contains(("커스틴", 1007, 2), data.Last);
        Assert.Contains(("플러시", 2003, 3), data.Last);
    }

    [Fact]
    public void ParsePartyRoster_recovers_all_eight_slots_from_a_real_raid_packet()
    {
        // Verbatim decompressed 0x9702 packet from the 6/11 무스펠의 성배 8-인 공대 capture (20260611-201458):
        // 8 members in slots 1..8 → party 1 = 에이/콘팡/꼬북/몽몽, party 2 = 유꾸/용성/무좀/주리오 (콘팡 = 본인).
        byte[] packet = Hex(
            "E9 03 02 97 D4 7D 07 00 06 EC 84 B1 EC 97 AD 08 " +
            "F5 75 09 00 00 03 B1 0D 00 00 00 00 D3 07 FF 02 " +
            "03 08 7A 01 B1 0D 00 00 00 00 D3 07 06 EC 97 90 " +
            "EC 9D B4 0C 00 00 00 2D 00 00 00 C1 13 00 00 D3 " +
            "07 A1 0F 04 92 22 0A 00 00 00 00 00 01 02 00 00 " +
            "00 00 1C 02 00 00 00 00 00 00 7A 02 AC B3 00 00 " +
            "00 00 D3 07 06 EC BD 98 ED 8C A1 10 00 00 00 2D " +
            "00 00 00 26 13 00 00 D3 07 A1 0F 04 6C 02 0A 00 " +
            "00 00 00 00 01 02 00 00 00 00 82 00 00 00 00 00 " +
            "00 00 7A 03 3C 79 00 00 00 00 FB 03 06 EA BC AC " +
            "EB B6 81 1A 00 00 00 2D 00 00 00 D5 14 00 00 FB " +
            "03 A1 0F 04 4E EF 0A 00 00 00 00 00 01 02 00 00 " +
            "00 00 5C 01 00 00 00 00 00 00 7A 04 14 04 00 00 " +
            "00 00 D3 07 06 EB AA BD EB AA BD 20 00 00 00 2D " +
            "00 00 00 CD 12 00 00 D3 07 A1 0F 04 34 2B 09 00 " +
            "00 00 00 00 01 02 00 00 00 00 4A 01 00 00 00 00 " +
            "00 00 7A 05 B1 BB 00 00 00 00 D3 07 06 EC 9C A0 " +
            "EA BE B8 14 00 00 00 2D 00 00 00 02 14 00 00 D3 " +
            "07 A1 0F 04 51 18 0A 00 00 00 00 00 01 02 00 00 " +
            "00 00 B4 00 00 00 00 00 00 00 7A 06 9E 22 00 00 " +
            "00 00 D1 07 06 EC 9A A9 EC 84 B1 14 00 00 00 2D " +
            "00 00 00 60 13 00 00 D1 07 A1 0F 04 92 B5 09 00 " +
            "00 00 00 00 01 02 00 00 00 00 1F 01 00 00 00 00 " +
            "00 00 7A 07 D0 9C 00 00 00 00 D3 07 06 EB AC B4 " +
            "EC A2 80 24 00 00 00 2D 00 00 00 19 13 00 00 D3 " +
            "07 A1 0F 04 86 82 09 00 00 00 00 00 01 02 00 00 " +
            "00 00 6E 00 00 00 00 00 00 00 7A 08 A8 8D 00 00 " +
            "00 00 D5 07 09 EC A3 BC EB A6 AC EC 98 A4 20 00 " +
            "00 00 2D 00 00 00 0A 13 00 00 01 D5 07 A1 0F 04 " +
            "D5 3E 09 00 00 00 00 00 01 02 00 00 00 00 A5 00 " +
            "00 00 00 00 00 00 09");

        var data = new RecordingData();
        var proc = new StreamProcessor(new NullSink(), data, null);
        proc.OnPacketReceived(packet, 1000);

        Assert.NotNull(data.Last);
        Assert.Equal(
            new (string, int, int)[]
            {
                ("에이", 2003, 1), ("콘팡", 2003, 2), ("꼬북", 1019, 3), ("몽몽", 2003, 4),
                ("유꾸", 2003, 5), ("용성", 2001, 6), ("무좀", 2003, 7), ("주리오", 2005, 8),
            },
            data.Last);
    }
}

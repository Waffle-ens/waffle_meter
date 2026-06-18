using System.Text;
using WaffleMeter.Capture;
using Xunit;

namespace WaffleMeter.Capture.Tests;

/// <summary>
/// Spec for the 0x9702 party/raid roster snapshot parser. The member encoding
/// ([serverId u16 LE][nameLen u8][name UTF-8]) was reverse-engineered against a live party-join capture
/// (20260618-094041), where the roster grew 플러시 → +Wildz → +으니야 → +컬리 as members joined.
/// </summary>
public sealed class PartyRosterParsingTests
{
    private sealed class RecordingData : ICaptureGameData
    {
        public IReadOnlyList<(string Nickname, int Server)>? Last;

        public void SavePartyRoster(IReadOnlyList<(string Nickname, int Server)> members) => Last = members;

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

    private static byte[] Packet(List<byte> body)
    {
        var packet = new byte[body.Count + 1];
        packet[0] = (byte)body.Count; // 1-byte length varint (value unchecked; only the prefix length matters)
        body.CopyTo(packet, 1);
        return packet;
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
        Assert.Contains(("Wildz", 1014), data.Last);
        Assert.Contains(("플러시", 2003), data.Last);
        Assert.Contains(("으니야", 1010), data.Last);
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
        Assert.Equal(new[] { ("Wildz", 1014) }, data.Last);
    }
}

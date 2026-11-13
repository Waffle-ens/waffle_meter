using System.Text;
using WaffleMeter.Capture;
using Xunit;

namespace WaffleMeter.Capture.Tests;

/// <summary>
/// Spec for <see cref="LobbyRosterParser"/>. The fixtures are SYNTHETIC frames built in the shape measured on the
/// owner's 2026-11-13 capture (Login_RS 0x3615, two fresh logins) and the 2026-10-07 in-game switch (Lobby_RS
/// 0x3626) — the real frames carry an account session token in clear text and must not be committed.
/// Shape: <c>[prefix][count][record…]</c>, each record <c>[_key u32][len u8][nickname][_pc_id u32][_race]
/// [_gender][_level u32]</c> followed by ~3 KB of equipment/customizing bytes, and every account's list opens with
/// a reserved <c>$</c>-named slot.
/// </summary>
public sealed class LobbyRosterParserTests
{
    private const int BodyStart = 4; // 2-byte length var-int + 2 opcode bytes

    /// <summary>One record: head + a body of bytes that look like the real equipment/customizing run (mostly
    /// small values and zeros, a guild name after a zero i64 — the near-miss a head must not be mistaken for).</summary>
    private static byte[] Record(uint key, string name, int pcId = 16, byte race = 2, byte gender = 2, int level = 55)
    {
        var b = new List<byte>();
        b.AddRange(BitConverter.GetBytes(key));
        byte[] n = Encoding.UTF8.GetBytes(name);
        b.Add((byte)n.Length);
        b.AddRange(n);
        b.AddRange(BitConverter.GetBytes(pcId));
        b.Add(race);
        b.Add(gender);
        b.AddRange(BitConverter.GetBytes(level));
        b.AddRange(new byte[20]);                       // conqueror / item level / exp / combat level / combat exp
        byte[] guild = Encoding.UTF8.GetBytes("악단");
        b.Add((byte)guild.Length);                       // _guild_name — after a zero run, so "key" would read 0
        b.AddRange(guild);
        b.AddRange(BitConverter.GetBytes(1_763_546_103_750L)); // _logout_time
        for (int i = 0; i < 600; i++)
        {
            b.Add((byte)(i % 7 == 0 ? 0x64 : i % 5 == 0 ? 0x28 : 0)); // customizing-like run
        }

        return b.ToArray();
    }

    private static byte[] Frame(byte[] prefix, params byte[][] records) => Frame(prefix, records.Length, records);

    private static byte[] Frame(byte[] prefix, int declaredCount, params byte[][] records)
    {
        var b = new List<byte> { 0x00, 0x00, 0x15, 0x36 }; // stand-in length + opcode; BodyStart = 4
        b.AddRange(prefix);
        b.Add((byte)declaredCount);
        foreach (byte[] r in records)
        {
            b.AddRange(r);
        }

        b.Add(0); // _extended_character_slot_count
        b.AddRange(Encoding.ASCII.GetBytes("*92001:00000000-0000-0000-0000-000000000000\u00042003\u00045001"));
        return b.ToArray();
    }

    /// <summary>Login_RS: 2-byte prefix + two i64 clocks before the count (head at body offset 19).</summary>
    private static readonly byte[] LoginPrefix = new byte[18];

    /// <summary>Lobby_RS: the count sits at body offset 2 (head at 3).</summary>
    private static readonly byte[] LobbyPrefix = new byte[2];

    [Fact]
    public void Login_list_yields_the_accounts_characters_without_the_reserved_slot()
    {
        byte[] f = Frame(LoginPrefix,
            Record(11262, "$gbArGbhGgkX", pcId: 44, level: 1),
            Record(15062, "가나다", level: 55),
            Record(9054, "라마바사"),
            Record(9066, "Abc12"));

        LobbyRosterParse p = LobbyRosterParser.TryParse(f, BodyStart);

        Assert.True(p.Ok);
        Assert.Equal(4, p.Records);
        Assert.Equal(["가나다", "라마바사", "Abc12"], p.Nicknames);
    }

    [Fact]
    public void Lobby_list_reads_the_same_way()
    {
        byte[] f = Frame(LobbyPrefix, Record(14999, "$mfg9SiYTSWK", pcId: 44, level: 1), Record(15000, "꼬마"));

        LobbyRosterParse p = LobbyRosterParser.TryParse(f, BodyStart);

        Assert.True(p.Ok);
        Assert.Equal(["꼬마"], p.Nicknames);
    }

    /// <summary>Fail closed: a declared count that the heads found do not match yields nothing — no grouping beats
    /// two accounts wrongly joined.</summary>
    [Theory]
    [InlineData(3)]
    [InlineData(1)]
    public void A_count_the_heads_do_not_match_yields_nothing(int declared)
    {
        byte[] f = Frame(LoginPrefix, declared, Record(1, "$abcdefghijk"), Record(2, "가나"));

        Assert.False(LobbyRosterParser.TryParse(f, BodyStart).Ok);
    }

    /// <summary>The server-move reconnect sends this opcode with an empty list (77–79 B in the corpus).</summary>
    [Fact]
    public void The_empty_reconnect_list_yields_nothing()
    {
        byte[] f = Frame(LoginPrefix);

        Assert.False(LobbyRosterParser.TryParse(f, BodyStart).Ok);
    }

    /// <summary>A head found deep inside the body is not the start of this array — some other message.</summary>
    [Fact]
    public void A_first_head_far_from_the_start_is_not_this_message()
    {
        byte[] f = Frame(new byte[40], Record(2, "가나"));

        Assert.False(LobbyRosterParser.TryParse(f, BodyStart).Ok);
    }

    private sealed class RecordingData : ICaptureGameData
    {
        public readonly List<IReadOnlyList<string>> Rosters = [];

        public void SaveLobbyRoster(IReadOnlyList<string> nicknames) => Rosters.Add(nicknames);

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
        public void SaveMobHp(int instanceId, long hp) { }
        public void SaveUseBuff(int uid, int skillCode, long buffStart, long buffEnd, long duration, int actorId) { }
        public void RequestOfficialCharacterLookup(int uid) { }
        public void SavePartyRoster(IReadOnlyList<(string Nickname, int Server, int Slot)> members) { }
        public void SaveAetherStatus(int baseVal, int bonus) { }
        public void SaveShugoKey(int baseVal, int bonus) { }
        public void SaveFieldBossTimers(IReadOnlyList<(int Code, long TargetMs)> timers) { }
    }

    /// <summary>A wire frame: the length var-int carries body + 4 − its own length (StreamAssembler).</summary>
    private static byte[] Wire(byte opcodeLow, byte[] payload)
    {
        var body = new List<byte> { opcodeLow, 0x36 };
        body.AddRange(payload);
        for (int lenLen = 1; lenLen <= 3; lenLen++)
        {
            int value = body.Count + 4 - lenLen;
            var len = new List<byte>();
            uint v = (uint)value;
            while (v >= 0x80)
            {
                len.Add((byte)(v | 0x80));
                v >>= 7;
            }

            len.Add((byte)v);
            if (len.Count == lenLen)
            {
                return [.. len, .. body];
            }
        }

        throw new InvalidOperationException("frame too long for the test");
    }

    /// <summary>End to end through <see cref="StreamProcessor"/>: both opcodes reach the parser (registration in
    /// OpcodeNames is the dispatch switch) and hand the roster to the data layer; a reconnect's empty list does not.</summary>
    [Theory]
    [InlineData((byte)0x15, 18)] // 0x3615 Login_RS
    [InlineData((byte)0x26, 2)]  // 0x3626 Lobby_RS
    public void The_stream_processor_forwards_a_roster(byte opcodeLow, int prefixLength)
    {
        var payload = new List<byte>(new byte[prefixLength]) { 2 };
        payload.AddRange(Record(1, "$abcdefghijk", pcId: 44, level: 1));
        payload.AddRange(Record(2, "가나다"));
        payload.Add(0);

        var data = new RecordingData();
        var processor = new StreamProcessor(NullStreamProcessorSink.Instance, data);
        processor.OnPacketReceived(Wire(opcodeLow, payload.ToArray()), 0);
        processor.OnPacketReceived(Wire(opcodeLow, new byte[60]), 0); // reconnect: empty list

        Assert.Equal(["가나다"], Assert.Single(data.Rosters));
    }

    [Fact]
    public void Garbage_and_short_frames_yield_nothing()
    {
        Assert.False(LobbyRosterParser.TryParse([0x05, 0x00, 0x15, 0x36, 0x00], BodyStart).Ok);
        Assert.False(LobbyRosterParser.TryParse([], 0).Ok);
        Assert.False(LobbyRosterParser.TryParse([1, 2, 3], 10).Ok);
        Assert.False(LobbyRosterParser.TryParse(Enumerable.Range(0, 4000).Select(i => (byte)(i * 37)).ToArray(), BodyStart).Ok);
    }
}

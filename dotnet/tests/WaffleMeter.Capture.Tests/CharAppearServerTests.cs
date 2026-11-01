using System.Text;
using WaffleMeter.Capture;
using Xunit;
using F = WaffleMeter.Capture.Tests.CharAppearFixtures;

namespace WaffleMeter.Capture.Tests;

/// <summary>
/// 0x3645(<c>CharAppear_NT</c>) 가 남의 캐릭터 서버로 <c>_server_id</c>(소속 서버)를 내는가.
/// <para>제보(2026-11-01): 인게임 파티 창 딘[젠카] · 두시반[시엘] 인데 미터는 [오다] · [네자]. 두 사람 다 서버를 이전한 캐릭터였다.
/// 종전 스캔은 "서버 대역 u16 + 길이 2..24 + 숫자 아닌 문자열"의 <b>마지막</b> 적중을 써서, 프레임 뒤쪽 <c>_dbid</c> 의 상위 16비트
/// (= 생성 서버)를 읽고 있었다. 이전한 캐릭터는 그게 옛 서버다.</para>
/// <para>실프레임 둘은 각각 "길드 있음"(서버 바로 뒤 길드 구조체)·"길드 없음"(서버 바로 뒤 버프 배열) 레이아웃이고, 둘 다 프레임
/// 뒤쪽에 옛 서버가 실린 dbid 를 갖고 있어 종전 스캔으로는 틀린 값이 나온다(뮤테이션 확인: <c>SnapshotServerId</c> 를 끄면 둘 다 빨개진다).</para>
/// </summary>
public sealed class CharAppearServerTests
{
    private sealed class RecordingData : ICaptureGameData
    {
        public readonly List<(int Uid, string Nickname, int Server)> Names = [];

        public void SaveNickname(int uid, string nickname, bool isExecutor, int server, int jobByte) =>
            Names.Add((uid, nickname, server));

        public void SaveUserPower(int uid, int power) { }
        public Mob? GetMob(int code) => null;
        public int? GetMobId(int instanceId) => null;
        public void SaveMobId(int instanceId, int mobCode) { }
        public bool SkillExists(long code) => false;
        public long CurrentEpoch() => 0;
        public void SaveDamage(ParsedDamagePacket pdp, long epoch) { }
        public void StartBattle(int target) { }
        public void EndBattle(int target) { }
        public void SaveSummon(int summonId, int ownerId) { }
        public void SaveMobHp(int instanceId, long hp) { }
        public void SaveUseBuff(int uid, int skillCode, long buffStart, long buffEnd, long duration, int actorId) { }
        public void RequestOfficialCharacterLookup(int uid) { }
        public void SavePartyRoster(IReadOnlyList<(string Nickname, int Server, int Slot)> members) { }
        public void SaveAetherStatus(int baseVal, int bonus) { }
        public void SaveShugoKey(int baseVal, int bonus) { }
        public void SaveFieldBossTimers(IReadOnlyList<(int Code, long TargetMs)> timers) { }
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

    private static (int Uid, string Nickname, int Server) Parse(byte[] frame)
    {
        var data = new RecordingData();
        new StreamProcessor(new NullSink(), data).OnPacketReceived(frame, 0);
        return Assert.Single(data.Names);
    }

    // ---- 실프레임 ----

    [Fact]
    public void A_transferred_character_in_a_guild_gets_its_current_server_not_its_creation_server()
    {
        byte[] frame = F.Bytes(F.GuildedTransferred);
        Assert.Contains("2C4D01000000D10705", Convert.ToHexString(frame)); // 대조: 뒤쪽 _dbid(상위 = 2001) + _stats 개수 5 — 종전 스캔이 고르던 자리

        Assert.Equal((1044, "가나", 2003), Parse(frame));
    }

    [Fact]
    public void A_transferred_character_without_a_guild_gets_its_current_server_not_its_creation_server()
    {
        byte[] frame = F.Bytes(F.GuildlessTransferred);
        Assert.Contains("0AE801000000DF0705", Convert.ToHexString(frame)); // 대조: 뒤쪽 _dbid(상위 = 2015) + _stats 개수 5

        Assert.Equal((14848, "다라마", 2009), Parse(frame));
    }

    // ---- 합성 프레임 — 규칙 하나씩 ----

    /// <summary><c>[len][45 36][uid][0][0][00][nickLen][nick][job]</c> + <paramref name="tail"/>.</summary>
    private static byte[] Frame(int uid, string nickname, params byte[][] tail)
    {
        var body = new List<byte> { 0x45, 0x36 };
        WriteVarInt(body, uid);
        WriteVarInt(body, 0);
        WriteVarInt(body, 0);
        body.Add(0x00);
        byte[] name = Encoding.UTF8.GetBytes(nickname);
        WriteVarInt(body, name.Length);
        body.AddRange(name);
        body.Add(16); // job
        foreach (byte[] t in tail)
        {
            body.AddRange(t);
        }

        var frame = new List<byte>();
        WriteVarInt(frame, body.Count + 4);
        frame.AddRange(body);
        return frame.ToArray();
    }

    private static byte[] U16(int v) => [(byte)(v & 0xFF), (byte)(v >> 8)];

    private static byte[] GuildId(int server, long number) => BitConverter.GetBytes(((long)server << 48) | number);

    private static byte[] Str(string s)
    {
        byte[] b = Encoding.UTF8.GetBytes(s);
        return [(byte)b.Length, .. b];
    }

    /// <summary>종전 스캔이 마지막에 고르던 모양 — <c>[_dbid 하위 6바이트][생성 서버 u16][_stats 개수 5][5바이트]</c>.</summary>
    private static byte[] StaleDbidTail(int creationServer) =>
        [0x2C, 0x4D, 0x01, 0x00, 0x00, 0x00, .. U16(creationServer), 0x05, 0xCD, 0x00, 0x8D, 0x0F, 0x00];

    [Fact]
    public void The_server_right_before_a_guild_wins_over_a_later_dbid()
    {
        byte[] frame = Frame(321, "검증",
            new byte[24],                       // CharState 자리(서버 대역 값 없음)
            U16(2003), GuildId(2003, 7), Str("숲"), new byte[16],
            StaleDbidTail(2016));

        Assert.Equal((321, "검증", 2003), Parse(frame));
    }

    [Fact]
    public void The_server_right_before_the_buff_list_wins_when_there_is_no_guild()
    {
        byte[] frame = Frame(322, "검증",
            new byte[24],
            U16(2009), [0x11, 0x11, 0x5A, 0xE1, 0xAB, 0xCA, 0x08], new byte[16], // _abnormals: 개수 17 + 첫 레코드 마스크 0x11
            StaleDbidTail(2015));

        Assert.Equal((322, "검증", 2009), Parse(frame));
    }

    /// <summary>길드도 버프 배열도 아니면 종전 느슨한 스캔으로 물러선다 — 레이아웃이 바뀌어도 서버를 통째로 잃지는 않는다.</summary>
    [Fact]
    public void Without_either_anchor_the_loose_scan_still_supplies_a_server()
    {
        byte[] frame = Frame(323, "검증", U16(2003), Str("AAAA"));

        Assert.Equal((323, "검증", 2003), Parse(frame));
    }

    /// <summary>길드 id 하위 48비트가 0 이면 길드가 아니다 — CharState 안의 우연한 서버 대역 값이 길드 모양으로 오탐되지 않게.</summary>
    [Fact]
    public void A_zero_guild_number_is_not_a_guild_anchor()
    {
        byte[] frame = Frame(324, "검증",
            U16(2011), GuildId(2003, 0), Str("숲"),
            U16(2009), [0x11, 0x10, 0x00, 0x00]);

        Assert.Equal((324, "검증", 2009), Parse(frame));
    }

    private static void WriteVarInt(List<byte> buffer, int value)
    {
        uint v = (uint)value;
        while (v >= 0x80)
        {
            buffer.Add((byte)(v | 0x80));
            v >>= 7;
        }

        buffer.Add((byte)v);
    }
}

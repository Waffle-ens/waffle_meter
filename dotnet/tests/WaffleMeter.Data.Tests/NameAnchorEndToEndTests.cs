using System.Text;
using WaffleMeter.Capture;
using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>
/// 이름 앵커의 <b>배선</b>을 고정한다: 실제 0x9200 바이트 → <see cref="StreamProcessor"/> →
/// <see cref="ICaptureGameData"/> → 진짜 <see cref="DataManager"/> → 데미지 증거 → executor 이동.
/// <para>왜 별도 테스트가 필요한가: 직전 구현은 파서 테스트도 데이터 계층 테스트도 전부 초록이었는데 실제로는
/// <b>한 번도 실행되지 않았다</b>. 두 계층을 각각 스텁으로 검증하면 "둘이 실제로 연결돼 있는가"는 아무도
/// 확인하지 않기 때문이다. <c>TryBindExecutorByIdentity</c>는 인터페이스 기본 no-op 멤버라 오버로드 하나만
/// 어긋나도 경고 없이 조용히 죽는다 — 그 실패 모드를 이 테스트가 잡는다.</para>
/// </summary>
public sealed class NameAnchorEndToEndTests
{
    private const string Me = "플러시";
    private const int MyServer = 2003;
    private const int NewUid = 15360;

    private sealed class NullSink : IStreamProcessorSink
    {
        public readonly List<string> Errors = [];

        public void Dispatch(int opcode, string? opcodeName, bool extraFlag, int len) { }
        public void UnknownOpcode(int opcode, bool extraFlag, int len) { }
        public void CompressedPacket(int len, bool extraFlag) { }
        public void ParserError(string stage, string reason) => Errors.Add($"{stage}:{reason}");
        public void Damage(string kind, ParsedDamagePacket packet, bool saved, string? reason, int? mobCode) { }
        public void Battle(int target, int toggle, int? mobCode, string? mobName, bool accepted, string? reason) { }
        public void Meta(string type, params (string Key, object? Value)[] fields) { }
    }

    /// <summary>Wire-shaped 0x9200 frame carrying one member profile (geometry verified against a live capture).</summary>
    private static byte[] MemberProfileFrame(int uid, string name, int server)
    {
        byte[] nm = Encoding.UTF8.GetBytes(name);
        var body = new List<byte> { 0x00, 0x92 };
        body.Add((byte)(uid & 0xFF));
        body.Add((byte)((uid >> 8) & 0xFF));
        body.Add((byte)((uid >> 16) & 0xFF));
        body.Add((byte)((uid >> 24) & 0xFF));
        body.Add((byte)(server & 0xFF));
        body.Add((byte)((server >> 8) & 0xFF));
        body.Add(0x81);
        body.Add(0xD3);
        body.Add(0x24);
        body.AddRange(Encoding.ASCII.GetBytes("A1B2C3D4-E5F6-4788-99AA-BBCCDDEEFF00")); // 형식만 같은 합성 GUID
        for (int i = 0; i < 6; i++)
        {
            body.Add((byte)(0x27 + i));
        }

        body.Add((byte)(server & 0xFF));
        body.Add((byte)((server >> 8) & 0xFF));
        body.Add((byte)nm.Length);
        body.AddRange(nm);

        var packet = new byte[body.Count + 1];
        packet[0] = (byte)body.Count;
        body.CopyTo(packet, 1);
        return packet;
    }

    [Fact]
    public void A_real_0x9200_frame_rebinds_self_once_that_uid_fights()
    {
        long now = 1_000_000;
        var dm = new DataManager { Clock = () => now };
        var sink = new NullSink();
        var processor = new StreamProcessor(sink, dm);
        dm.SaveNickname(100, Me, isExecutor: true, server: MyServer, jobByte: 0); // 존 이동 전 본인

        processor.OnPacketReceived(MemberProfileFrame(NewUid, Me, MyServer), now);

        Assert.Empty(sink.Errors);
        Assert.Equal(100, dm.ExecutorId()); // 아직 후보일 뿐 — 데미지 증거 전에는 움직이지 않는다

        dm.SaveDamage(
            new ParsedDamagePacket { ActorId = NewUid, TargetId = 9999, SkillCode = 1, Damage = 100 },
            dm.CurrentEpoch());

        Assert.Equal(NewUid, dm.ExecutorId());
        Assert.True(dm.User(NewUid)?.IsExecutor);
    }

    [Fact]
    public void A_party_members_profile_never_moves_self()
    {
        long now = 1_000_000;
        var dm = new DataManager { Clock = () => now };
        var processor = new StreamProcessor(new NullSink(), dm);
        dm.SaveNickname(100, Me, isExecutor: true, server: MyServer, jobByte: 0);

        processor.OnPacketReceived(MemberProfileFrame(4857, "Wildz", 1014), now);
        dm.SaveDamage(
            new ParsedDamagePacket { ActorId = 4857, TargetId = 9999, SkillCode = 1, Damage = 100 },
            dm.CurrentEpoch());

        Assert.Equal(100, dm.ExecutorId());
    }
}

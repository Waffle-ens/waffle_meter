using WaffleMeter.Capture;
using Xunit;

namespace WaffleMeter.Capture.Tests;

/// <summary>
/// 0x3802(시전 시 쿨타임 시작) 파서 회귀 스펙. 프레임은 전부 실제 캡처에서 뽑은 바이트 그대로다.
/// <para>액터 varint 다음 바이트는 오랫동안 레이아웃 주석에 <c>[00]</c> 리터럴로 적혀 있었지만 실은 플래그이고,
/// 0x04·0x08 비트가 켜지면 프레임 끝에 varint가 하나 더 붙는다. 파서는 쿨타임을 "프레임의 마지막 varint"로
/// 읽으므로 그 프레임에서는 쿨타임 대신 버프/충전 잔여시간을 집어 온다. 게다가 그런 프레임은 전부 충전형
/// 스킬(NeedCoolTime=0)이라 서버가 말하는 진짜 쿨타임은 0 — 즉 "지금 쓸 수 있다"가 정답인데 미터는 반대로
/// "쿨 중"이라고 적는다. 코퍼스 5개 실측으로 오저장 165건 전량이 이 조건에 해당했다.</para>
/// </summary>
public class CooldownStartParsingTests
{
    private sealed class RecordingData : ICaptureGameData
    {
        public readonly List<(int SkillCode, long RemainingMs, long ArrivedAt, int ActorId, bool FromCast)> Saved = [];

        public void SaveCooldown(int skillCode, long remainingMs, long arrivedAt, int actorId, bool fromCast = false)
            => Saved.Add((skillCode, remainingMs, arrivedAt, actorId, fromCast));

        // 아래는 인터페이스가 기본 구현을 주지 않는 멤버들 — 이 스펙과 무관하다.
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
        public void SavePartyRoster(IReadOnlyList<(string Nickname, int Server, int Slot)> members) { }
        public void SaveAetherStatus(int baseVal, int bonus) { }
        public void SaveShugoKey(int baseVal, int bonus) { }
        public void SaveFieldBossTimers(IReadOnlyList<(int Code, long TargetMs)> timers) { }
    }

    private static byte[] Frame(string hex)
        => hex.Split(' ', StringSplitOptions.RemoveEmptyEntries)
              .Select(b => Convert.ToByte(b, 16))
              .ToArray();

    // 20260831 궁성 세션. actor=239, flag=0x00, skill=14220050(축복의 활), 꼬리 마지막 varint = 81,100ms.
    private const string BlessedBowCast =
        "1C 02 38 EF 01 00 12 FB D8 00 52 00 EF 01 85 75 0B 43 BC 9D 01 01 CC F9 04";

    // 20260817. actor=7858, flag=0x0C, skill=11110010(집중 막기, 충전형 · NeedCoolTime=0).
    // 마지막 varint는 15,100 — 쿨타임이 아니라 버프 잔여시간이다.
    private const string FocusedBlockCharge =
        "1C 02 38 B2 3D 0C 7A 86 A9 00 B8 00 B2 3D 7C 26 7D 42 90 4E 01 00 01 FC 75";

    // 같은 스킬, flag=0x08 만 켜진 변종.
    private const string FocusedBlockChargeFlag8 =
        "1B 02 38 B2 3D 08 7A 86 A9 00 DF 00 B2 3D 55 80 0A C3 90 4E 01 00 84 52";

    [Fact]
    public void Plain_cast_frame_stores_the_cooldown_and_marks_it_as_a_cast()
    {
        var data = new RecordingData();
        new StreamProcessor(data: data).OnPacketReceived(Frame(BlessedBowCast), 5_000);

        (int SkillCode, long RemainingMs, long ArrivedAt, int ActorId, bool FromCast) saved = Assert.Single(data.Saved);
        Assert.Equal(14_220_050, saved.SkillCode);
        Assert.Equal(81_100, saved.RemainingMs);
        Assert.Equal(5_000, saved.ArrivedAt);
        Assert.Equal(239, saved.ActorId);
        Assert.True(saved.FromCast, "0x3802 값은 잠정값이므로 시전 출처로 표시되어야 한다");
    }

    [Theory]
    [InlineData(FocusedBlockCharge)]
    [InlineData(FocusedBlockChargeFlag8)]
    public void Frame_with_a_trailing_extra_varint_stores_nothing(string hex)
    {
        var data = new RecordingData();
        new StreamProcessor(data: data).OnPacketReceived(Frame(hex), 5_000);

        // 여기서 저장이 일어나면 충전형 스킬이 버프가 떠 있는 내내 회색으로 칠해진다(호법성 쾌유의 주문 8.0초 전 구간).
        Assert.Empty(data.Saved);
    }

    [Fact]
    public void Truncated_frame_is_swallowed()
    {
        var data = new RecordingData();
        // 액터 varint 까지만 있고 플래그 바이트가 없는 프레임 — 인덱스 밖을 읽어 터지면 안 된다.
        new StreamProcessor(data: data).OnPacketReceived(Frame("05 02 38 B2 3D"), 5_000);

        Assert.Empty(data.Saved);
    }
}

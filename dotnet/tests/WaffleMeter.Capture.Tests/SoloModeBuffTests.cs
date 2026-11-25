using WaffleMeter.Capture;
using Xunit;

namespace WaffleMeter.Capture.Tests;

/// <summary>
/// 파티 던전 '1인 모드'(클라 134) 입장 버프가 미터까지 오는 길. 1743 과 19996x01 은 버프 파서의 코드 대역
/// 게이트 밖이고 직업 버프는 무기한이라, 전용 가로채기가 없으면 두 게이트에서 소리 없이 버려진다 —
/// 그래서 "buff.json 에 행을 넣으면 잡힌다"는 오해가 생기기 쉽다.
/// </summary>
public sealed class SoloModeBuffTests
{
    private sealed class RecordingData : ICaptureGameData
    {
        public readonly List<int> SoloBuffs = [];
        public readonly List<int> StoredBuffs = [];

        public void SaveSoloModeBuff(int skillCode, long arrivedAt) => SoloBuffs.Add(skillCode);

        public void SaveUseBuff(int uid, int skillCode, long buffStart, long buffEnd, long duration, int actorId) =>
            StoredBuffs.Add(skillCode);

        public void SaveUseBuff(int uid, int skillCode, long buffStart, long buffEnd, long duration, int actorId, int level, int slot) =>
            StoredBuffs.Add(skillCode);

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
        public void RequestOfficialCharacterLookup(int uid) { }
        public void SavePartyRoster(IReadOnlyList<(string Nickname, int Server, int Slot)> members) { }
        public void SaveAetherStatus(int baseVal, int bonus) { }
        public void SaveShugoKey(int baseVal, int bonus) { }
        public void SaveFieldBossTimers(IReadOnlyList<(int Code, long TargetMs)> timers) { }
    }

    private const int Uid = 12332;

    private static void WriteVarInt(List<byte> to, int value)
    {
        uint v = (uint)value;
        while (v >= 0x80)
        {
            to.Add((byte)(v | 0x80));
            v >>= 7;
        }

        to.Add((byte)v);
    }

    private static void WriteU32(List<byte> to, long value)
    {
        to.Add((byte)(value & 0xFF));
        to.Add((byte)((value >> 8) & 0xFF));
        to.Add((byte)((value >> 16) & 0xFF));
        to.Add((byte)((value >> 24) & 0xFF));
    }

    /// <summary>0x382A 최초 적용 — AbyssArtifactCountTests 와 같은 실측 배치.</summary>
    private static byte[] BuffApply(int target, long code, long duration)
    {
        var body = new List<byte> { 0x2A, 0x38 };
        WriteVarInt(body, target);
        body.Add(0x01);
        body.Add(0x11);
        WriteVarInt(body, 0x42);
        WriteU32(body, code);
        WriteU32(body, duration);
        for (int i = 0; i < 12; i++)
        {
            body.Add(0);
        }

        var frame = new List<byte>();
        WriteVarInt(frame, body.Count + 3);
        frame.AddRange(body);
        return frame.ToArray();
    }

    private static RecordingData Feed(params byte[][] frames)
    {
        var data = new RecordingData();
        var processor = new StreamProcessor(NullStreamProcessorSink.Instance, data);
        foreach (byte[] frame in frames)
        {
            processor.OnPacketReceived(frame, 0);
        }

        return data;
    }

    [Theory]
    [InlineData(1743)]
    [InlineData(19_996_401)] // 검성
    [InlineData(19_996_901)] // 마도성
    [InlineData(19_997_201)] // 권성
    public void The_catalog_knows_every_entry_buff(int code) =>
        Assert.True(SoloModeBuffCatalog.IsSoloModeBuff(code));

    /// <summary>이웃 코드는 아니다 — 100 간격 밖, 범위 밖, 같은 대역의 다른 시스템 스킬.</summary>
    [Theory]
    [InlineData(1742)]
    [InlineData(1744)] // 1인 모드 몹 보정(숨은 버프) — 몹에 걸리는 것이라 플래그 근거가 아니다
    [InlineData(19_996_301)]
    [InlineData(19_996_402)]
    [InlineData(19_997_301)]
    [InlineData(1_999_640)] // 버프를 거는 시스템 스킬 코드 자체
    public void Neighbouring_codes_are_not_entry_buffs(int code) =>
        Assert.False(SoloModeBuffCatalog.IsSoloModeBuff(code));

    /// <summary>무기한 직업 버프와 1743 이 두 게이트를 지나 1인 모드 신호로 도착한다.</summary>
    [Fact]
    public void Entry_buffs_reach_the_data_layer_past_both_gates()
    {
        RecordingData data = Feed(
            BuffApply(Uid, 1743, 0xFFFFFFFF),
            BuffApply(Uid, 19_996_701, 0xFFFFFFFF)); // 살성, 무기한

        Assert.Equal([1743, 19_996_701], data.SoloBuffs);
    }

    /// <summary>버프 저장소로는 가지 않는다 — 상시 버프라 오버레이만 어지럽히고, StatConvert 라 버프 이득
    /// 모델(rDPS)에 넣으면 기여가 틀린다.</summary>
    [Fact]
    public void Entry_buffs_are_kept_out_of_the_buff_store()
    {
        RecordingData data = Feed(BuffApply(Uid, 19_996_401, 0xFFFFFFFF));

        Assert.Empty(data.StoredBuffs);
    }

    /// <summary>가로채기가 다른 버프를 건드리지 않는다 — 일반 직업 버프는 종전대로 저장소로 간다.</summary>
    [Fact]
    public void Ordinary_job_buffs_still_reach_the_buff_store()
    {
        RecordingData data = Feed(BuffApply(Uid, 130_000_021, 5000));

        Assert.Empty(data.SoloBuffs);
        Assert.Equal([130_000_021], data.StoredBuffs);
    }
}

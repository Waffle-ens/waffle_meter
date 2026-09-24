using System.Text;
using WaffleMeter.Capture;
using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>
/// 시련 어픽스의 <b>배선</b>을 고정한다: 실제 모양의 0x9702 방 스냅샷 바이트 → <see cref="StreamProcessor"/> →
/// 디코더 → 진짜 <see cref="DataManager"/> → <see cref="TrialDifficultyTracker"/>.
/// <para>왜 별도 테스트가 필요한가: 불의 신전(v3.2.5)은 트래커 테스트가 전부 초록이었는데 실전 런은 102/102
/// 시련 블록 없이 올라갔다. 트래커 테스트는 레벨을 <b>직접</b> 넣었고, 그 앞의 디코더가 불의 신전 ID(37~52)를
/// "축*10+레벨" 로 풀어 방을 통째로 버리고 있었다. 계층을 각각 검증하면 "와이어 값이 트래커까지 가는가"는
/// 아무도 확인하지 않는다.</para>
/// </summary>
public sealed class TrialRoomAffixEndToEndTests
{
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

    private static void WriteU32(List<byte> to, long value)
    {
        for (int i = 0; i < 4; i++)
        {
            to.Add((byte)((value >> (8 * i)) & 0xFF));
        }
    }

    /// <summary><c>[02 97][roomKey u32][descLen u8][desc][_limit_member u8][_dungeon_id u32]</c> … 멤버 배열 …
    /// <c>[count u8][affix i32 LE × 4][_reason u8]</c> — RoomAffixTailTests 와 같은 실측 모양.</summary>
    private static byte[] RoomSnapshot(int dungeonId, int roomKey, int[] affixIds)
    {
        var body = new List<byte> { 0x02, 0x97 };
        WriteU32(body, roomKey);
        byte[] d = Encoding.UTF8.GetBytes("시련 갑니다");
        body.Add((byte)d.Length);
        body.AddRange(d);
        body.Add(5);                 // _limit_member
        WriteU32(body, dungeonId);
        body.Add((byte)affixIds.Length);
        foreach (int a in affixIds)
        {
            WriteU32(body, a);
        }

        body.Add(10);                // _reason

        var frame = new List<byte> { (byte)(body.Count + 3) };
        frame.AddRange(body);
        return frame.ToArray();
    }

    private static TrialDifficulty Feed(int dungeonId, int[] affixIds)
    {
        var dm = new DataManager();
        new StreamProcessor(new NullSink(), dm).OnPacketReceived(RoomSnapshot(dungeonId, 384095, affixIds), 0);
        return dm.TrialDifficulty.Current;
    }

    [Fact]
    public void A_fire_temple_sixteen_room_reaches_the_tracker_as_sixteen()
    {
        // Timelimit_2 3=49 · Rebirthlimit_2 3=52 · BossBuff_2 8=44 · Pc_debuff_1 2=46
        TrialDifficulty trial = Feed(TrialDungeonAxes.FireTempleMapId, [49, 52, 44, 46]);

        Assert.True(trial.IsTrial);
        Assert.Equal(16, trial.Level);
        Assert.Equal(8, trial.BossBuff);
        Assert.True(trial.IsTop16Difficulty);
        Assert.Equal("시련 16단계", trial.Label);
    }

    [Fact]
    public void A_mid_fire_temple_room_is_a_point_value_not_a_range()
    {
        // 2+1+5+1 = 9. 보스 강화 5는 바크론 공식으로는 존재할 수 없는 값이다.
        TrialDifficulty trial = Feed(TrialDungeonAxes.FireTempleMapId, [48, 50, 41, 45]);

        Assert.Equal(9, trial.Level);
        Assert.False(trial.IsTopDifficulty);
    }

    [Fact]
    public void A_bakron_sixteen_room_still_reaches_the_tracker_as_sixteen()
    {
        TrialDifficulty trial = Feed(TrialDungeonAxes.BakronIslandMapId, [4, 14, 24, 34]);

        Assert.Equal(16, trial.Level);
        Assert.True(trial.IsTop16Difficulty);
    }
}

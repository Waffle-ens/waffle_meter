using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>
/// 버프 오버레이가 캐릭터 교체·재접속 직후 첫 전투에서 비어 있다가 뒤늦게 뜨는 문제. 본인 로드 패킷(0x3633)은
/// 단발이라 재접속 버스트에서 수십 초까지 늦어질 수 있는데(실측 +246 s), 그동안 executor가 미확정(owner==0)/옛
/// 캐릭(stale)이라 본인 자버프가 uid==owner 게이트에서 통째로 드롭됐다. 이제 그런 버프를 엔티티 uid별로 스테이징해
/// 두었다가, SaveExecutorId가 바로 그 uid를 본인으로 확정하는 순간 오버레이에 재생한다(신원 권위는 그대로라
/// 확정된 uid의 버프만 복원 = 오귀속 없음).
/// </summary>
public sealed class StagedSelfBuffReplayTests
{
    private const int BuffCode = 118000071;      // 실 지속 자버프 (base 11800000)
    private const int OtherJobBuff = 153100301;  // 마도성 집중의 기원 (base 15310000)

    private static DataManager AtClock(long now)
    {
        var dm = new DataManager();
        dm.Clock = () => now;
        return dm;
    }

    [Fact]
    public void Self_buff_applied_before_recognition_is_replayed_when_the_executor_is_confirmed()
    {
        long now = 1_000_000;
        DataManager dm = AtClock(now);

        // executor 미확정(0). 본인 엔티티 uid 42로 자버프 도착 → 종전엔 통째 드롭.
        dm.SaveUseBuff(42, BuffCode, now, now + 30_000, 30_000, actorId: 42);
        Assert.Empty(dm.ActiveOwnerBuffs(now));

        // 늦게 온 0x3633이 uid 42를 본인으로 확정 → 스테이징된 버프가 즉시 복원.
        dm.SaveNickname(42, "본인", isExecutor: true, server: 3, jobByte: 0);
        OwnerBuffView b = Assert.Single(dm.ActiveOwnerBuffs(now));
        Assert.Equal(11800000, b.Code);
    }

    [Fact]
    public void An_expired_staged_buff_is_not_replayed()
    {
        long now = 1_000_000;
        long clock = now;
        var dm = new DataManager { Clock = () => clock };

        dm.SaveUseBuff(42, BuffCode, now, now + 5_000, 5_000, actorId: 42);
        clock = now + 6_000; // 스테이징된 버프가 이미 만료된 뒤에 본인 확정
        dm.SaveNickname(42, "본인", isExecutor: true, server: 3, jobByte: 0);

        Assert.Empty(dm.ActiveOwnerBuffs(clock));
    }

    [Fact]
    public void Character_switch_clears_the_old_buffs_and_replays_only_the_new_characters_staged_buffs()
    {
        long now = 1_000_000;
        DataManager dm = AtClock(now);

        // 옛 캐릭 확정 + 자버프 하나(즉시 저장).
        dm.SaveNickname(10, "옛캐릭", isExecutor: true, server: 3, jobByte: 0);
        dm.SaveUseBuff(10, BuffCode, now, now + 30_000, 30_000, actorId: 10);
        Assert.Single(dm.ActiveOwnerBuffs(now));

        // 새 캐릭 자버프가 새 uid로 도착(executor는 아직 옛 캐릭 → 드롭 + 스테이징).
        dm.SaveUseBuff(20, OtherJobBuff, now, now + 30_000, 30_000, actorId: 20);

        // 새 캐릭 0x3633 → 교체 감지: 옛 버프 클리어 + 새 스테이징만 재생.
        dm.SaveNickname(20, "새캐릭", isExecutor: true, server: 3, jobByte: 0);
        OwnerBuffView b = Assert.Single(dm.ActiveOwnerBuffs(now));
        Assert.Equal(15310000, b.Code); // 옛 11800000 아님
    }

    [Fact]
    public void A_uid_taken_over_by_another_player_does_not_replay_its_staged_buffs_as_self()
    {
        long now = 1_000_000;
        DataManager dm = AtClock(now);

        // uid 42에 자버프 스테이징(미인식).
        dm.SaveUseBuff(42, BuffCode, now, now + 30_000, 30_000, actorId: 42);
        // 그 uid가 다른 플레이어에게 넘어감(닉 변경 = takeover) → 스테이징 무효화.
        dm.SaveNickname(42, "남", isExecutor: false, server: 3, jobByte: 0);
        dm.SaveNickname(42, "남2", isExecutor: false, server: 3, jobByte: 0);

        // 이제 uid 42가 본인으로 확정돼도 남의 버프가 재생되면 안 된다.
        dm.SaveNickname(42, "본인", isExecutor: true, server: 3, jobByte: 0);
        Assert.Empty(dm.ActiveOwnerBuffs(now));
    }

    [Fact]
    public void A_party_members_buff_is_never_replayed_as_self()
    {
        long now = 1_000_000;
        DataManager dm = AtClock(now);

        // 본인 확정.
        dm.SaveNickname(10, "본인", isExecutor: true, server: 3, jobByte: 0);
        // 파티원(uid 20)이 자기 버프를 받음 → 본인 오버레이엔 안 뜸(그리고 스테이징돼도 20은 executor가 안 됨).
        dm.SaveUseBuff(20, OtherJobBuff, now, now + 30_000, 30_000, actorId: 20);
        Assert.Empty(dm.ActiveOwnerBuffs(now));

        // 설령 uid 20이 (다른 캐릭 재접속으로) 본인이 되더라도, 그건 실제 본인 버프였으므로 재생은 정상이다 —
        // 여기서 핵심은 본인이 uid 10으로 확정돼 있는 동안 20의 버프가 본인 것으로 새지 않는다는 점이다.
        Assert.Empty(dm.ActiveOwnerBuffs(now));
    }
}

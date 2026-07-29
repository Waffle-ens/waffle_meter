using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>
/// DataManager.SaveNickname의 executor 승격 2차 방어선. 1차 게이트는 파서
/// (<c>StreamProcessor.SearchOwnNickname</c>, 커버리지는 <c>OwnNicknameValidationTests</c>)에 있지만,
/// executor 승격은 되돌리기가 비싼 부작용을 줄줄이 단다 — 파티 로스터·오드·슈고열쇠·버프 초기화,
/// 통계 신원 교체, 동의 모달. 파서가 뚫려도 신원 저장소만은 오염되지 않아야 한다.
/// <para>2026-07-30 실측: 오프셋을 잘못 잡은 varint가 uid 106900을 만들었고(엔티티 id 공간은 16383까지),
/// 그 uid가 <c>"I"</c>/server 47200으로 본인 자리를 차지했다.</para>
/// </summary>
public sealed class ExecutorPromotionUidGuardTests
{
    [Fact]
    public void Executor_promotion_with_an_out_of_range_uid_is_ignored()
    {
        var dm = new DataManager();
        dm.SaveNickname(9549, "콘팡", isExecutor: true, server: 2003, jobByte: 16);

        dm.SaveNickname(106900, "I", isExecutor: true, server: 47200, jobByte: 12);

        Assert.Equal(9549, dm.ExecutorId());        // 본인은 그대로
        Assert.Null(dm.User(106900));               // 쓰레기 uid는 저장소에 들어가지도 않는다
        Assert.Equal("콘팡", dm.User(9549)!.Nickname);
    }

    [Fact]
    public void Executor_promotion_with_a_zero_or_negative_uid_is_ignored()
    {
        var dm = new DataManager();
        dm.SaveNickname(9549, "콘팡", isExecutor: true, server: 2003, jobByte: 16);

        dm.SaveNickname(0, "X", isExecutor: true, server: 2003, jobByte: 0);
        dm.SaveNickname(-7, "Y", isExecutor: true, server: 2003, jobByte: 0);

        Assert.Equal(9549, dm.ExecutorId());
    }

    /// <summary>상한 바로 아래는 통과해야 한다(코퍼스 실측 최대 정상 본인 uid = 15510).</summary>
    [Fact]
    public void Executor_promotion_just_below_the_cap_still_works()
    {
        var dm = new DataManager();

        dm.SaveNickname(16383, "콘팡", isExecutor: true, server: 2003, jobByte: 16);

        Assert.Equal(16383, dm.ExecutorId());
        Assert.True(dm.User(16383)!.IsExecutor);
    }

    /// <summary>게이트는 executor 승격에만 건다. 타인 닉네임(0x3645)은 uid 공간이 다르게 보일 수 있고
    /// executor를 건드리지 않으므로 종전 동작 그대로 저장된다.</summary>
    [Fact]
    public void Non_executor_nicknames_are_unaffected_by_the_guard()
    {
        var dm = new DataManager();

        dm.SaveNickname(106900, "낯선사람", isExecutor: false, server: 2003, jobByte: 8);

        Assert.Equal("낯선사람", dm.User(106900)!.Nickname);
        Assert.Equal(0, dm.ExecutorId());
    }
}

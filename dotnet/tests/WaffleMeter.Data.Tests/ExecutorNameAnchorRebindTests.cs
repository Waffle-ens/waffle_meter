using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>
/// 이름 앵커 재발급: 존 이동·난입으로 본인의 엔티티 id가 바뀌었는데 본인 로드 패킷(0x3633)이 다시 오지 않는
/// 경우, 아무 플레이어 메타데이터 패킷이든 그 (닉네임, 서버)가 현재 본인과 정확히 일치하면 본인을 그 새 uid로
/// 다시 묶는다. 추정이 아니라 신원 완전일치이므로 낯선 사람을 본인으로 둔갑시키지 않는다.
/// </summary>
public sealed class ExecutorNameAnchorRebindTests
{
    private const string Me = "와플";
    private const int MyServer = 2003;

    private static DataManager WithSelf(int uid)
    {
        var dm = new DataManager();
        dm.SaveNickname(uid, Me, isExecutor: true, server: MyServer, jobByte: 0);
        return dm;
    }

    [Fact]
    public void Same_identity_on_a_new_uid_rebinds_self()
    {
        DataManager dm = WithSelf(100);
        Assert.Equal(100, dm.ExecutorId());

        // 존 이동 후 같은 캐릭터가 새 엔티티 id로 등장 — isExecutor 없이 평범한 메타데이터 패킷으로만.
        dm.SaveNickname(200, Me, isExecutor: false, server: MyServer, jobByte: 0);

        Assert.Equal(200, dm.ExecutorId());
    }

    [Fact]
    public void A_stranger_never_becomes_self()
    {
        DataManager dm = WithSelf(100);

        dm.SaveNickname(200, "남", isExecutor: false, server: MyServer, jobByte: 0);

        Assert.Equal(100, dm.ExecutorId());
    }

    [Fact]
    public void A_namesake_on_another_server_is_not_taken_as_self()
    {
        DataManager dm = WithSelf(100);

        dm.SaveNickname(200, Me, isExecutor: false, server: MyServer + 1, jobByte: 0);

        Assert.Equal(100, dm.ExecutorId());
    }

    [Fact]
    public void An_unknown_server_on_either_side_still_rebinds()
    {
        // 잘린 0x3633은 Server를 남기지 않는다 — 모를 때는 서버 비교를 하지 않아야 재발급이 막히지 않는다.
        var dm = new DataManager();
        dm.SaveNickname(100, Me, isExecutor: true, server: 0, jobByte: 0);

        dm.SaveNickname(200, Me, isExecutor: false, server: MyServer, jobByte: 0);

        Assert.Equal(200, dm.ExecutorId());
    }

    [Fact]
    public void Self_is_never_invented_when_no_executor_is_known_yet()
    {
        var dm = new DataManager();

        dm.SaveNickname(200, Me, isExecutor: false, server: MyServer, jobByte: 0);

        Assert.Equal(0, dm.ExecutorId()); // 앵커가 없으면 아무도 본인이 되지 않는다
    }

    [Fact]
    public void Repeated_metadata_for_the_current_self_is_a_no_op()
    {
        DataManager dm = WithSelf(100);

        dm.SaveNickname(100, Me, isExecutor: false, server: MyServer, jobByte: 0);

        Assert.Equal(100, dm.ExecutorId());
    }
}

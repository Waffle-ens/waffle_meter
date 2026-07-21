using WaffleMeter.Capture;
using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>
/// 이름 앵커: 존 이동·난입으로 본인의 엔티티 id가 바뀌었는데 본인 로드 패킷(0x3633)이 다시 오지 않으면, 본인
/// 딜이 통째로 신원 미상이 된다. 0x9200 멤버 프로필은 (엔티티 uid, 닉네임, 서버)를 한 레코드에 싣는 유일한
/// 브로드캐스트이므로, 그 (닉네임, 서버)가 현재 본인과 <b>완전일치</b>할 때 그 uid를 본인 후보로 적재한다.
/// <para>핵심은 <b>즉시 승격하지 않는다</b>는 것이다 — 실측상 본인 레코드의 21%가 그 세션에서 한 번도 등장하지
/// 않는 uid를 가리키고, 그런 uid로 executor를 옮기면 본인 기능이 통째로 죽는다. "그 uid가 실제로 딜했다"는
/// 증거를 본 뒤에만 승격한다.</para>
/// <para>종전 구현은 <c>SaveNickname(isExecutor: false)</c>에 매달려 있었는데, 그 분기의 유일한 실제 호출자인
/// 0x3645가 본인 닉네임을 절대 싣지 않아 <b>한 번도 실행되지 않았다</b>. 그래서 이 테스트들은 프로덕션이 실제로
/// 타는 진입점(<see cref="DataManager.TryBindExecutorByIdentity"/> + <see cref="DataManager.SaveDamage"/>)만
/// 구동한다 — 그러지 않으면 죽은 코드를 상대로 초록불이 켜진다.</para>
/// </summary>
public sealed class ExecutorNameAnchorRebindTests
{
    private const string Me = "와플";
    private const int MyServer = 2003;

    private static long _now;

    private static DataManager WithSelf(int uid, int server = MyServer)
    {
        _now = 1_000_000;
        var dm = new DataManager { Clock = () => _now };
        dm.SaveNickname(uid, Me, isExecutor: true, server: server, jobByte: 0);
        return dm;
    }

    /// <summary>그 uid로 데미지가 지나가게 한다 — 승격의 유일한 트리거.</summary>
    private static void Damage(DataManager dm, int actorId) =>
        dm.SaveDamage(
            new ParsedDamagePacket { ActorId = actorId, TargetId = 9999, SkillCode = 1, Damage = 100 },
            dm.CurrentEpoch());

    [Fact]
    public void A_matching_identity_on_a_new_uid_does_not_move_the_executor_by_itself()
    {
        DataManager dm = WithSelf(100);

        dm.TryBindExecutorByIdentity(200, Me, MyServer);

        Assert.Equal(100, dm.ExecutorId()); // 아직 후보일 뿐이다
    }

    [Fact]
    public void The_staged_anchor_is_promoted_once_that_uid_actually_damages()
    {
        DataManager dm = WithSelf(100);
        dm.TryBindExecutorByIdentity(200, Me, MyServer);

        Damage(dm, 200);

        Assert.Equal(200, dm.ExecutorId());
        Assert.Equal(Me, dm.User(200)?.Nickname); // 딜만 넣던 무명 uid에 신원이 채워진다
        Assert.True(dm.User(200)?.IsExecutor);
        Assert.False(dm.User(100)?.IsExecutor);
    }

    [Fact]
    public void Being_the_target_of_damage_is_also_liveness_evidence()
    {
        // 실측: 0x9200이 그 uid의 마지막 '타격'보다 늦게 오는 경우가 흔하고(4건 중 3건), 그중 한 건은 피격
        // 프레임으로만 아직 살아 있음이 드러났다. 맞는 것도 "그 엔티티가 지금 이 전투에 있다"는 증거다.
        DataManager dm = WithSelf(100);
        dm.TryBindExecutorByIdentity(200, Me, MyServer);

        dm.SaveDamage(
            new ParsedDamagePacket { ActorId = 555, TargetId = 200, SkillCode = 1, Damage = 100 },
            dm.CurrentEpoch());

        Assert.Equal(200, dm.ExecutorId());
    }

    [Fact]
    public void A_uid_that_never_damages_is_never_promoted()
    {
        // 실측 21%: 0x9200이 그 세션에서 한 번도 싸우지 않는 uid를 실어 온다. 즉시 승격했다면 여기서 본인이
        // 유령 uid로 옮겨가 본인 행·자기색·버프·업로드가 전부 죽는다.
        DataManager dm = WithSelf(100);
        dm.TryBindExecutorByIdentity(200, Me, MyServer);

        Damage(dm, 300); // 다른 누군가가 싸운다

        Assert.Equal(100, dm.ExecutorId());
    }

    [Fact]
    public void A_stale_anchor_expires_instead_of_firing_much_later()
    {
        // 수명을 짧게 잡는 이유는 엔티티 id 재사용이다 — 오래 들고 있을수록 그 id가 다른 플레이어에게
        // 재발급될 창이 커진다.
        DataManager dm = WithSelf(100);
        dm.TryBindExecutorByIdentity(200, Me, MyServer);

        _now += 90 * 1000L + 1;
        Damage(dm, 200);

        Assert.Equal(100, dm.ExecutorId());
    }

    [Fact]
    public void A_uid_that_another_player_has_taken_over_is_never_promoted()
    {
        // 스테이징 이후 그 엔티티 id가 다른 플레이어에게 재발급됐다. 승격 직전에 다시 확인하지 않으면
        // executor가 남을 가리키고, 그대로 통계 신원까지 남의 것으로 올라간다.
        DataManager dm = WithSelf(100);
        dm.TryBindExecutorByIdentity(200, Me, MyServer);

        dm.SaveNickname(200, "권자르", isExecutor: false, server: MyServer, jobByte: 0);
        Damage(dm, 200);

        Assert.Equal(100, dm.ExecutorId());
    }

    [Fact]
    public void A_uid_revealed_as_a_mob_after_staging_is_never_promoted()
    {
        // 몹은 상시 피격되므로 '데미지 증거'가 자동으로 충족된다 — 승격 시점의 재검사가 유일한 방어선이다.
        DataManager dm = WithSelf(100);
        dm.TryBindExecutorByIdentity(200, Me, MyServer);

        dm.SaveMobId(200, 2301059);
        dm.SaveDamage(
            new ParsedDamagePacket { ActorId = 555, TargetId = 200, SkillCode = 1, Damage = 100 },
            dm.CurrentEpoch());

        Assert.Equal(100, dm.ExecutorId());
    }

    [Fact]
    public void A_stranger_never_becomes_self()
    {
        DataManager dm = WithSelf(100);

        dm.TryBindExecutorByIdentity(200, "남", MyServer);
        Damage(dm, 200);

        Assert.Equal(100, dm.ExecutorId());
    }

    [Fact]
    public void A_namesake_on_another_server_is_not_taken_as_self()
    {
        DataManager dm = WithSelf(100);

        dm.TryBindExecutorByIdentity(200, Me, MyServer + 1);
        Damage(dm, 200);

        Assert.Equal(100, dm.ExecutorId());
    }

    [Fact]
    public void An_unknown_server_blocks_the_bind_rather_than_guessing()
    {
        // fail-closed. 서버를 모를 때 통과시키면 타 서버 동명이인이 본인으로 승격되고, 그 뒤로는 아무 증상
        // 없이 남의 캐릭터 신원으로 통계가 올라간다. 실측상 본인 로드 489건이 전부 서버를 싣고 오므로
        // (서버 미상 0건) 막아서 잃는 것이 없다.
        DataManager dm = WithSelf(100, server: -1);

        dm.TryBindExecutorByIdentity(200, Me, MyServer);
        Damage(dm, 200);

        Assert.Equal(100, dm.ExecutorId());
    }

    [Fact]
    public void A_record_without_a_server_is_not_a_binding_candidate()
    {
        DataManager dm = WithSelf(100);

        dm.TryBindExecutorByIdentity(200, Me, server: -1);
        Damage(dm, 200);

        Assert.Equal(100, dm.ExecutorId());
    }

    [Fact]
    public void Self_is_never_invented_when_no_executor_is_known_yet()
    {
        _now = 1_000_000;
        var dm = new DataManager { Clock = () => _now };

        dm.TryBindExecutorByIdentity(200, Me, MyServer);
        Damage(dm, 200);

        Assert.Equal(0, dm.ExecutorId()); // 앵커가 없으면 아무도 본인이 되지 않는다
    }

    [Fact]
    public void A_uid_outside_the_entity_id_space_is_rejected()
    {
        // 오프셋 오독의 지문. 실측 엔티티 uid 최댓값이 16383이고 초과 사례가 0이다.
        DataManager dm = WithSelf(100);

        dm.TryBindExecutorByIdentity(16384, Me, MyServer);
        Damage(dm, 16384);

        Assert.Equal(100, dm.ExecutorId());
    }

    [Fact]
    public void A_known_mob_instance_is_rejected()
    {
        DataManager dm = WithSelf(100);
        dm.SaveMobId(200, 2301059);

        dm.TryBindExecutorByIdentity(200, Me, MyServer);
        Damage(dm, 200);

        Assert.Equal(100, dm.ExecutorId());
    }

    [Fact]
    public void A_known_summon_is_rejected()
    {
        DataManager dm = WithSelf(100);
        dm.SaveSummon(200, 100);

        dm.TryBindExecutorByIdentity(200, Me, MyServer);
        Damage(dm, 200);

        Assert.Equal(100, dm.ExecutorId());
    }

    [Fact]
    public void A_late_own_load_packet_wins_and_voids_the_stale_candidate()
    {
        // 후보를 적재한 뒤 진짜 0x3633이 또 다른 uid로 도착하면, 그 다음 앵커 승격이 옛 후보를 되살리면 안 된다.
        DataManager dm = WithSelf(100);
        dm.TryBindExecutorByIdentity(200, Me, MyServer);

        dm.SaveNickname(300, Me, isExecutor: true, server: MyServer, jobByte: 0);
        Damage(dm, 200);

        Assert.Equal(300, dm.ExecutorId());
    }

    [Fact]
    public void A_character_switch_voids_the_candidate()
    {
        DataManager dm = WithSelf(100);
        dm.TryBindExecutorByIdentity(200, Me, MyServer);

        dm.SaveNickname(300, "다른캐릭", isExecutor: true, server: MyServer, jobByte: 0);
        Damage(dm, 200);

        Assert.Equal(300, dm.ExecutorId());
    }

    [Fact]
    public void Promotion_is_a_re_instance_not_a_character_switch()
    {
        // 이름·서버가 같으므로 캐릭터 교체가 아니다 — 파티 프리뷰(0x9702) 같은 직전 상태를 날리면 안 된다.
        DataManager dm = WithSelf(100);
        dm.SavePartyRoster(new List<(string, int, int)> { (Me, MyServer, 1), ("친구", MyServer, 2) });

        dm.TryBindExecutorByIdentity(200, Me, MyServer);
        Damage(dm, 200);

        Assert.Equal(200, dm.ExecutorId());
        Assert.Equal(2, dm.PartyRosterIdentities(300_000).Count);
    }
}

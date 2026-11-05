using WaffleMeter.Capture;
using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>
/// 소환 매핑은 <b>타격 시각</b>으로 판정한다. 전투가 끝나면 계산기가 그 전투의 패킷 전체를 다시 해석하므로, "지금
/// 매핑이 있나"로 판정하면 소환수가 죽는 순간 그 소환수가 이미 넣은 피해까지 저장·업로드에서 사라진다. 실제 로그에서
/// 치유성 −2.3%, 마도성 −10.3%·−43% 로 드러난 회귀다.
/// <para>그래서 여기 테스트는 라이브 값만 보지 않는다. 라이브만 단언하면 이 회귀가 전부 통과한다 — 실제로 그랬다.
/// <see cref="Finish"/> 가 전투를 닫고 저장된 기록을 돌려준다.</para>
/// </summary>
public sealed class SummonMappingTimeTests
{
    private const int Instance = 100, BossCode = 2301722, Server = 2003;
    private const int Me = 5001, Sorcerer = 5002, Cleric = 5003, Stranger = 5004, Unknown = 5005, Elementalist = 5006, Pet = 18066;
    private const int FlameBolt = 15210000, WallOfFire = 15390000, ClericHit = 17060000, ClericSummonSkill = 17153450;
    private const int ColdShock = 16010000, SpiritStrike = 16400010;
    private const int ShieldFrame = 12320000;       // 네자칸의 방패: 군중전에서 소환수 actor 로 플레이어 대상에 찍힌다
    private const int GrantedProc = 11340028;       // 흡혈의 검 착취: 수혜자가 actor 로 찍힌다
    private const int PetCode = 2920874, OtherCode = 2930001;
    private const int SorcererJobByte = 25, ClericJobByte = 29;
    private const long T0 = 1_000_000;

    private static (DataManager Dm, DpsCalculator Calc, long[] Now, List<DpsLog> Saved) Setup()
    {
        long[] now = { T0 };
        var dm = new DataManager { Clock = () => now[0] };
        dm.LoadMobs(new Dictionary<int, Mob> { [BossCode] = new Mob(BossCode, "그리오사", Boss: true) });
        dm.LoadSkills([new Skill(SpiritStrike, "불의 정령: 화염 타격")]);
        dm.SaveMobId(Instance, BossCode);
        dm.SaveNickname(Me, "나", isExecutor: true, server: Server, jobByte: SorcererJobByte);
        dm.SaveNickname(Sorcerer, "마도", isExecutor: false, server: Server, jobByte: SorcererJobByte);
        dm.SaveNickname(Cleric, "힐러", isExecutor: false, server: Server, jobByte: ClericJobByte);
        dm.SavePartyRoster([("나", Server, 1), ("마도", Server, 2), ("힐러", Server, 3)]);
        dm.MobHp(Instance, 100_000);
        dm.StartBattle(Instance);

        var calc = new DpsCalculator(dm);
        var saved = new List<DpsLog>();
        calc.OnBattleLogged = saved.Add;
        return (dm, calc, now, saved);
    }

    private static void Hit(DataManager dm, int actor, long ts, int dmg, int skill, bool dot = false, int target = Instance) =>
        dm.SaveDamage(
            new ParsedDamagePacket { ActorId = actor, TargetId = target, Damage = dmg, Timestamp = ts, SkillCode = skill, Dot = dot },
            dm.CurrentEpoch());

    private static double Amount(DpsReport r, int uid) =>
        r.Information.TryGetValue(uid, out DpsInformation? i) ? i.Amount : 0d;

    /// <summary>전투를 닫고(전체 재누적이 돈다) 저장된 기록을 돌려준다.</summary>
    private static DpsReport Finish(DataManager dm, DpsCalculator calc, List<DpsLog> saved)
    {
        calc.GetDps();
        dm.EndBattle(Instance);
        calc.GetDps();
        return Assert.Single(saved).Report;
    }

    [Fact]
    public void A_summon_that_died_mid_fight_keeps_its_damage_through_the_battle_end_recount()
    {
        (DataManager dm, DpsCalculator calc, long[] now, List<DpsLog> saved) = Setup();
        dm.SaveSummon(Pet, Cleric);
        Hit(dm, Me, T0 + 100, 5000, FlameBolt);
        Hit(dm, Cleric, T0 + 110, 1000, ClericHit);
        Hit(dm, Pet, T0 + 200, 700, ClericSummonSkill);
        Hit(dm, Pet, T0 + 800, 700, ClericSummonSkill);
        double live = Amount(calc.GetDps(), Cleric);
        Assert.Equal(2400d, live);

        now[0] = T0 + 900;
        dm.SaveEntityDeath(Pet, now[0]);

        Assert.Equal(live, Amount(Finish(dm, calc, saved), Cleric)); // 회귀: 1000 — 소환수 몫이 소급해서 사라졌다
    }

    [Fact]
    public void An_expired_wall_keeps_its_damage_on_the_sorcerer_who_cast_it()
    {
        // 불의 장벽은 약 20초 뒤 자연 만료될 때도 0x8D04 를 받는다(실측 127개 중 81개).
        (DataManager dm, DpsCalculator calc, long[] now, List<DpsLog> saved) = Setup();
        dm.SaveSummon(Pet, Sorcerer);
        Hit(dm, Sorcerer, T0 + 100, 3000, FlameBolt);
        for (int i = 1; i <= 5; i++)
        {
            Hit(dm, Pet, T0 + 100 + i * 1000, 500, WallOfFire, dot: true);
        }

        calc.GetDps();
        now[0] = T0 + 6_000;
        dm.SaveEntityDeath(Pet, now[0]);
        Hit(dm, Pet, now[0] + 90, 500, WallOfFire, dot: true); // 사망 패킷 뒤에 온 마지막 틱

        Assert.Equal(6000d, Amount(Finish(dm, calc, saved), Sorcerer));
    }

    [Theory]
    [InlineData(500, true)]  // 도착 순서가 뒤집힌 마지막 직격
    [InlineData(501, false)] // 그 너머의 직격은 재사용된 id 의 새 엔티티다
    public void A_direct_hit_after_the_death_is_the_old_summon_only_within_the_grace(int afterMs, bool folds)
    {
        (DataManager dm, DpsCalculator calc, long[] now, List<DpsLog> saved) = Setup();
        dm.SaveSummon(Pet, Cleric);
        Hit(dm, Cleric, T0 + 100, 1000, ClericHit);
        now[0] = T0 + 1_000;
        dm.SaveEntityDeath(Pet, now[0]);
        Hit(dm, Pet, now[0] + afterMs, 700, ClericSummonSkill);

        Assert.Equal(folds ? 1700d : 1000d, Amount(Finish(dm, calc, saved), Cleric));
    }

    [Theory]
    [InlineData(15_000, true)]  // 죽기 전에 걸어 둔 DoT
    [InlineData(15_001, false)] // 그 너머에는 id 재사용이 섞인다
    public void A_dot_tick_after_the_death_stays_with_the_owner_only_within_the_tail(int afterMs, bool folds)
    {
        (DataManager dm, DpsCalculator calc, long[] now, List<DpsLog> saved) = Setup();
        dm.SaveSummon(Pet, Cleric);
        Hit(dm, Cleric, T0 + 100, 1000, ClericHit);
        now[0] = T0 + 1_000;
        dm.SaveEntityDeath(Pet, now[0]);
        Hit(dm, Pet, now[0] + afterMs, 700, ClericSummonSkill, dot: true);

        Assert.Equal(folds ? 1700d : 1000d, Amount(Finish(dm, calc, saved), Cleric));
    }

    [Fact]
    public void A_dot_tick_of_another_class_after_the_death_is_not_the_dead_summons()
    {
        (DataManager dm, DpsCalculator calc, long[] now, List<DpsLog> saved) = Setup();
        dm.SaveSummon(Pet, Cleric);
        Hit(dm, Cleric, T0 + 100, 1000, ClericHit);
        now[0] = T0 + 1_000;
        dm.SaveEntityDeath(Pet, now[0]);
        Hit(dm, Pet, now[0] + 2_000, 700, WallOfFire, dot: true); // 재사용된 id 위의 불의 장벽

        Assert.Equal(1000d, Amount(Finish(dm, calc, saved), Cleric));
    }

    [Fact]
    public void A_spirit_dot_on_a_dead_pets_id_goes_to_the_lone_elementalist()
    {
        // 다른 직업 DoT 는 죽은 소환수의 것이 아니다. 매핑이 끝난 것으로 보므로 이름 접두 규칙이 판단한다.
        (DataManager dm, DpsCalculator calc, long[] now, List<DpsLog> saved) = Setup();
        dm.SaveNickname(Elementalist, "정령", isExecutor: false, server: Server, jobByte: 21);
        dm.SavePartyRoster([("나", Server, 1), ("마도", Server, 2), ("힐러", Server, 3), ("정령", Server, 4)]);
        dm.SaveMobId(Pet, PetCode);
        dm.SaveSummon(Pet, Cleric);
        Hit(dm, Cleric, T0 + 100, 1000, ClericHit);
        Hit(dm, Elementalist, T0 + 110, 1000, ColdShock);
        now[0] = T0 + 1_000;
        dm.SaveEntityDeath(Pet, now[0]);
        Hit(dm, Pet, now[0] + 2_000, 700, SpiritStrike, dot: true);

        DpsReport report = Finish(dm, calc, saved);
        Assert.Equal(1700d, Amount(report, Elementalist));
        Assert.Equal(1000d, Amount(report, Cleric));
    }

    [Fact]
    public void Class_history_keeps_counting_after_the_death()
    {
        (DataManager dm, DpsCalculator calc, long[] now, List<DpsLog> saved) = Setup();
        dm.SaveNickname(Unknown, "미상", isExecutor: false, server: Server, jobByte: 0);
        dm.SavePartyRoster([("나", Server, 1), ("미상", Server, 2)]);
        dm.SaveMobId(Pet, PetCode);
        dm.SaveSummon(Pet, Unknown);
        Hit(dm, Me, T0 + 100, 5000, FlameBolt);
        Hit(dm, Pet, T0 + 200, 700, ClericSummonSkill);
        now[0] = T0 + 1_000;
        dm.SaveEntityDeath(Pet, now[0]);
        Hit(dm, Pet, now[0] + 2_000, 900, WallOfFire, dot: true);

        Assert.True(dm.IsSummonContradicted(Pet, now[0] + 2_000));
        Assert.Equal(700d, Amount(Finish(dm, calc, saved), Unknown));
    }

    [Fact]
    public void A_late_hit_of_a_dead_pet_without_a_mob_id_leaves_no_row()
    {
        // 코드 마커 없는 0x3641 은 몹 id 를 남기지 않는다. 죽은 항목이 소환수로 답해야 임시 행이 철회된다.
        (DataManager dm, DpsCalculator calc, long[] now, List<DpsLog> saved) = Setup();
        dm.SaveSummon(Pet, Cleric);
        Hit(dm, Cleric, T0 + 100, 1000, ClericHit);
        now[0] = T0 + 1_000;
        dm.SaveEntityDeath(Pet, now[0]);
        Hit(dm, Pet, now[0] + 600, 700, ClericSummonSkill);

        DpsReport report = Finish(dm, calc, saved);
        Assert.Equal(1000d, Amount(report, Cleric));
        Assert.DoesNotContain(report.Contributors, u => u.Id == Pet);
    }

    [Fact]
    public void A_second_death_does_not_move_the_stamp()
    {
        (DataManager dm, DpsCalculator calc, long[] now, List<DpsLog> saved) = Setup();
        dm.SaveMobId(Pet, PetCode);
        dm.SaveSummon(Pet, Cleric);
        Hit(dm, Cleric, T0 + 100, 1000, ClericHit);
        now[0] = T0 + 1_000;
        dm.SaveEntityDeath(Pet, now[0]);
        Hit(dm, Pet, T0 + 3_000, 700, ClericSummonSkill);
        now[0] = T0 + 5_000;
        dm.SaveEntityDeath(Pet, now[0]);

        Assert.Equal(1000d, Amount(Finish(dm, calc, saved), Cleric));
    }

    [Fact]
    public void A_repeated_nickname_does_not_move_the_end()
    {
        // 플레이어 신원 패킷은 반복된다. 끝난 시각이 뒤로 밀리면 그 사이 타격이 옛 주인에게 접힌다.
        (DataManager dm, DpsCalculator calc, long[] now, List<DpsLog> saved) = Setup();
        dm.SaveSummon(Pet, Cleric);
        Hit(dm, Cleric, T0 + 100, 1000, ClericHit);
        now[0] = T0 + 1_000;
        dm.SaveNickname(Pet, "남", isExecutor: false, server: Server, jobByte: ClericJobByte);
        Hit(dm, Pet, T0 + 1_100, 900, ClericHit);
        now[0] = T0 + 5_000;
        dm.SaveNickname(Pet, "남", isExecutor: false, server: Server, jobByte: ClericJobByte);

        DpsReport report = Finish(dm, calc, saved);
        Assert.Equal(1000d, Amount(report, Cleric));
        Assert.Equal(900d, Amount(report, Pet));
    }

    [Fact]
    public void A_respawn_under_another_mob_code_keeps_the_earlier_hits_and_refuses_the_later_ones()
    {
        (DataManager dm, DpsCalculator calc, long[] now, List<DpsLog> saved) = Setup();
        dm.SaveMobId(Pet, PetCode);
        dm.SaveSummon(Pet, Cleric);
        Hit(dm, Cleric, T0 + 100, 1000, ClericHit);
        Hit(dm, Pet, T0 + 200, 700, ClericSummonSkill);
        calc.GetDps();

        now[0] = T0 + 1_000;
        dm.SaveMobId(Pet, OtherCode);
        Hit(dm, Pet, now[0] + 100, 900, ClericSummonSkill);

        DpsReport report = Finish(dm, calc, saved);
        Assert.Null(dm.SummonerId(Pet));
        Assert.Equal(1700d, Amount(report, Cleric));
        Assert.DoesNotContain(report.Contributors, u => u.Id == Pet);
    }

    [Fact]
    public void A_nickname_on_a_spawned_id_still_reads_as_a_mob_for_later_hits()
    {
        // 현재 동작을 그대로 적어 둔다. 닉네임은 소환 매핑만 끝내고 몹 id 는 남긴다. 실측상 플레이어 uid(≤16362)와
        // 몹 id(≥16387)가 겹치지 않아 실제 트래픽에는 없는 모양이다.
        (DataManager dm, DpsCalculator calc, long[] now, List<DpsLog> saved) = Setup();
        dm.SaveMobId(Pet, PetCode);
        dm.SaveSummon(Pet, Cleric);
        Hit(dm, Cleric, T0 + 100, 1000, ClericHit);
        Hit(dm, Pet, T0 + 200, 700, ClericSummonSkill);
        calc.GetDps();

        now[0] = T0 + 1_000;
        dm.SaveNickname(Pet, "남", isExecutor: false, server: Server, jobByte: SorcererJobByte);
        Hit(dm, Pet, now[0] + 100, 900, FlameBolt);

        DpsReport report = Finish(dm, calc, saved);
        Assert.Equal(1700d, Amount(report, Cleric));
        Assert.Equal(0d, Amount(report, Pet));
    }

    [Fact]
    public void A_nickname_landing_on_the_id_splits_the_hits_at_that_moment()
    {
        // 몹 id 가 없는 id 에서 매핑이 끝나는 규칙만 본다(바로 위 테스트 참조).
        (DataManager dm, DpsCalculator calc, long[] now, List<DpsLog> saved) = Setup();
        dm.SaveSummon(Pet, Cleric);
        Hit(dm, Cleric, T0 + 100, 1000, ClericHit);
        Hit(dm, Pet, T0 + 200, 700, ClericSummonSkill);
        calc.GetDps();

        now[0] = T0 + 1_000;
        dm.SaveNickname(Pet, "남", isExecutor: false, server: Server, jobByte: SorcererJobByte);
        Hit(dm, Pet, now[0] + 100, 900, FlameBolt);

        DpsReport report = Finish(dm, calc, saved);
        Assert.Equal(1700d, Amount(report, Cleric));
        Assert.Equal(900d, Amount(report, Pet));
    }

    [Fact]
    public void Another_class_code_on_a_player_target_does_not_look_like_a_recycled_id()
    {
        // 실측: 군중전에서 매핑된 소환수의 다른 직업 코드 1,162건 중 1,154건은 대상이 플레이어였다. 이걸 직업 이력에
        // 넣으면 살아 있는 장벽이 재사용 id 로 오판돼 그 뒤 피해가 전부 빠진다.
        (DataManager dm, DpsCalculator calc, _, List<DpsLog> saved) = Setup();
        dm.SaveNickname(Stranger, "남", isExecutor: false, server: Server, jobByte: 0);
        dm.SaveSummon(Pet, Sorcerer);
        Hit(dm, Sorcerer, T0 + 100, 3000, FlameBolt);
        Hit(dm, Pet, T0 + 200, 500, ShieldFrame, dot: true, target: Stranger);
        Hit(dm, Pet, T0 + 300, 500, WallOfFire, dot: true);
        Hit(dm, Pet, T0 + 400, 500, ShieldFrame, dot: true, target: Stranger);
        Hit(dm, Pet, T0 + 500, 500, WallOfFire, dot: true);

        Assert.False(dm.IsSummonContradicted(Pet, T0 + 500));
        Assert.Equal(4000d, Amount(Finish(dm, calc, saved), Sorcerer));
    }

    [Fact]
    public void Player_target_frames_do_not_poison_an_unknown_owners_summon()
    {
        (DataManager dm, DpsCalculator calc, _, List<DpsLog> saved) = Setup();
        dm.SaveNickname(Stranger, "남", isExecutor: false, server: Server, jobByte: 0);
        dm.SaveNickname(Unknown, "미상", isExecutor: false, server: Server, jobByte: 0);
        dm.SavePartyRoster([("나", Server, 1), ("미상", Server, 2)]);
        dm.SaveMobId(Pet, PetCode);
        dm.SaveSummon(Pet, Unknown);
        Hit(dm, Me, T0 + 100, 5000, FlameBolt);
        Hit(dm, Pet, T0 + 200, 500, ShieldFrame, dot: true, target: Stranger);
        Hit(dm, Pet, T0 + 300, 500, WallOfFire, dot: true);

        Assert.Equal(500d, Amount(Finish(dm, calc, saved), Unknown));
    }

    [Fact]
    public void A_granted_proc_does_not_poison_an_unknown_owners_summon()
    {
        (DataManager dm, DpsCalculator calc, _, List<DpsLog> saved) = Setup();
        dm.SaveNickname(Unknown, "미상", isExecutor: false, server: Server, jobByte: 0);
        dm.SavePartyRoster([("나", Server, 1), ("미상", Server, 2)]);
        dm.SaveMobId(Pet, PetCode);
        dm.SaveSummon(Pet, Unknown);
        Hit(dm, Me, T0 + 100, 5000, FlameBolt);
        Hit(dm, Pet, T0 + 200, 300, GrantedProc);
        Hit(dm, Pet, T0 + 300, 700, ClericSummonSkill);

        Assert.Equal(1000d, Amount(Finish(dm, calc, saved), Unknown));
    }

    [Fact]
    public void Another_class_code_on_a_mob_still_marks_the_id_as_recycled_when_the_owner_job_is_unknown()
    {
        (DataManager dm, DpsCalculator calc, _, List<DpsLog> saved) = Setup();
        dm.SaveNickname(Unknown, "미상", isExecutor: false, server: Server, jobByte: 0);
        dm.SavePartyRoster([("나", Server, 1), ("미상", Server, 2)]);
        dm.SaveSummon(Pet, Unknown);
        Hit(dm, Me, T0 + 100, 5000, FlameBolt);
        Hit(dm, Pet, T0 + 200, 700, ClericSummonSkill);
        Hit(dm, Pet, T0 + 300, 900, WallOfFire, dot: true);
        Hit(dm, Pet, T0 + 400, 900, WallOfFire, dot: true);

        Assert.True(dm.IsSummonContradicted(Pet, T0 + 300));
        Assert.False(dm.IsSummonContradicted(Pet, T0 + 200));
        Assert.Equal(700d, Amount(Finish(dm, calc, saved), Unknown));
    }

    [Fact]
    public void A_granted_proc_does_not_lock_the_recipients_job()
    {
        // 수혜자가 actor 로 찍히는 흡혈의 검이 직업을 잠그면, 등급 규칙 때문에 본인 스킬로도 교정되지 않고
        // 진짜 소환수 피해까지 "직업 불일치"로 빠진다.
        (DataManager dm, DpsCalculator calc, _, List<DpsLog> saved) = Setup();
        dm.SaveSummon(Pet, Cleric);
        Hit(dm, Cleric, T0 + 100, 300, GrantedProc);
        Hit(dm, Cleric, T0 + 150, 1000, ClericHit);
        Hit(dm, Pet, T0 + 200, 700, ClericSummonSkill);

        DpsReport report = Finish(dm, calc, saved);
        Assert.Equal(JobClass.CLERIC, dm.User(Cleric)!.Job);
        Assert.Equal(2000d, Amount(report, Cleric));
    }

    [Fact]
    public void A_granted_proc_from_a_summon_neither_drops_nor_contradicts()
    {
        (DataManager dm, DpsCalculator calc, _, List<DpsLog> saved) = Setup();
        dm.SaveSummon(Pet, Cleric);
        Hit(dm, Cleric, T0 + 100, 1000, ClericHit);
        Hit(dm, Pet, T0 + 200, 700, ClericSummonSkill);
        Hit(dm, Pet, T0 + 300, 300, GrantedProc);
        Hit(dm, Pet, T0 + 400, 700, ClericSummonSkill);

        Assert.False(dm.IsSummonContradicted(Pet, T0 + 400));
        Assert.Equal(2700d, Amount(Finish(dm, calc, saved), Cleric));
    }

    [Fact]
    public void A_fold_in_a_late_saved_battle_stays_on_the_self_of_that_battle()
    {
        // 존 이동으로 본인 uid 가 바뀐 뒤 전투가 저장돼도, 재누적이 소환수 타격을 새 uid 로 보내 같은 닉네임 행을
        // 둘 만들면 안 된다.
        const int SelfAfterReload = 5100;
        (DataManager dm, DpsCalculator calc, long[] now, List<DpsLog> saved) = Setup();
        dm.SaveSummon(Pet, Me);
        Hit(dm, Pet, T0 + 50, 3000, FlameBolt, dot: true);
        Hit(dm, Me, T0 + 100, 9000, FlameBolt);
        calc.GetDps();

        now[0] = T0 + 5_000;
        dm.SaveNickname(SelfAfterReload, "나", isExecutor: true, server: Server, jobByte: SorcererJobByte);

        DpsReport report = Finish(dm, calc, saved);
        Assert.Equal(12000d, Amount(report, Me));
        Assert.DoesNotContain(report.Contributors, u => u.Id == SelfAfterReload);
        Assert.Equal(Me, report.ExecutorId);
    }

    [Fact]
    public void Resolve_summon_owner_follows_the_same_rules_as_the_damage_path()
    {
        (DataManager dm, _, long[] now, _) = Setup();

        // 스폰보다 먼저 때려 생긴 임시 레코드는 플레이어 증거가 아니다.
        dm.EnsureUser(Pet);
        dm.SaveSummon(Pet, Cleric);
        Assert.Equal(Cleric, dm.ResolveSummonOwner(Pet));

        // 죽어도 그 소환수가 남긴 버프·시전의 주인은 그대로다.
        dm.SaveEntityDeath(Pet, now[0]);
        Assert.Equal(Cleric, dm.ResolveSummonOwner(Pet));

        // 매핑보다 나중에 닉네임을 받으면 그 id 는 이제 플레이어다.
        dm.SaveNickname(Pet, "남", isExecutor: false, server: Server, jobByte: SorcererJobByte);
        Assert.Equal(Pet, dm.ResolveSummonOwner(Pet));

        // 본인은 절대 소환수가 아니다.
        dm.SaveSummon(Me, Cleric);
        Assert.Equal(Me, dm.ResolveSummonOwner(Me));

        // 모르는 id 는 받은 그대로다.
        Assert.Equal(7777, dm.ResolveSummonOwner(7777));
    }

    [Fact]
    public void A_stale_roster_lists_nobody()
    {
        (DataManager dm, _, long[] now, _) = Setup();
        dm.SavePartyRoster([("정령", Server, 4), ("옛정령", Server, 5)]);
        dm.SavePartyRosterJobPower([("정령", Server, 21, 1), ("옛정령", Server, 21, 1)]);
        now[0] = T0 + 3_600_000;

        Assert.Empty(dm.CurrentRosterJobCodes(60_000));
    }

    [Fact]
    public void Summon_mapping_snapshots_survive_new_spawns_from_the_parser_thread()
    {
        // 행 클릭의 시전 조회는 UI 스레드에서 돈다. 파서가 그동안 새 키를 꽂아도 열거가 깨지면 안 된다.
        var repository = new SummonRepository();
        for (int i = 0; i < 4_000; i++)
        {
            repository.Save(i, 1);
        }

        Exception? failure = null;
        var done = new ManualResetEventSlim(false);
        var parser = new Thread(() =>
        {
            int id = 4_000;
            while (!done.IsSet)
            {
                repository.Save(id++, 1);
                if (id >= 8_000)
                {
                    repository.Flush();
                    id = 0;
                }
            }
        }) { IsBackground = true };

        parser.Start();
        try
        {
            for (int round = 0; round < 3_000; round++)
            {
                repository.Mappings();
                repository.GetAll();
            }
        }
        catch (Exception e)
        {
            failure = e;
        }
        finally
        {
            done.Set();
        }

        parser.Join();
        Assert.Null(failure);
    }

    [Fact]
    public void Current_roster_job_codes_only_lists_who_is_in_the_roster_now()
    {
        (DataManager dm, _, _, _) = Setup();
        dm.SavePartyRoster([("나", Server, 1), ("옛정령", Server, 2)], partyId: 111);
        dm.SavePartyRosterJobPower([("나", Server, 25, 1), ("옛정령", Server, 21, 1)]);
        dm.SavePartyRoster([("나", Server, 1), ("새힐러", Server, 2)], partyId: 222);
        dm.SavePartyRosterJobPower([("나", Server, 25, 1), ("새힐러", Server, 29, 1)]);

        var current = dm.CurrentRosterJobCodes(60_000);

        Assert.DoesNotContain(current, m => m.Nickname == "옛정령");
        Assert.Contains(current, m => m.Nickname == "새힐러" && m.JobCode == 29);
        Assert.Contains(dm.PartyRosterJobPower(60_000), m => m.Nickname == "옛정령"); // 표시용 사전은 그대로
    }
}

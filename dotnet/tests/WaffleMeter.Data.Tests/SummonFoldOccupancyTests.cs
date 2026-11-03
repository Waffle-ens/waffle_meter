using WaffleMeter.Capture;
using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>
/// Entity-id occupancy rules for the summon fold (DpsCalculator.ResolveActor), found by an exhaustive sweep of
/// the attribution paths after the 불의 장벽 report. The summon→owner map outlives the summon and entity ids are
/// recycled, so "mapped" must mean "mapped more recently than any player identity on that uid":
///  - a nickname observed AFTER a mapping drops the mapping (DataManager.SaveNickname) — the original hardening
///    (a stale mapping must never steal a real player's skill) kept;
///  - a mapping that survives folds even when a RETIRED player record or a provisional nickname-less record sits
///    on the uid (previously both blocked the fold and the summon's damage was dropped as a mob);
///  - the fold lands on the NEWEST uid of the owner's identity (a re-instanced owner no longer gets a second row);
///  - a summon mapped to an owner outside this fight is dropped instead of falling through to the lone-정령성
///    name-prefix rule (which credited strangers' spirits to our 정령성 at shared field bosses);
///  - the current executor uid is never folded.
/// </summary>
public sealed class SummonFoldOccupancyTests
{
    private const int Instance = 100;
    private const int BossCode = 2301722;
    private const int Server = 2003;
    private const int Me = 5001;             // executor, 마도성
    private const int FlameBolt = 15210000;  // 마도성
    private const int ClericHit = 17060000;  // 치유성 own skill
    private const int ClericSummonSkill = 17153450;
    private const int PetCode = 2920874;

    private static (DataManager Dm, DpsCalculator Calc) Boss()
    {
        var dm = new DataManager { Clock = () => 1_000_000 };
        dm.LoadMobs(new Dictionary<int, Mob> { [BossCode] = new Mob(BossCode, "그리오사", Boss: true) });
        dm.SaveMobId(Instance, BossCode);
        dm.SaveNickname(Me, "나", isExecutor: true, server: Server, jobByte: 25);
        return (dm, new DpsCalculator(dm));
    }

    private static void Open(DataManager dm)
    {
        dm.MobHp(Instance, 100_000);
        dm.StartBattle(Instance);
    }

    private static void Hit(DataManager dm, int actor, long ts, int dmg, int skill) =>
        dm.SaveDamage(
            new ParsedDamagePacket { ActorId = actor, TargetId = Instance, Damage = dmg, Timestamp = ts, SkillCode = skill },
            dm.CurrentEpoch());

    private static double Amount(DpsReport r, int uid) =>
        r.Information.TryGetValue(uid, out DpsInformation? i) ? i.Amount : 0d;

    [Fact]
    public void Summon_that_raced_its_spawn_folds_its_post_reveal_damage_into_the_owner()
    {
        (DataManager dm, DpsCalculator calc) = Boss();
        const int Cleric = 5002, SummonId = 18066;
        dm.SaveNickname(Cleric, "힐러", isExecutor: false, server: Server, jobByte: 29);
        Open(dm);

        Hit(dm, Me, 1_001_000, 5000, FlameBolt);
        Hit(dm, Cleric, 1_001_010, 1000, ClericHit);
        Hit(dm, SummonId, 1_001_100, 900, ClericSummonSkill); // before its spawn → provisional phantom row
        Assert.Contains(calc.GetDps().Contributors, u => u.Id == SummonId);

        dm.SaveMobId(SummonId, PetCode);
        dm.SaveSummon(SummonId, Cleric);                      // the reveal
        Assert.DoesNotContain(calc.GetDps().Contributors, u => u.Id == SummonId);

        Hit(dm, SummonId, 1_002_000, 900, ClericSummonSkill);
        Hit(dm, SummonId, 1_002_100, 900, ClericSummonSkill);
        DpsReport report = calc.GetDps();

        // The pre-reveal 900 was retracted with the phantom (by design); everything after the reveal is the
        // owner's. Previously the provisional record left by EnsureUser blocked the fold and both hits were lost.
        Assert.Equal(1000d + 900 + 900, Amount(report, Cleric));
        Assert.DoesNotContain(report.Contributors, u => u.Id == SummonId);
    }

    [Fact]
    public void Retired_player_uid_recycled_to_a_summon_folds_into_the_summons_owner()
    {
        (DataManager dm, DpsCalculator calc) = Boss();
        const int Cleric = 5002, RetiredUid = 6000;
        dm.SaveNickname(RetiredUid, "옛날사람", isExecutor: false, server: Server, jobByte: 13); // seen in an earlier scene
        dm.SaveNickname(Cleric, "힐러", isExecutor: false, server: Server, jobByte: 29);
        Open(dm);

        dm.SaveMobId(RetiredUid, PetCode);   // the game reissues that id as the cleric's summon
        dm.SaveSummon(RetiredUid, Cleric);
        Hit(dm, Me, 1_001_000, 5000, FlameBolt);
        Hit(dm, Cleric, 1_001_010, 1000, ClericHit);
        Hit(dm, RetiredUid, 1_001_100, 900, ClericSummonSkill);
        DpsReport report = calc.GetDps();

        Assert.Equal(1900d, Amount(report, Cleric));
        Assert.DoesNotContain(report.Contributors, u => u.Id == RetiredUid);
        Assert.Equal(JobClass.RANGER, dm.User(RetiredUid)!.Job); // the retired record is left alone
    }

    [Fact]
    public void Player_nickname_observed_after_a_stale_mapping_keeps_the_players_own_damage()
    {
        // The original hardening: a summon id recycled to a REAL player (nickname arrives after the mapping) must
        // never have that player's skill folded into the old owner.
        (DataManager dm, DpsCalculator calc) = Boss();
        const int Cleric = 5002, ReusedUid = 18066;
        dm.SaveNickname(Cleric, "힐러", isExecutor: false, server: Server, jobByte: 29);
        dm.SaveSummon(ReusedUid, Cleric);                                                   // old summon
        dm.SaveNickname(ReusedUid, "검성", isExecutor: false, server: Server, jobByte: 5); // id now a 검성 player
        Open(dm);

        Hit(dm, Me, 1_001_000, 5000, FlameBolt);
        Hit(dm, Cleric, 1_001_010, 1000, ClericHit);
        Hit(dm, ReusedUid, 1_001_100, 4000, 11020000); // 검성 own skill
        DpsReport report = calc.GetDps();

        Assert.Null(dm.SummonerId(ReusedUid));
        Assert.Equal(1000d, Amount(report, Cleric));
        Assert.Equal(4000d, Amount(report, ReusedUid));
        Assert.Equal(JobClass.GLADIATOR, report.Contributors.Single(u => u.Id == ReusedUid).Job);
    }

    [Fact]
    public void Executor_uid_is_never_folded_even_when_mapped()
    {
        (DataManager dm, DpsCalculator calc) = Boss();
        const int Cleric = 5002;
        dm.SaveNickname(Cleric, "힐러", isExecutor: false, server: Server, jobByte: 29);
        dm.SaveSummon(Me, Cleric); // a mis-parse / recycle landing on the live executor uid
        Open(dm);

        Hit(dm, Cleric, 1_001_010, 1000, ClericHit);
        Hit(dm, Me, 1_001_100, 5000, FlameBolt);
        DpsReport report = calc.GetDps();

        Assert.Equal(5000d, Amount(report, Me));
        Assert.Equal(1000d, Amount(report, Cleric));
    }

    [Fact]
    public void Fold_lands_on_the_live_uid_of_a_re_instanced_owner()
    {
        (DataManager dm, DpsCalculator calc) = Boss();
        const int OldOwnerUid = 6000, NewOwnerUid = 6001, Summon = 18066;
        dm.SaveNickname(OldOwnerUid, "힐러", isExecutor: false, server: Server, jobByte: 29);
        dm.SaveSummon(Summon, OldOwnerUid);
        dm.SaveNickname(NewOwnerUid, "힐러", isExecutor: false, server: Server, jobByte: 29); // zone reload: new uid
        dm.SavePartyRoster([("나", Server, 1), ("힐러", Server, 2)]);
        Open(dm);

        Hit(dm, Me, 1_001_000, 5000, FlameBolt);
        Hit(dm, NewOwnerUid, 1_001_010, 1000, ClericHit);
        Hit(dm, Summon, 1_001_100, 900, ClericSummonSkill);
        DpsReport report = calc.GetDps();

        Assert.Equal(1, report.Contributors.Count(u => u.Nickname == "힐러")); // previously two 힐러 rows
        Assert.Equal(1900d, Amount(report, NewOwnerUid));
        Assert.DoesNotContain(report.Contributors, u => u.Id == OldOwnerUid);
    }

    [Fact]
    public void Strangers_mapped_spirit_is_dropped_not_credited_to_our_lone_elementalist()
    {
        (DataManager dm, DpsCalculator calc) = Boss();
        const int Ours = 5002, Stranger = 7001, StrangerSpirit = 18066;
        dm.SaveNickname(Ours, "우리정령성", isExecutor: false, server: Server, jobByte: 21);
        dm.SaveNickname(Stranger, "타인정령성", isExecutor: false, server: Server, jobByte: 21); // not in this fight
        dm.SaveSummon(StrangerSpirit, Stranger);
        dm.LoadSkills([new Skill(16400010, "불의 정령: 화염 타격")]);
        Open(dm);

        Hit(dm, Me, 1_001_000, 5000, FlameBolt);
        Hit(dm, Ours, 1_001_050, 3000, 16010000);
        Hit(dm, StrangerSpirit, 1_001_100, 900, 16400010);
        DpsReport report = calc.GetDps();

        Assert.Equal(3000d, Amount(report, Ours)); // previously 3900 via the name-prefix fallback
        Assert.DoesNotContain(report.Contributors, u => u.Id == StrangerSpirit);
        Assert.DoesNotContain(report.Contributors, u => u.Id == Stranger);
    }

    [Fact]
    public void Unmapped_spirit_skill_still_goes_to_the_lone_elementalist()
    {
        // The name-prefix rule itself is unchanged for a spirit with NO mapping at all.
        (DataManager dm, DpsCalculator calc) = Boss();
        const int Ours = 5002, Spirit = 18066;
        dm.SaveNickname(Ours, "우리정령성", isExecutor: false, server: Server, jobByte: 21);
        dm.LoadSkills([new Skill(16400010, "불의 정령: 화염 타격")]);
        Open(dm);

        Hit(dm, Me, 1_001_000, 5000, FlameBolt);
        Hit(dm, Ours, 1_001_050, 3000, 16010000);
        Hit(dm, Spirit, 1_001_100, 900, 16400010);

        Assert.Equal(3900d, Amount(calc.GetDps(), Ours));
    }
}

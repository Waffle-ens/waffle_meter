using WaffleMeter.Capture;
using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>
/// Covers the "마도성 스킬이 정령에 잡힌다" report: a 마도성's 불의 장벽 (a summoned fire-wall object that deals
/// damage under its OWN entity id with the job-locked 15390000 code) was shown in a 정령성 party member's
/// breakdown. Root cause: the summon→owner map persists across battles and entity ids are recycled, so the
/// wall landed on an id that still mapped to the 정령성's earlier spirit. ResolveActor folded the wall's
/// damage into that stale owner without checking that the owner's class could even cast the skill (and the
/// fold guard keeps the job inference from correcting the owner's icon, so the row kept its 정령성 icon).
///
/// Two layers:
///  - DataManager: a 0x3641 spawn that registers a recycled instance id under a DIFFERENT mob code drops the
///    stale summon→owner mapping (the old entity is gone).
///  - DpsCalculator: a summon fold is rejected when the owner's known class cannot cast the job-locked skill;
///    the hit is re-homed to the lone contributor of the skill's class, or dropped when that is ambiguous.
/// </summary>
public sealed class StaleSummonMapAttributionTests
{
    private const int Instance = 100;
    private const int BossCode = 2301722;   // 금기의 마수 그리오사
    private const int Sorcerer = 5001;       // 마도성 (executor)
    private const int Elementalist = 5002;   // 정령성 party member
    private const int RecycledId = 18066;    // was the 정령성's spirit in an earlier fight; now the 불의 장벽 object
    private const int WallOfFire = 15390000; // 마도성 불의 장벽 (job-locked 15xx)
    private const int FlameBolt = 15210000;  // 마도성 불꽃 화살
    private const int ColdShock = 16010000;  // 정령성 냉기 충격
    private const int SorcererJobByte = 25;
    private const int ElementalistJobByte = 21;
    private const int Server = 2003;

    private static (DataManager Dm, DpsCalculator Calc) Boss()
    {
        var dm = new DataManager { Clock = () => 1_000_000 };
        dm.LoadMobs(new Dictionary<int, Mob> { [BossCode] = new Mob(BossCode, "그리오사", Boss: true) });
        dm.SaveMobId(Instance, BossCode);
        dm.SaveNickname(Sorcerer, "마도", isExecutor: true, server: Server, jobByte: SorcererJobByte);
        dm.SaveNickname(Elementalist, "펭모", isExecutor: false, server: Server, jobByte: ElementalistJobByte);
        // Stale mapping left over from an earlier fight: the 정령성's spirit used entity id 18066.
        dm.SaveSummon(RecycledId, Elementalist);
        dm.MobHp(Instance, 100_000);
        dm.StartBattle(Instance);
        return (dm, new DpsCalculator(dm));
    }

    private static void Hit(DataManager dm, int actor, long ts, int dmg, int skill) =>
        dm.SaveDamage(
            new ParsedDamagePacket { ActorId = actor, TargetId = Instance, Damage = dmg, Timestamp = ts, SkillCode = skill },
            dm.CurrentEpoch());

    private static double Amount(DpsReport r, int uid) =>
        r.Information.TryGetValue(uid, out DpsInformation? i) ? i.Amount : 0d;

    [Fact]
    public void Wall_of_fire_on_a_recycled_spirit_id_goes_to_the_lone_sorcerer_not_the_stale_owner()
    {
        (DataManager dm, DpsCalculator calc) = Boss();

        Hit(dm, Sorcerer, 1_001_000, 5000, FlameBolt);
        Hit(dm, Elementalist, 1_001_050, 3000, ColdShock);
        Hit(dm, RecycledId, 1_001_100, 900, WallOfFire);
        Hit(dm, RecycledId, 1_001_200, 900, WallOfFire);

        DpsReport report = calc.GetDps();

        Assert.Equal(6800d, Amount(report, Sorcerer));     // 5000 own + 2 × 900 wall
        Assert.Equal(3000d, Amount(report, Elementalist)); // untouched by the 마도성 skill
        Assert.DoesNotContain(report.Contributors, u => u.Id == RecycledId);
        Assert.Equal(JobClass.ELEMENTALIST, report.Contributors.Single(u => u.Id == Elementalist).Job);
        Assert.Equal(JobClass.SORCERER, report.Contributors.Single(u => u.Id == Sorcerer).Job);
    }

    [Fact]
    public void Stale_owner_that_dealt_no_damage_is_not_surfaced_by_the_wall()
    {
        // The reported shape: the 정령성 row existed ONLY because of the folded 불의 장벽 (it was a fresh party
        // member with no damage of its own).
        (DataManager dm, DpsCalculator calc) = Boss();
        dm.SavePartyRoster([("마도", Server, 1), ("펭모", Server, 2)]);

        Hit(dm, Sorcerer, 1_001_000, 5000, FlameBolt);
        Hit(dm, RecycledId, 1_001_100, 900, WallOfFire);

        DpsReport report = calc.GetDps();

        Assert.Equal(5900d, Amount(report, Sorcerer));
        Assert.DoesNotContain(report.Contributors, u => u.Id == Elementalist);
        Assert.DoesNotContain(report.Contributors, u => u.Id == RecycledId);
    }

    [Fact]
    public void Wall_of_fire_is_dropped_when_the_sorcerer_is_ambiguous()
    {
        const int SecondSorcerer = 5003;
        (DataManager dm, DpsCalculator calc) = Boss();
        dm.SaveNickname(SecondSorcerer, "마도2", isExecutor: false, server: Server, jobByte: SorcererJobByte);

        Hit(dm, Sorcerer, 1_001_000, 5000, FlameBolt);
        Hit(dm, SecondSorcerer, 1_001_010, 4000, FlameBolt);
        Hit(dm, Elementalist, 1_001_050, 3000, ColdShock);
        Hit(dm, RecycledId, 1_001_100, 900, WallOfFire);

        DpsReport report = calc.GetDps();

        Assert.Equal(5000d, Amount(report, Sorcerer));
        Assert.Equal(4000d, Amount(report, SecondSorcerer));
        Assert.Equal(3000d, Amount(report, Elementalist)); // never credited with a 마도성 skill
        Assert.DoesNotContain(report.Contributors, u => u.Id == RecycledId); // and no phantom row either
    }

    [Fact]
    public void Summon_whose_owner_can_cast_the_skill_still_folds_into_the_owner()
    {
        // Control: a genuine fold (a 치유성 summon casting a 17xx skill into its 치유성 owner) is unchanged.
        const int Cleric = 5004;
        const int ClericSummon = 18070;
        const int ClericSkill = 17153450;
        (DataManager dm, DpsCalculator calc) = Boss();
        dm.SaveNickname(Cleric, "힐러", isExecutor: false, server: Server, jobByte: 29);
        dm.SaveSummon(ClericSummon, Cleric);

        Hit(dm, Sorcerer, 1_001_000, 5000, FlameBolt);
        Hit(dm, Cleric, 1_001_050, 1000, 17060000);
        Hit(dm, ClericSummon, 1_001_100, 700, ClericSkill);

        DpsReport report = calc.GetDps();

        Assert.Equal(1700d, Amount(report, Cleric));
        Assert.DoesNotContain(report.Contributors, u => u.Id == ClericSummon);
    }

    [Fact]
    public void Owner_with_unknown_job_still_receives_the_fold()
    {
        // A summon owner whose class is not yet known cannot be contradicted, so the fold stays.
        const int Unknown = 5005;
        const int Summon = 18071;
        (DataManager dm, DpsCalculator calc) = Boss();
        dm.SaveNickname(Unknown, "미상", isExecutor: false, server: Server, jobByte: 0);
        dm.SaveSummon(Summon, Unknown);
        dm.SavePartyRoster([("마도", Server, 1), ("미상", Server, 2)]);

        Hit(dm, Sorcerer, 1_001_000, 5000, FlameBolt);
        Hit(dm, Summon, 1_001_100, 700, 17153450);

        DpsReport report = calc.GetDps();

        Assert.Equal(700d, Amount(report, Unknown));
    }

    [Fact]
    public void Respawn_under_a_different_mob_code_drops_the_stale_owner_mapping()
    {
        var dm = new DataManager();
        dm.SaveMobId(RecycledId, 2920874);        // the spirit's spawn
        dm.SaveSummon(RecycledId, Elementalist);
        Assert.Equal(Elementalist, dm.SummonerId(RecycledId));

        dm.SaveMobId(RecycledId, 2920874);        // re-broadcast of the SAME entity keeps its owner
        Assert.Equal(Elementalist, dm.SummonerId(RecycledId));

        dm.SaveMobId(RecycledId, 2930001);        // the id is recycled to a different object (e.g. 불의 장벽)
        Assert.Null(dm.SummonerId(RecycledId));
    }
}

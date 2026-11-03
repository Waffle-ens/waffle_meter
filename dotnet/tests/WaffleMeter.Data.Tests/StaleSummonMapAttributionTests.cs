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
///  - DpsCalculator: a summon fold is rejected (the hit is dropped, not re-homed) when the contradiction is
///    trustworthy: the entity has already cast another class's job-locked skills under this mapping (a summon
///    never changes class — the recycled-id signature), or the owner's job is their OWN un-folded skill's. A
///    mismatch against a merely Authoritative (jobByte / official lookup, both known to mis-read) owner with no
///    class history keeps folding, so a real summon of a mis-labelled owner is never silently dropped — including
///    on later pulls, since the rule is independent of battle clocks.
/// </summary>
public sealed class StaleSummonMapAttributionTests
{
    private const int Instance = 100;
    private const int BossCode = 2301722;   // 금기의 마수 그리오사
    private const int Sorcerer = 5001;       // 마도성 (executor)
    private const int Elementalist = 5002;   // 정령성 party member
    private const int RecycledId = 18066;    // the 정령성's spirit in an earlier fight; later the 불의 장벽 object
    private const int WallOfFire = 15390000; // 마도성 불의 장벽 (job-locked 15xx)
    private const int FlameBolt = 15210000;  // 마도성 불꽃 화살
    private const int ColdShock = 16010000;  // 정령성 냉기 충격
    private const int SpiritStrike = 16400010; // "불의 정령: …" (16xx, job-locked to 정령성)
    private const int ClericSummonSkill = 17153450;
    private const int SorcererJobByte = 25;
    private const int ElementalistJobByte = 21;
    private const int ClericJobByte = 29;
    private const int ChanterJobByte = 33;
    private const int Server = 2003;

    private static (DataManager Dm, DpsCalculator Calc) Setup()
    {
        var dm = new DataManager { Clock = () => 1_000_000 };
        dm.LoadMobs(new Dictionary<int, Mob> { [BossCode] = new Mob(BossCode, "그리오사", Boss: true) });
        dm.LoadSkills([new Skill(SpiritStrike, "불의 정령: 화염 타격")]);
        dm.SaveMobId(Instance, BossCode);
        dm.SaveNickname(Sorcerer, "마도", isExecutor: true, server: Server, jobByte: SorcererJobByte);
        dm.SaveNickname(Elementalist, "펭모", isExecutor: false, server: Server, jobByte: ElementalistJobByte);
        dm.SavePartyRoster([("마도", Server, 1), ("펭모", Server, 2)]);
        return (dm, new DpsCalculator(dm));
    }

    private static void Open(DataManager dm)
    {
        dm.MobHp(Instance, 100_000);
        dm.StartBattle(Instance);
    }

    /// <summary>End the pull with the boss alive (leash/wipe) and let the calculator flush it.</summary>
    private static void Leash(DataManager dm, DpsCalculator calc)
    {
        calc.GetDps();
        dm.EndBattle(Instance);
        calc.GetDps();
    }

    private static void Hit(DataManager dm, int actor, long ts, int dmg, int skill, bool dot = false) =>
        dm.SaveDamage(
            new ParsedDamagePacket { ActorId = actor, TargetId = Instance, Damage = dmg, Timestamp = ts, SkillCode = skill, Dot = dot },
            dm.CurrentEpoch());

    private static double Amount(DpsReport r, int uid) =>
        r.Information.TryGetValue(uid, out DpsInformation? i) ? i.Amount : 0d;

    [Fact]
    public void Wall_of_fire_on_a_recycled_spirit_id_is_not_folded_into_the_stale_owner()
    {
        // Pull 1: the 정령성's spirit (id 18066) is mapped and casts its 16xx spirit skill → folded, as normal.
        (DataManager dm, DpsCalculator calc) = Setup();
        dm.SaveSummon(RecycledId, Elementalist);
        Open(dm);
        Hit(dm, Sorcerer, 1_001_000, 5000, FlameBolt);
        Hit(dm, RecycledId, 1_001_100, 500, SpiritStrike);
        Assert.Equal(500d, Amount(calc.GetDps(), Elementalist));
        Leash(dm, calc);

        // Pull 2: the spirit is gone and id 18066 now belongs to the 마도성's 불의 장벽, whose spawn was lost
        // (or carried no usable owner record), so the stale mapping survived.
        Open(dm);
        Hit(dm, Sorcerer, 2_001_000, 5000, FlameBolt);
        Hit(dm, Elementalist, 2_001_050, 3000, ColdShock);
        Hit(dm, RecycledId, 2_001_100, 900, WallOfFire);
        Hit(dm, RecycledId, 2_001_200, 900, WallOfFire);
        Hit(dm, RecycledId, 2_001_300, 400, WallOfFire, dot: true);

        DpsReport report = calc.GetDps();
        Assert.True(dm.IsSummonContradicted(RecycledId, 2_001_100));
        Assert.False(dm.IsSummonContradicted(RecycledId, 1_001_100)); // the spirit's own earlier hit is unaffected
        Assert.Equal(3000d, Amount(report, Elementalist)); // previously 5200: the wall was folded in
        Assert.Equal(5000d, Amount(report, Sorcerer));     // dropped, not guessed onto the sorcerer
        Assert.DoesNotContain(report.Contributors, u => u.Id == RecycledId);
        Assert.Equal(JobClass.ELEMENTALIST, report.Contributors.Single(u => u.Id == Elementalist).Job);
    }

    [Fact]
    public void Stale_owner_that_dealt_no_damage_is_not_surfaced_by_the_wall()
    {
        // The reported shape: the 정령성 row existed ONLY because of the folded 불의 장벽 (a fresh party member
        // with no damage of its own, so its job is the Authoritative jobByte — not OwnSkill).
        (DataManager dm, DpsCalculator calc) = Setup();
        dm.SaveSummon(RecycledId, Elementalist);
        Open(dm);
        Hit(dm, RecycledId, 1_001_100, 500, SpiritStrike); // the spirit's own earlier hit under this mapping
        Leash(dm, calc);

        Open(dm);
        Hit(dm, Sorcerer, 2_001_000, 5000, FlameBolt);
        Hit(dm, RecycledId, 2_001_100, 900, WallOfFire);

        DpsReport report = calc.GetDps();
        Assert.Equal(5000d, Amount(report, Sorcerer));
        Assert.DoesNotContain(report.Contributors, u => u.Id == Elementalist);
        Assert.DoesNotContain(report.Contributors, u => u.Id == RecycledId);
    }

    [Fact]
    public void Class_change_within_one_pull_is_caught_too()
    {
        (DataManager dm, DpsCalculator calc) = Setup();
        dm.SaveSummon(RecycledId, Elementalist);
        Open(dm);
        Hit(dm, RecycledId, 1_001_000, 500, SpiritStrike);   // spirit
        Hit(dm, RecycledId, 1_001_100, 900, WallOfFire);     // same id, other class: recycled mid-fight
        Hit(dm, RecycledId, 1_001_200, 900, WallOfFire);

        Assert.Equal(500d, Amount(calc.GetDps(), Elementalist));
    }

    [Fact]
    public void A_fresh_spawn_resets_the_class_history()
    {
        (DataManager dm, DpsCalculator calc) = Setup();
        dm.SaveSummon(RecycledId, Elementalist);
        Open(dm);
        Hit(dm, RecycledId, 1_001_000, 500, SpiritStrike);
        Hit(dm, RecycledId, 1_001_100, 900, WallOfFire);     // contradiction
        Assert.Equal(500d, Amount(calc.GetDps(), Elementalist));

        dm.SaveSummon(RecycledId, Sorcerer);                 // the wall's own 0x3641 (with owner) finally lands
        Hit(dm, RecycledId, 1_002_000, 900, WallOfFire);
        DpsReport report = calc.GetDps();
        Assert.False(dm.IsSummonContradicted(RecycledId, 1_002_000));
        Assert.Equal(900d, Amount(report, Sorcerer));
        Assert.Equal(500d, Amount(report, Elementalist));
    }

    [Fact]
    public void Mismatch_against_an_own_skill_job_is_stale_without_any_history()
    {
        // The owner's job came from their OWN un-folded skill (ground truth), so the first contradicting hit is
        // already certain.
        (DataManager dm, DpsCalculator calc) = Setup();
        Open(dm);
        Hit(dm, Elementalist, 1_001_050, 3000, ColdShock);     // OwnSkill → ELEMENTALIST locked
        calc.GetDps();
        dm.SaveSummon(RecycledId, Elementalist);
        Hit(dm, RecycledId, 1_010_000, 900, WallOfFire);

        Assert.Equal(JobProvenance.OwnSkill, dm.User(Elementalist)!.JobSource);
        Assert.Equal(3000d, Amount(calc.GetDps(), Elementalist));
    }

    [Fact]
    public void Mismatch_against_an_authoritative_job_without_history_still_folds_across_pulls()
    {
        // Reviewer finding: a 치유성 whose snapshot jobByte was mis-read as 호법성 (Authoritative) and who never
        // lands an own damage skill is never corrected, and their summon is kept alive across pulls. The
        // mismatch alone must not drop that summon's damage — on the first pull or any later one.
        const int Healer = 5003, HealerSummon = 18070;
        (DataManager dm, DpsCalculator calc) = Setup();
        dm.SaveNickname(Healer, "힐러", isExecutor: false, server: Server, jobByte: ChanterJobByte); // mis-read
        dm.SavePartyRoster([("마도", Server, 1), ("펭모", Server, 2), ("힐러", Server, 3)]);
        dm.SaveSummon(HealerSummon, Healer);

        Open(dm);
        Hit(dm, Sorcerer, 1_001_000, 5000, FlameBolt);
        Hit(dm, HealerSummon, 1_001_100, 700, ClericSummonSkill);
        Assert.Equal(700d, Amount(calc.GetDps(), Healer));
        Leash(dm, calc);

        Open(dm); // pull 2, same summon, mapping older than this battle
        Hit(dm, Sorcerer, 2_001_000, 5000, FlameBolt);
        Hit(dm, HealerSummon, 2_001_100, 700, ClericSummonSkill);
        Hit(dm, HealerSummon, 2_001_200, 700, ClericSummonSkill);

        DpsReport report = calc.GetDps();
        Assert.Equal(JobClass.CHANTER, dm.User(Healer)!.Job);
        Assert.Equal(1400d, Amount(report, Healer));
    }

    [Fact]
    public void Summon_whose_owner_can_cast_the_skill_still_folds_into_the_owner()
    {
        const int Cleric = 5004, ClericSummon = 18070;
        (DataManager dm, DpsCalculator calc) = Setup();
        dm.SaveNickname(Cleric, "힐러", isExecutor: false, server: Server, jobByte: ClericJobByte);
        dm.SaveSummon(ClericSummon, Cleric);
        Open(dm);

        Hit(dm, Sorcerer, 1_001_000, 5000, FlameBolt);
        Hit(dm, Cleric, 1_001_050, 1000, 17060000);
        Hit(dm, ClericSummon, 1_001_100, 700, ClericSummonSkill);

        DpsReport report = calc.GetDps();
        Assert.Equal(1700d, Amount(report, Cleric));
        Assert.DoesNotContain(report.Contributors, u => u.Id == ClericSummon);
    }

    [Fact]
    public void Owner_with_unknown_job_still_receives_the_fold()
    {
        const int Unknown = 5005, Summon = 18071;
        (DataManager dm, DpsCalculator calc) = Setup();
        dm.SaveNickname(Unknown, "미상", isExecutor: false, server: Server, jobByte: 0);
        dm.SaveSummon(Summon, Unknown);
        dm.SavePartyRoster([("마도", Server, 1), ("미상", Server, 2)]);
        Open(dm);

        Hit(dm, Sorcerer, 1_001_000, 5000, FlameBolt);
        Hit(dm, Summon, 1_001_100, 700, ClericSummonSkill);

        Assert.Equal(700d, Amount(calc.GetDps(), Unknown));
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

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
///  - DpsCalculator: a summon fold is rejected (the hit is dropped, not re-homed) when the owner's known class
///    cannot cast the job-locked skill and the contradiction is trustworthy: the owner's job is their own
///    un-folded skill's, or the mapping predates this battle. A mapping made during this fight whose owner is
///    merely mis-labelled (Authoritative jobByte) keeps folding, so a real summon is not silently dropped.
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
    private const int ClericSummonSkill = 17153450;
    private const int SorcererJobByte = 25;
    private const int ElementalistJobByte = 21;
    private const int ClericJobByte = 29;
    private const int ChanterJobByte = 33;
    private const int Server = 2003;
    private const long EarlierFight = 900_000;  // when the stale mapping was made
    private const long ThisFight = 1_000_000;   // battle start

    private sealed class Clock { public long Now = EarlierFight; }

    private static (DataManager Dm, DpsCalculator Calc, Clock Clock) Boss(bool staleMapping = true)
    {
        var clock = new Clock();
        var dm = new DataManager { Clock = () => clock.Now };
        dm.LoadMobs(new Dictionary<int, Mob> { [BossCode] = new Mob(BossCode, "그리오사", Boss: true) });
        dm.SaveMobId(Instance, BossCode);
        dm.SaveNickname(Sorcerer, "마도", isExecutor: true, server: Server, jobByte: SorcererJobByte);
        dm.SaveNickname(Elementalist, "펭모", isExecutor: false, server: Server, jobByte: ElementalistJobByte);
        if (staleMapping)
        {
            // Left over from an earlier fight: the 정령성's spirit used entity id 18066.
            dm.SaveSummon(RecycledId, Elementalist);
        }

        clock.Now = ThisFight;
        dm.MobHp(Instance, 100_000);
        dm.StartBattle(Instance);
        return (dm, new DpsCalculator(dm), clock);
    }

    private static void Hit(DataManager dm, int actor, long ts, int dmg, int skill) =>
        dm.SaveDamage(
            new ParsedDamagePacket { ActorId = actor, TargetId = Instance, Damage = dmg, Timestamp = ts, SkillCode = skill },
            dm.CurrentEpoch());

    private static double Amount(DpsReport r, int uid) =>
        r.Information.TryGetValue(uid, out DpsInformation? i) ? i.Amount : 0d;

    [Fact]
    public void Wall_of_fire_on_a_recycled_spirit_id_is_not_folded_into_the_stale_owner()
    {
        (DataManager dm, DpsCalculator calc, _) = Boss();

        Hit(dm, Sorcerer, 1_001_000, 5000, FlameBolt);
        Hit(dm, Elementalist, 1_001_050, 3000, ColdShock);
        Hit(dm, RecycledId, 1_001_100, 900, WallOfFire);
        Hit(dm, RecycledId, 1_001_200, 900, WallOfFire);

        DpsReport report = calc.GetDps();

        Assert.Equal(3000d, Amount(report, Elementalist)); // previously 4800: the wall was folded in
        Assert.Equal(5000d, Amount(report, Sorcerer));     // dropped, not guessed onto the sorcerer
        Assert.DoesNotContain(report.Contributors, u => u.Id == RecycledId);
        Assert.Equal(JobClass.ELEMENTALIST, report.Contributors.Single(u => u.Id == Elementalist).Job);
    }

    [Fact]
    public void Stale_owner_that_dealt_no_damage_is_not_surfaced_by_the_wall()
    {
        // The reported shape: the 정령성 row existed ONLY because of the folded 불의 장벽 (it was a fresh party
        // member with no damage of its own, so its job is the Authoritative jobByte — not OwnSkill).
        (DataManager dm, DpsCalculator calc, _) = Boss();
        dm.SavePartyRoster([("마도", Server, 1), ("펭모", Server, 2)]);

        Hit(dm, Sorcerer, 1_001_000, 5000, FlameBolt);
        Hit(dm, RecycledId, 1_001_100, 900, WallOfFire);

        DpsReport report = calc.GetDps();

        Assert.Equal(5000d, Amount(report, Sorcerer));
        Assert.DoesNotContain(report.Contributors, u => u.Id == Elementalist);
        Assert.DoesNotContain(report.Contributors, u => u.Id == RecycledId);
    }

    [Fact]
    public void Wall_of_fire_dot_ticks_are_dropped_too()
    {
        (DataManager dm, DpsCalculator calc, _) = Boss();

        Hit(dm, Elementalist, 1_001_050, 3000, ColdShock);
        dm.SaveDamage(
            new ParsedDamagePacket { ActorId = RecycledId, TargetId = Instance, Damage = 400, Timestamp = 1_001_100, SkillCode = WallOfFire, Dot = true },
            dm.CurrentEpoch());

        Assert.Equal(3000d, Amount(calc.GetDps(), Elementalist));
    }

    [Fact]
    public void Mismatch_on_an_own_skill_job_is_stale_even_for_a_mapping_made_this_battle()
    {
        // The owner's job came from their OWN un-folded skill (ground truth), so the contradiction is certain
        // regardless of how fresh the mapping is: the id was recycled mid-fight.
        (DataManager dm, DpsCalculator calc, Clock clock) = Boss(staleMapping: false);

        Hit(dm, Elementalist, 1_001_050, 3000, ColdShock);      // OwnSkill → ELEMENTALIST locked
        calc.GetDps();
        clock.Now = ThisFight + 5_000;
        dm.SaveSummon(RecycledId, Elementalist);                // the spirit (re)summoned during this fight
        Hit(dm, RecycledId, 1_010_000, 900, WallOfFire);        // ...but the id now carries a 마도성 wall

        Assert.Equal(JobProvenance.OwnSkill, dm.User(Elementalist)!.JobSource);
        Assert.Equal(3000d, Amount(calc.GetDps(), Elementalist));
    }

    [Fact]
    public void Mismatch_on_an_authoritative_job_with_a_mapping_made_this_battle_still_folds()
    {
        // Reviewer finding: a 치유성 whose snapshot jobByte was mis-read as 호법성 (Authoritative) and who never
        // lands an own damage skill is never corrected. Their summon, mapped DURING this fight, casts 17xx —
        // the mismatch must NOT drop a real summon's damage; the old fold is kept for this shape.
        const int Healer = 5003, HealerSummon = 18070;
        (DataManager dm, DpsCalculator calc, Clock clock) = Boss(staleMapping: false);
        dm.SaveNickname(Healer, "힐러", isExecutor: false, server: Server, jobByte: ChanterJobByte); // mis-read
        dm.SavePartyRoster([("마도", Server, 1), ("펭모", Server, 2), ("힐러", Server, 3)]);
        clock.Now = ThisFight + 1_000;
        dm.SaveSummon(HealerSummon, Healer);

        Hit(dm, Sorcerer, 1_001_000, 5000, FlameBolt);
        Hit(dm, HealerSummon, 1_001_100, 700, ClericSummonSkill);
        Hit(dm, HealerSummon, 1_001_200, 700, ClericSummonSkill);

        DpsReport report = calc.GetDps();
        Assert.Equal(JobClass.CHANTER, dm.User(Healer)!.Job);
        Assert.Equal(1400d, Amount(report, Healer));
    }

    [Fact]
    public void Mismatch_on_an_authoritative_job_with_a_mapping_from_an_earlier_fight_is_dropped()
    {
        // Same mis-labelled owner, but the mapping is inherited from before this battle: that is the recycled-id
        // signature, so the contradiction is trusted.
        const int Healer = 5003, HealerSummon = 18070;
        (DataManager dm, DpsCalculator calc, _) = Boss(staleMapping: false);
        dm.Clock = () => EarlierFight;
        dm.SaveNickname(Healer, "힐러", isExecutor: false, server: Server, jobByte: ChanterJobByte);
        dm.SaveSummon(HealerSummon, Healer);
        dm.Clock = () => ThisFight;
        dm.SavePartyRoster([("마도", Server, 1), ("힐러", Server, 3)]);

        Hit(dm, Sorcerer, 1_001_000, 5000, FlameBolt);
        Hit(dm, HealerSummon, 1_001_100, 700, ClericSummonSkill);

        Assert.Equal(0d, Amount(calc.GetDps(), Healer));
    }

    [Fact]
    public void Summon_whose_owner_can_cast_the_skill_still_folds_into_the_owner()
    {
        // Control: a genuine fold (a 치유성 summon casting a 17xx skill into its 치유성 owner) is unchanged, even
        // with a mapping that predates the battle (spirits and summons are kept alive across fights).
        const int Cleric = 5004, ClericSummon = 18070;
        (DataManager dm, DpsCalculator calc, _) = Boss();
        dm.Clock = () => EarlierFight;
        dm.SaveNickname(Cleric, "힐러", isExecutor: false, server: Server, jobByte: ClericJobByte);
        dm.SaveSummon(ClericSummon, Cleric);
        dm.Clock = () => ThisFight;

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
        // A summon owner whose class is not yet known cannot be contradicted, so the fold stays.
        const int Unknown = 5005, Summon = 18071;
        (DataManager dm, DpsCalculator calc, _) = Boss();
        dm.SaveNickname(Unknown, "미상", isExecutor: false, server: Server, jobByte: 0);
        dm.SaveSummon(Summon, Unknown);
        dm.SavePartyRoster([("마도", Server, 1), ("미상", Server, 2)]);

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

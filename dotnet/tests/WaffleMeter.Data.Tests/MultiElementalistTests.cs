using WaffleMeter.Capture;
using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>
/// Several 정령성 in one party/raid: every spirit must land on its own owner or be dropped — never on the other
/// 정령성. Three paths: the 0x3641 fold (one mapping per spirit), the name-prefix fallback for an UNMAPPED spirit
/// (now gated on the roster holding a single 정령성, not on who happened to hit first), and a spirit id recycled
/// to the other 정령성's spirit (same class, invisible to the class-history guard — ended by the 0x8D04 death).
/// </summary>
public sealed class MultiElementalistTests
{
    private const int Instance = 100, BossCode = 2301722, Server = 2003;
    private const int Me = 5001, ElemA = 5002, ElemB = 5003, SpiritA = 18066, SpiritB = 18067;
    private const int ColdShock = 16010000, SpiritStrike = 16400010;

    private static (DataManager Dm, DpsCalculator Calc) Setup(bool secondElementalist = true, bool roster = true)
    {
        var dm = new DataManager { Clock = () => 1_000_000 };
        dm.LoadMobs(new Dictionary<int, Mob> { [BossCode] = new Mob(BossCode, "그리오사", Boss: true) });
        dm.LoadSkills([new Skill(SpiritStrike, "불의 정령: 화염 타격")]);
        dm.SaveMobId(Instance, BossCode);
        dm.SaveNickname(Me, "나", isExecutor: true, server: Server, jobByte: 25);
        dm.SaveNickname(ElemA, "정령A", isExecutor: false, server: Server, jobByte: 21);
        if (secondElementalist)
        {
            dm.SaveNickname(ElemB, "정령B", isExecutor: false, server: Server, jobByte: 21);
        }

        if (roster)
        {
            var members = new List<(string, int, int)> { ("나", Server, 1), ("정령A", Server, 2) };
            var jobs = new List<(string, int, int, int)> { ("나", Server, 25, 1), ("정령A", Server, 21, 1) };
            if (secondElementalist)
            {
                members.Add(("정령B", Server, 3));
                jobs.Add(("정령B", Server, 21, 1));
            }

            dm.SavePartyRoster(members);
            dm.SavePartyRosterJobPower(jobs);
        }

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
    public void Mapped_spirits_each_fold_into_their_own_owner_regardless_of_hit_order()
    {
        (DataManager dm, DpsCalculator calc) = Setup();
        dm.SaveSummon(SpiritA, ElemA);
        dm.SaveSummon(SpiritB, ElemB);

        Hit(dm, ElemA, 1_001_000, 1000, ColdShock);
        Hit(dm, SpiritB, 1_001_050, 700, SpiritStrike); // B's spirit before B's own first hit
        Hit(dm, SpiritA, 1_001_100, 500, SpiritStrike);
        Hit(dm, ElemB, 1_001_200, 2000, ColdShock);

        DpsReport r = calc.GetDps();
        Assert.Equal(1500d, Amount(r, ElemA));
        Assert.Equal(2700d, Amount(r, ElemB));
    }

    [Fact]
    public void Unmapped_spirit_is_not_credited_to_the_elementalist_who_merely_hit_first()
    {
        (DataManager dm, DpsCalculator calc) = Setup();
        dm.SaveSummon(SpiritA, ElemA); // B's spirit has no owner record

        Hit(dm, ElemA, 1_001_000, 1000, ColdShock);
        Hit(dm, SpiritB, 1_001_050, 700, SpiritStrike); // only A is a contributor so far → previously A's
        Hit(dm, ElemB, 1_001_200, 2000, ColdShock);
        Hit(dm, SpiritB, 1_001_300, 700, SpiritStrike);

        DpsReport r = calc.GetDps();
        Assert.Equal(1000d, Amount(r, ElemA));
        Assert.Equal(2000d, Amount(r, ElemB)); // dropped, not guessed
        Assert.DoesNotContain(r.Contributors, u => u.Id == SpiritB);
    }

    [Fact]
    public void Roster_elementalist_who_never_hits_still_makes_the_prefix_rule_ambiguous()
    {
        (DataManager dm, DpsCalculator calc) = Setup();

        Hit(dm, ElemA, 1_001_000, 1000, ColdShock);
        Hit(dm, SpiritB, 1_001_050, 700, SpiritStrike);

        Assert.Equal(1000d, Amount(calc.GetDps(), ElemA));
    }

    [Fact]
    public void Single_elementalist_party_still_credits_an_unmapped_spirit()
    {
        (DataManager dm, DpsCalculator calc) = Setup(secondElementalist: false);

        Hit(dm, ElemA, 1_001_000, 1000, ColdShock);
        Hit(dm, SpiritA, 1_001_050, 700, SpiritStrike);

        Assert.Equal(1700d, Amount(calc.GetDps(), ElemA));
    }

    [Fact]
    public void Without_a_roster_the_contributor_only_rule_is_unchanged()
    {
        (DataManager dm, DpsCalculator calc) = Setup(roster: false);

        Hit(dm, ElemA, 1_001_000, 1000, ColdShock);
        Hit(dm, SpiritB, 1_001_050, 700, SpiritStrike); // one 정령성 contributor, no roster → credited (as before)
        Hit(dm, ElemB, 1_001_200, 2000, ColdShock);
        Hit(dm, SpiritB, 1_001_300, 700, SpiritStrike); // two contributors → dropped (as before)

        DpsReport r = calc.GetDps();
        Assert.Equal(1700d, Amount(r, ElemA));
        Assert.Equal(2000d, Amount(r, ElemB));
    }

    [Fact]
    public void Spirit_death_ends_its_mapping_so_a_recycled_id_does_not_fold_to_the_old_owner()
    {
        (DataManager dm, DpsCalculator calc) = Setup();
        dm.SaveSummon(SpiritA, ElemA);

        Hit(dm, ElemA, 1_001_000, 1000, ColdShock);
        Hit(dm, ElemB, 1_001_010, 2000, ColdShock);
        Hit(dm, SpiritA, 1_001_050, 500, SpiritStrike);
        calc.GetDps();

        dm.SaveEntityDeath(SpiritA, 1_002_000);   // A's spirit dies; the id is reissued to B's new spirit whose
        Hit(dm, SpiritA, 1_003_000, 700, SpiritStrike); // spawn was lost → same class, so only the death can tell

        DpsReport r = calc.GetDps();
        Assert.Null(dm.SummonerId(SpiritA));
        Assert.Equal(1500d, Amount(r, ElemA));     // previously 2200
        Assert.Equal(2000d, Amount(r, ElemB));     // two 정령성 in the roster → not guessed either
    }

    [Fact]
    public void Ten_man_raid_counts_the_other_sub_party_too()
    {
        (DataManager dm, DpsCalculator calc) = Setup(secondElementalist: false);
        // The other sub-party's 정령성 (slot 7) is in the roster but has never been seen as a uid.
        dm.SavePartyRoster([("나", Server, 1), ("정령A", Server, 2), ("정령C", Server, 7)]);
        dm.SavePartyRosterJobPower([("나", Server, 25, 1), ("정령A", Server, 21, 1), ("정령C", Server, 21, 1)]);

        Hit(dm, ElemA, 1_001_000, 1000, ColdShock);
        Hit(dm, SpiritB, 1_001_050, 700, SpiritStrike); // an unmapped spirit: could be 정령C's

        Assert.Equal(1000d, Amount(calc.GetDps(), ElemA));
    }
}

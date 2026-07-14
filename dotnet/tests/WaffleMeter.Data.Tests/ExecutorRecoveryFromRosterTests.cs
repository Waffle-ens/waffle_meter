using WaffleMeter.Capture;
using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>
/// Recovering "which uid is me" when the own-nickname packet (0x3633) never arrived — the meter was started,
/// or restarted, while the player was already inside a dungeon, so the one packet that says "this uid is you"
/// had already been sent. Symptom seen live: the player's own damage lands but under a nameless row, so the
/// self colour, the self row name and — because a recording is scoped to the party BY NAME — their whole
/// replay track go missing (diag: contributors=2, roster=2, self=MISSING).
/// </summary>
public sealed class ExecutorRecoveryFromRosterTests
{
    private const int Instance = 100;
    private const int BossCode = 2301008;
    private const int Me = 5001;      // our uid — the game never told us it is ours
    private const int Mate = 5002;
    private const int RangerSkill = 16080000; // job-locked: what makes a nameless actor register as a player

    private static (DataManager Dm, DpsCalculator Calc) Fight()
    {
        var dm = new DataManager { Clock = () => 1_000_000 };
        dm.LoadMobs(new Dictionary<int, Mob> { [BossCode] = new Mob(BossCode, "보스", Boss: true) });
        dm.SaveMobId(Instance, BossCode);
        dm.MobHp(Instance, 100_000);
        dm.StartBattle(Instance);
        return (dm, new DpsCalculator(dm));
    }

    private static void Hit(DataManager dm, int actor, int skill = RangerSkill) =>
        dm.SaveDamage(
            new ParsedDamagePacket
            {
                ActorId = actor,
                TargetId = Instance,
                Damage = 1000,
                SkillCode = skill,
                Timestamp = 1_000_100,
            },
            dm.CurrentEpoch());

    [Fact]
    public void The_one_roster_name_nobody_claims_belongs_to_the_one_nameless_dealer()
    {
        (DataManager dm, DpsCalculator calc) = Fight();
        dm.SavePartyRoster([("나", 2003, 1), ("동료", 2003, 2)]);
        dm.SaveNickname(Mate, "동료", isExecutor: false, server: 2003, jobByte: 32); // the game named them (0x3645)

        Hit(dm, Mate);
        Hit(dm, Me); // our damage: a nameless provisional row
        DpsReport report = calc.GetDps();

        User me = Assert.Single(report.Contributors, u => u.Id == Me);
        Assert.Equal("나", me.Nickname);
        Assert.Equal(2003, me.Server);
        Assert.True(me.IsExecutor);
        Assert.Equal(Me, report.ExecutorId); // and everything downstream (self colour, replay scoping) follows
    }

    [Fact]
    public void A_party_member_who_has_not_damaged_yet_leaves_it_ambiguous_so_we_wait()
    {
        // 동료 is in the party but hasn't dealt damage and hasn't been named, so TWO roster identities are
        // unaccounted for. Guessing here could label us with a party member's name.
        (DataManager dm, DpsCalculator calc) = Fight();
        dm.SavePartyRoster([("나", 2003, 1), ("동료", 2003, 2)]);

        Hit(dm, Me);
        DpsReport report = calc.GetDps();

        Assert.Equal(0, report.ExecutorId);
        Assert.True(string.IsNullOrEmpty(Assert.Single(report.Contributors, u => u.Id == Me).Nickname));
    }

    [Fact]
    public void Two_nameless_dealers_are_ambiguous_too()
    {
        (DataManager dm, DpsCalculator calc) = Fight();
        dm.SavePartyRoster([("나", 2003, 1), ("동료", 2003, 2)]);
        dm.SaveNickname(Mate, "동료", isExecutor: false, server: 2003, jobByte: 32);

        Hit(dm, Mate);
        Hit(dm, Me);
        Hit(dm, 5003); // a second unidentified dealer (a stranger on a shared boss)
        DpsReport report = calc.GetDps();

        Assert.Equal(0, report.ExecutorId);
    }

    [Fact]
    public void Nothing_is_touched_once_the_game_has_told_us_who_we_are()
    {
        (DataManager dm, DpsCalculator calc) = Fight();
        dm.SavePartyRoster([("나", 2003, 1), ("동료", 2003, 2)]);
        dm.SaveNickname(Me, "나", isExecutor: true, server: 2003, jobByte: 13); // the normal 0x3633 path

        Hit(dm, Me);
        DpsReport report = calc.GetDps();

        Assert.Equal(Me, report.ExecutorId);
        Assert.Equal("나", Assert.Single(report.Contributors, u => u.Id == Me).Nickname);
    }

    [Fact]
    public void Without_a_party_there_is_nothing_to_match_against()
    {
        (DataManager dm, DpsCalculator calc) = Fight(); // solo: no 0x9702 roster

        Hit(dm, Me);

        Assert.Equal(0, calc.GetDps().ExecutorId);
    }
}

using WaffleMeter.Capture;
using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>
/// Covers the "던전 안인데 파티원 아닌 사람 한 명이 잡힌다" phantom (the 그리오사 case). Root cause: a healer's
/// summon whose 0x3641 owner record is tagged 07 02 01 (not the usual 07 02 06) was left unmapped, so its
/// boss damage could not be folded into the summoner; when that summon also dealt damage BEFORE its own spawn
/// packet registered it as a mob, AccumulatePacket provisionally registered the raw entity id as a player
/// (its damage skill is job-locked), surfacing a nickname-less non-party row.
///
/// Two fixes, one per test class:
///  - StreamProcessor: the 07 02 06 owner scan falls back to 07 02 01, validated against a known player so a
///    stray match inside a mob-spawn packet cannot mis-map a mob to a garbage owner. (Fix 2)
///  - DpsCalculator: a provisional (nickname-less) contributor later revealed as a summon/mob is retracted. (Fix 1)
/// The packet bytes below are the REAL captured 0x3641 summon packets (owner 11651 = 0x2D83) from the session
/// that produced the report — the 251-byte 18066 (07 02 01) and a 209-byte sibling (07 02 06).
/// </summary>
public sealed class SummonOwnerParseTests
{
    // Real 0x3641 summon packet for instance 18066 — the atypical 251-byte variant whose owner record is
    // tagged 07 02 01. Owner (0x2D83 = 11651) sits 3 bytes past the 07.
    private const string Summon18066_0702_01 =
        "FD014136928D015F000109EBB094EB8298EB8298AA912C00400200C6A7C600F69F4600F8C145DA6C1843BF2001A6E702A6E702C4270000C4270000000000000000000000000000F0C6020064000000F04902000100000000000000A086010000000000C8571000010102110181969800FFFFFFFFFFFFFFFF8075D52ABB030000928D01010400C6A7C600F69F4600F8C1451304EBE1380A60EA000000000000155C64429F010000928D010131B0050100C6A7C600F69F4600F8C145070201832D0000120500000000DF0706EC9CA0EC9DBC02000200000000000000000000000000000003CD00F81E0000D00042030000D600960000003200000000";

    // Real 0x3641 summon packet for instance 22744 — the uniform 209-byte variant tagged 07 02 06. Owner 11651.
    private const string Summon22744_0702_06 =
        "D3014136D8B1015F000109EBB094EB8298EB8298AA912C004002005F02C70008844500D8D345C18EAC41570F01A6E702A6E702C4270000C4270000000000000000000000000000F0C6020064000000F04902000100000000000000A086010000000000C8571000010101110181969800FFFFFFFFFFFFFFFF8075D52ABB030000D8B1010102005F02C70008844500D8D345070206832D0000120500000000DF0706EC9CA0EC9DBC02000200000000000000000000000000000003CD00EC200000D00042030000D600960000003200000000";

    private static DataManager NewDm() => new();

    [Fact]
    public void Owner_marker_0702_06_maps_without_needing_a_known_owner()
    {
        DataManager dm = NewDm(); // 11651 is NOT a known user here — the primary marker does not require it
        new StreamProcessor(NullStreamProcessorSink.Instance, dm)
            .OnPacketReceived(Convert.FromHexString(Summon22744_0702_06), 0);

        Assert.Equal(11651, dm.SummonerId(22744));
    }

    [Fact]
    public void Owner_marker_0702_01_maps_when_owner_is_a_known_player()
    {
        DataManager dm = NewDm();
        dm.SaveNickname(11651, "바나나", isExecutor: false, server: 2015, jobByte: 32); // owner recognized

        new StreamProcessor(NullStreamProcessorSink.Instance, dm)
            .OnPacketReceived(Convert.FromHexString(Summon18066_0702_01), 0);

        Assert.Equal(11651, dm.SummonerId(18066)); // fallback recovered the previously-orphaned summon
    }

    [Fact]
    public void Owner_marker_0702_01_is_rejected_when_owner_is_not_a_known_player()
    {
        DataManager dm = NewDm(); // 11651 NOT registered -> the fallback must not trust a stray 07 02 01

        new StreamProcessor(NullStreamProcessorSink.Instance, dm)
            .OnPacketReceived(Convert.FromHexString(Summon18066_0702_01), 0);

        // No mapping: this guards against the over-greedy fallback mis-mapping mob-spawn packets (which also
        // contain 07 02 01) to a fixed garbage owner.
        Assert.Null(dm.SummonerId(18066));
    }
}

/// <summary>
/// The DpsCalculator half: a summon that dealt damage before its spawn packet arrived is provisionally shown
/// as a player, then retracted once the game reveals it as a summon/mob. Guards the phantom row from lingering.
/// </summary>
public sealed class SummonPhantomRetractionTests
{
    private const int Instance = 100;
    private const int BossCode = 2301722;   // 금기의 마수 그리오사
    private const int Dealer = 5001;         // real party member (executor)
    private const int SummonId = 18066;      // healer summon (no owner map yet, spawn not processed)
    private const int PetCode = 2920874;     // the summon's mob code (not a boss)
    private const int ClericSkill = 17153450; // job-locked cleric skill the summon casts (ConvertFromSkill != null)
    private const int FighterSkill = 19010000;

    private static (DataManager Dm, DpsCalculator Calc) Boss()
    {
        var dm = new DataManager { Clock = () => 1_000_000 };
        dm.LoadMobs(new Dictionary<int, Mob> { [BossCode] = new Mob(BossCode, "그리오사", Boss: true) });
        dm.SaveMobId(Instance, BossCode);
        dm.SaveNickname(Dealer, "딜러", isExecutor: true, server: 2003, jobByte: 20);
        return (dm, new DpsCalculator(dm));
    }

    private static void Hit(DataManager dm, int actor, long ts, int dmg, int skill) =>
        dm.SaveDamage(
            new ParsedDamagePacket { ActorId = actor, TargetId = Instance, Damage = dmg, Timestamp = ts, SkillCode = skill },
            dm.CurrentEpoch());

    [Fact]
    public void Provisional_summon_row_is_retracted_once_revealed_as_a_mob_instance()
    {
        (DataManager dm, DpsCalculator calc) = Boss();
        dm.MobHp(Instance, 100_000);
        dm.StartBattle(Instance);

        // The summon lands hits on the boss BEFORE its own 0x3641 spawn packet is processed (packet reorder):
        // no summon→owner map yet, and it is not yet a known mob instance.
        Hit(dm, Dealer, 1_001_000, 5000, FighterSkill);
        Hit(dm, SummonId, 1_001_100, 900, ClericSkill);
        Hit(dm, SummonId, 1_001_200, 900, ClericSkill);

        DpsReport live = calc.GetDps();
        // Pre-reveal, the race provisionally shows the summon as a nickname-less player (the phantom).
        Assert.Contains(live.Contributors, u => u.Id == SummonId);
        Assert.Contains(live.Contributors, u => u.Id == Dealer);

        // The delayed spawn packet lands: the entity is now registered as a (non-boss) mob instance.
        dm.SaveMobId(SummonId, PetCode);

        DpsReport after = calc.GetDps();
        Assert.DoesNotContain(after.Contributors, u => u.Id == SummonId); // phantom retracted
        Assert.Contains(after.Contributors, u => u.Id == Dealer);          // real player kept
        Assert.False(after.Information.ContainsKey(SummonId));             // and its damage row is gone
    }

    [Fact]
    public void Provisional_summon_row_is_retracted_once_mapped_to_an_owner()
    {
        (DataManager dm, DpsCalculator calc) = Boss();
        dm.MobHp(Instance, 100_000);
        dm.StartBattle(Instance);

        Hit(dm, Dealer, 1_001_000, 5000, FighterSkill);
        Hit(dm, SummonId, 1_001_100, 900, ClericSkill);
        Assert.Contains(calc.GetDps().Contributors, u => u.Id == SummonId);

        // The owner map arrives (07 02 01 fallback resolved the summoner).
        dm.SaveSummon(SummonId, Dealer);

        Assert.DoesNotContain(calc.GetDps().Contributors, u => u.Id == SummonId);
    }

    [Fact]
    public void Real_player_without_a_nickname_yet_is_not_retracted()
    {
        // A late-identity player (난입 executor before 0x3633) is also provisional, but is never a summon/mob,
        // so the retraction must leave it alone — otherwise the barge-join fix would regress.
        (DataManager dm, DpsCalculator calc) = Boss();
        dm.MobHp(Instance, 100_000);
        dm.StartBattle(Instance);

        const int LatePlayer = 7777;
        Hit(dm, LatePlayer, 1_001_100, 4000, FighterSkill); // job-locked skill, no identity yet
        Assert.Contains(calc.GetDps().Contributors, u => u.Id == LatePlayer);

        // No spawn/owner reveal — it stays (it is a real player, just unnamed for now).
        Assert.Contains(calc.GetDps().Contributors, u => u.Id == LatePlayer);
    }
}

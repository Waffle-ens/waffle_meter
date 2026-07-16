using WaffleMeter.Capture;
using WaffleMeter.Data;
using WaffleMeter.Stats;
using Xunit;

namespace WaffleMeter.Stats.Tests;

public sealed class StatsPayloadBuilderTests
{
    private static DataManager TwoPlayerParty(int mePower = 5000, int allyPower = 3000)
    {
        var dm = new DataManager();
        dm.SaveNickname(1, "Me", isExecutor: true, server: 3, jobByte: 5);    // GLADIATOR
        if (mePower > 0)
        {
            dm.SaveUserPower(1, mePower);
        }

        dm.SaveNickname(2, "Ally", isExecutor: false, server: 3, jobByte: 25); // SORCERER
        if (allyPower > 0)
        {
            dm.SaveUserPower(2, allyPower);
        }

        return dm;
    }

    private static DpsLog SampleLog(DataManager dm, bool boss = true)
    {
        User me = dm.User(1)!;
        User ally = dm.User(2)!;

        var report = new DpsReport
        {
            Contributors = new List<User> { me, ally },
            BattleStart = 1_000_000,
            BattleEnd = 1_030_000, // duration 30000
            Target = new MobInfo(100, new Mob(12345, "센터보스", boss), remainHp: 0, maxHp: 1_000_000),
            Information = new Dictionary<int, DpsInformation>
            {
                [1] = new DpsInformation(1_000_000, 50_000, 60.0, 40.0),
                [2] = new DpsInformation(600_000, 30_000, 40.0, 24.0),
            },
        };

        return new DpsLog
        {
            Report = report,
            SkillDetails = new Dictionary<int, Dictionary<string, AnalyzedSkill>>
            {
                [1] = new() { ["11020001"] = new AnalyzedSkill { SkillCode = 11020001, Name = "강타", DamageAmount = 1_000_000, Times = 100, CritTimes = 20, DoubleTimes = 10, PerfectTimes = 5, BackTimes = 3, ParryTimes = 2 } },
                [2] = new() { ["15210001"] = new AnalyzedSkill { SkillCode = 15210001, Name = "파이어", DamageAmount = 600_000, Times = 50, CritTimes = 10 } },
            },
            BuffRates = new Dictionary<int, List<OperatingData>>
            {
                [1] = new()
                {
                    new OperatingData(110200050, "글래디버프", null, null, 95.5, 1), // self (caster==owner, prefix match)
                    new OperatingData(150000010, "동료버프", null, null, 50.0, 2),  // party (caster 2 != owner 1)
                },
            },
            BossBuffRates = new List<OperatingData>
            {
                new OperatingData(990000000, "보스디버프", null, null, 80.0, 1),
            },
        };
    }

    private static StatsPayloadBuilder Builder(DataManager dm, bool isPublic = false) =>
        new(dm, publicCharacterProvider: () => isPublic, clock: () => 1_700_000_000_000);

    private static StatsUploadPayload BuildOk(DataManager dm, DpsLog log, bool isPublic = false)
    {
        BuildResult result = Builder(dm, isPublic).Build(log, "1.7.9", killConfirmed: true);
        return Assert.IsType<BuildResult.Payload>(result).Value;
    }

    [Fact]
    public void Builds_character_encounter_and_battle()
    {
        DataManager dm = TwoPlayerParty();
        StatsUploadPayload payload = BuildOk(dm, SampleLog(dm));

        Assert.Equal(5, payload.SchemaVersion);
        Assert.Equal("1.7.9", payload.ClientVersion);
        Assert.Equal(1_700_000_000_000, payload.UploadedAt);
        Assert.Equal(StatsIdentity.CharacterIdentityHash(3, "Me"), payload.Character.IdentityHash);
        Assert.Equal("검성", payload.Character.Job);
        Assert.Equal(5000, payload.Character.Power);
        Assert.False(payload.Character.Public);
        Assert.Equal(12345, payload.Encounter.MobCode);
        Assert.Equal("센터보스", payload.Encounter.BossName);
        Assert.Equal(30_000, payload.Battle.DurationMs);
        Assert.Equal(2, payload.Battle.PartySize);
        Assert.Equal(64, payload.BattleHash.Length);
    }

    [Fact]
    public void Participants_sorted_by_damage_with_uploader_flag()
    {
        DataManager dm = TwoPlayerParty();
        StatsUploadPayload payload = BuildOk(dm, SampleLog(dm));

        Assert.Equal(2, payload.Participants.Count);
        Assert.True(payload.Participants[0].IsUploader);   // Me, higher damage
        Assert.Equal(5000, payload.Participants[0].Power);
        Assert.False(payload.Participants[1].IsUploader);  // Ally
        Assert.Equal(3000, payload.Participants[1].Power);
    }

    [Fact]
    public void Own_result_and_skills_are_computed()
    {
        DataManager dm = TwoPlayerParty();
        StatsUploadPayload payload = BuildOk(dm, SampleLog(dm));

        Assert.Equal(1_000_000, payload.Result.TotalDamage);
        Assert.Equal(50_000, payload.Result.Dps);
        Assert.Equal(60.0, payload.Result.PartyContribution);
        Assert.Equal(100, payload.Result.HitCount);
        Assert.Equal(20.0, payload.Result.CritRate); // 20/100 * 100

        StatsSkillPayload skill = Assert.Single(payload.Skills);
        Assert.Equal(11020001, skill.SkillCode);
        Assert.Equal("direct", skill.DamageType);
        Assert.Equal(1_000_000, skill.Damage);
        Assert.Equal(100.0, skill.Share);
    }

    [Fact]
    public void Front_and_back_rates_divide_by_the_flag_bearing_hit_count()
    {
        // 후방/전방 are facing judgments carried only by flag-bearing (sw6) hits, so they divide by FlaggedTimes —
        // matching the meter's 후방/전방 detail tiles. Crit stays over every hit.
        DataManager dm = TwoPlayerParty();
        DpsLog log = SampleLog(dm);
        log.SkillDetails[1]["11020001"] = new AnalyzedSkill
        {
            SkillCode = 11020001, Name = "강타", DamageAmount = 1_000_000,
            Times = 100, FlaggedTimes = 40, FrontTimes = 24, BackTimes = 12, CritTimes = 20,
        };

        StatsUploadPayload payload = BuildOk(dm, log);

        Assert.Equal(60.0, payload.Result.FrontRate); // 24 / 40 flagged (NOT 24 / 100 hits)
        Assert.Equal(30.0, payload.Result.BackRate);  // 12 / 40 flagged (NOT 12 / 100 hits)
        Assert.Equal(20.0, payload.Result.CritRate);  // crit is per-hit: 20 / 100
    }

    [Fact]
    public void Uploader_dps_series_and_self_buff_intervals_come_from_the_frozen_snapshot()
    {
        DataManager dm = TwoPlayerParty();
        DpsLog log = SampleLog(dm); // BattleStart 1_000_000, BattleEnd 1_030_000 (30s), uploader = GLADIATOR(prefix 11)
        log.Report.DpsSeries = new Dictionary<int, long[]> { [1] = [100, 0, 200] };
        log.Report.BuffIntervals = new Dictionary<int, List<BuffTimeline>>
        {
            [1] =
            [
                new BuffTimeline(110200050, "글래디버프", 1, 11020000, 11, [(1_005_000L, 1_020_000L)]), // self class buff
                new BuffTimeline(22101051, "용기의 주문서", 1, 22101051, 0, [(1_000_000L, 1_030_000L)]), // consumable → drop
                new BuffTimeline(150000010, "동료버프", 2, 15000000, 15, [(1_000_000L, 1_030_000L)]),   // party → drop
            ],
        };

        StatsUploadPayload payload = BuildOk(dm, log);

        Assert.NotNull(payload.DpsSeries);
        Assert.Equal(1, payload.DpsSeries!.Step);
        Assert.Equal([100L, 0L, 200L], payload.DpsSeries.Damage);

        StatsSelfBuffIntervalPayload buff = Assert.Single(payload.SelfBuffIntervals!); // only the own-class buff survives
        Assert.Equal(11020000, buff.BaseCode);
        Assert.Equal("글래디버프", buff.Name);
        Assert.Equal([5, 20], buff.Spans); // whole-second offsets from BattleStart (5s..20s)
    }

    [Fact]
    public void Missing_dps_snapshot_omits_the_graph_fields()
    {
        // A report without the frozen snapshot (old/pre-freeze) must omit dpsSeries/selfBuffIntervals, not send empty.
        DataManager dm = TwoPlayerParty();
        StatsUploadPayload payload = BuildOk(dm, SampleLog(dm)); // SampleLog leaves DpsSeries/BuffIntervals empty

        Assert.Null(payload.DpsSeries);
        Assert.Null(payload.SelfBuffIntervals);
    }

    [Fact]
    public void Buffs_are_classified_self_and_party_with_actor_indexes()
    {
        DataManager dm = TwoPlayerParty();
        StatsUploadPayload payload = BuildOk(dm, SampleLog(dm));

        StatsBuffPayload self = payload.Buffs.Single(b => b.BuffCode == 110200050);
        Assert.Equal("self", self.Source);
        Assert.Equal("participant", self.Scope);
        Assert.Equal("buff", self.Category);
        Assert.Equal(95.5, self.OperatingRate);
        Assert.Equal(0, self.OwnerParticipantIndex);
        Assert.Equal(0, self.ActorParticipantIndex); // actor 1 -> participant 0

        StatsBuffPayload party = payload.Buffs.Single(b => b.BuffCode == 150000010);
        Assert.Equal("party", party.Source);
        Assert.Equal(1, party.ActorParticipantIndex); // actor 2 -> participant 1
    }

    [Fact]
    public void Boss_debuffs_have_boss_scope_and_no_source()
    {
        DataManager dm = TwoPlayerParty();
        StatsUploadPayload payload = BuildOk(dm, SampleLog(dm));

        StatsBuffPayload debuff = Assert.Single(payload.BossDebuffs);
        Assert.Equal(990000000, debuff.BuffCode);
        Assert.Equal("boss", debuff.Scope);
        Assert.Equal("debuff", debuff.Category);
        Assert.Null(debuff.Source);
        Assert.Equal(0, debuff.ActorParticipantIndex);
    }

    [Fact]
    public void Buffs_carry_the_base_code_the_rank_variants_collapsed_to()
    {
        DataManager dm = TwoPlayerParty();
        DpsLog log = SampleLog(dm);
        log.BuffRates[1] =
        [
            new OperatingData(118000071, "살기 파열", null, null, 99.8, 1, 11800000, 11),
        ];

        StatsUploadPayload payload = BuildOk(dm, log);

        StatsBuffPayload buff = Assert.Single(payload.Buffs);
        Assert.Equal(118000071, buff.BuffCode); // raw runtime code — the web's rDPS looks buff values up by it
        Assert.Equal(11800000, buff.BaseCode);
    }

    [Fact]
    public void Category_stays_target_derived()
    {
        // "buff" for a player target, "debuff" for the boss. That IS the taxonomy: a player skill's debuff lands
        // on its target, never on the caster. The stats web also pins participant rows with z.literal("buff"),
        // so any other value rejects the whole upload.
        DataManager dm = TwoPlayerParty();
        StatsUploadPayload payload = BuildOk(dm, SampleLog(dm));

        Assert.All(payload.Buffs, b => Assert.Equal("buff", b.Category));
        Assert.All(payload.BossDebuffs, b => Assert.Equal("debuff", b.Category));
        Assert.Equal(5, payload.SchemaVersion); // v5 = additive frontRate + dpsSeries + selfBuffIntervals (web accepts v5)
    }

    [Fact]
    public void A_self_buff_that_fell_back_to_its_base_code_is_still_classified_self()
    {
        // Regression: BuffSource used to read the job prefix off buffCode. The fallback path emits the 8-digit
        // base (11800000 / 10_000_000 == 1), so the caster's own buff was labelled "other" — and the stats web
        // drops every non-scroll "other" row, which is how these vanished from the web but not the meter.
        DataManager dm = TwoPlayerParty();
        DpsLog log = SampleLog(dm);
        log.BuffRates[1] =
        [
            new OperatingData(11800000, "살기 파열", null, null, 60.0, 1, 11800000, 11),
        ];

        StatsUploadPayload payload = BuildOk(dm, log);

        Assert.Equal("self", Assert.Single(payload.Buffs).Source);
    }

    [Fact]
    public void An_eight_digit_mob_debuff_on_a_player_is_never_that_players_self_buff()
    {
        // 15003201(중독) is a mob effect whose 8-digit code leads with 15 — the 마도성 prefix. The ally IS 마도성,
        // so deriving the prefix from the code (or from BaseCode, which is the code itself here) would report the
        // mob's poison as her own class buff. Only the raw 9-digit job band may grant a prefix.
        DataManager dm = TwoPlayerParty();
        DpsLog log = SampleLog(dm);
        log.BuffRates[2] =
        [
            new OperatingData(15003201, "중독", null, null, 30.0, 2, 15003201, JobPrefix: 0),
        ];

        StatsUploadPayload payload = BuildOk(dm, log);

        StatsParticipantPayload ally = payload.Participants.Single(p => !p.IsUploader);
        Assert.Equal("마도성", ally.Job);

        StatsBuffPayload row = Assert.Single(ally.Buffs);
        Assert.Equal("other", row.Source);
        Assert.Equal(15003201, row.BaseCode);
    }

    [Fact]
    public void Party_composition_counts_jobs_and_synergy()
    {
        DataManager dm = TwoPlayerParty();
        StatsUploadPayload payload = BuildOk(dm, SampleLog(dm));

        Assert.Equal(1, payload.PartyComposition.Jobs["검성"]);
        Assert.Equal(1, payload.PartyComposition.Jobs["마도성"]);
        Assert.True(payload.PartyComposition.Synergy.HasGladiator);
        Assert.False(payload.PartyComposition.Synergy.HasCleric);
        Assert.Equal(1, payload.PartyComposition.Synergy.SynergyCount); // only GLADIATOR is a synergy job
    }

    [Theory]
    [InlineData(false, true, "not_uploadable_boss")] // not a boss
    [InlineData(true, false, "not_kill")]            // kill not confirmed
    public void Skips_when_not_uploadable(bool boss, bool killConfirmed, string expected)
    {
        DataManager dm = TwoPlayerParty();
        BuildResult result = Builder(dm).Build(SampleLog(dm, boss), "1.7.9", killConfirmed);
        Assert.Equal(expected, Assert.IsType<BuildResult.Skip>(result).Reason);
    }

    [Fact]
    public void Skips_when_own_power_unresolved()
    {
        DataManager dm = TwoPlayerParty(mePower: 0);
        BuildResult result = Builder(dm).Build(SampleLog(dm), "1.7.9", killConfirmed: true);
        Assert.Equal("own_power_unresolved", Assert.IsType<BuildResult.Skip>(result).Reason);
    }

    [Fact]
    public void Skips_when_a_participant_power_unresolved()
    {
        DataManager dm = TwoPlayerParty(allyPower: 0);
        BuildResult result = Builder(dm).Build(SampleLog(dm), "1.7.9", killConfirmed: true);
        Assert.Equal("participant_power_unresolved", Assert.IsType<BuildResult.Skip>(result).Reason);
    }

    [Fact]
    public void OwnCharacter_reports_executor_snapshot()
    {
        DataManager dm = TwoPlayerParty();
        StatsOwnCharacter own = Builder(dm).OwnCharacter();

        Assert.True(own.Detected);
        Assert.Equal(1, own.Id);
        Assert.Equal("Me", own.Nickname);
        Assert.Equal("검성", own.Job);
        Assert.Equal(5000, own.Power);
    }
}

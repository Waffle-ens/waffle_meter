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

        Assert.Equal(4, payload.SchemaVersion);
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

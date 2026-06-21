using WaffleMeter.Capture;
using WaffleMeter.Data;
using WaffleMeter.Stats;
using Xunit;

namespace WaffleMeter.Stats.Tests;

/// <summary>
/// Covers the 8-인 공대 sub-party tagging on the upload payload: each participant carries its raw roster
/// slot (1-8) and a derived party number (slots 1-4 = party 1 = uploader's party, 5-8 = party 2). For a
/// non-raid party (only slots 1-4 / no roster) the tags stay null and are omitted on send.
/// </summary>
public sealed class StatsPayloadPartyNumberTests
{
    // Two damage-dealers (Me = executor, Ally) vs a boss; PartySlots is set per-test to model the roster.
    private static (DataManager Dm, DpsLog Log) Scene()
    {
        var dm = new DataManager();
        dm.SaveNickname(1, "Me", isExecutor: true, server: 3, jobByte: 5);
        dm.SaveUserPower(1, 5000);
        dm.SaveNickname(2, "Ally", isExecutor: false, server: 3, jobByte: 25);
        dm.SaveUserPower(2, 3000);

        var report = new DpsReport
        {
            Contributors = new List<User> { dm.User(1)!, dm.User(2)! },
            BattleStart = 1_000_000,
            BattleEnd = 1_030_000,
            Target = new MobInfo(100, new Mob(12345, "보스", true), remainHp: 0, maxHp: 1_000_000),
            Information = new Dictionary<int, DpsInformation>
            {
                [1] = new DpsInformation(1_000_000, 50_000, 60.0, 40.0),
                [2] = new DpsInformation(600_000, 30_000, 40.0, 24.0),
            },
        };
        var log = new DpsLog
        {
            Report = report,
            SkillDetails = new Dictionary<int, Dictionary<string, AnalyzedSkill>>
            {
                [1] = new() { ["11020001"] = new AnalyzedSkill { SkillCode = 11020001, Name = "강타", DamageAmount = 1_000_000, Times = 100 } },
                [2] = new() { ["15210001"] = new AnalyzedSkill { SkillCode = 15210001, Name = "파이어", DamageAmount = 600_000, Times = 50 } },
            },
        };
        return (dm, log);
    }

    private static StatsUploadPayload Build(DataManager dm, DpsLog log)
    {
        var builder = new StatsPayloadBuilder(dm, publicCharacterProvider: () => false, clock: () => 1_700_000_000_000);
        return Assert.IsType<BuildResult.Payload>(builder.Build(log, "2.0.0", killConfirmed: true)).Value;
    }

    [Fact]
    public void Eight_player_raid_tags_each_participant_with_party_number()
    {
        (DataManager dm, DpsLog log) = Scene();
        // Frozen 8-인 공대 roster: Me = slot 2 (party 1), Ally = slot 6 (party 2); the other six slots make
        // a 2nd party (slots 5-8) present so the upload tags the sub-parties.
        log.Report.PartySlots = new Dictionary<int, int>
        {
            [1] = 2, [2] = 6, [10] = 1, [11] = 3, [12] = 4, [13] = 5, [14] = 7, [15] = 8,
        };

        StatsUploadPayload payload = Build(dm, log);

        StatsParticipantPayload me = payload.Participants.Single(p => p.IsUploader);
        StatsParticipantPayload ally = payload.Participants.Single(p => !p.IsUploader);
        Assert.Equal(2, me.PartySlot);
        Assert.Equal(1, me.PartyNumber);    // slots 1-4 => party 1 (uploader's own party)
        Assert.Equal(6, ally.PartySlot);
        Assert.Equal(2, ally.PartyNumber);  // slots 5-8 => party 2
    }

    [Fact]
    public void Non_raid_party_leaves_party_tags_null()
    {
        (DataManager dm, DpsLog log) = Scene();
        log.Report.PartySlots = new Dictionary<int, int> { [1] = 1, [2] = 2 }; // 4-인 이하, no 2nd party

        StatsUploadPayload payload = Build(dm, log);

        Assert.All(payload.Participants, p => Assert.Null(p.PartyNumber));
        Assert.All(payload.Participants, p => Assert.Null(p.PartySlot));
    }

    [Fact]
    public void No_roster_leaves_party_tags_null()
    {
        (DataManager dm, DpsLog log) = Scene(); // PartySlots empty (no 0x9702 captured)

        StatsUploadPayload payload = Build(dm, log);

        Assert.All(payload.Participants, p => Assert.Null(p.PartyNumber));
    }
}

using WaffleMeter.Capture;
using WaffleMeter.Data;
using WaffleMeter.Stats;
using Xunit;

namespace WaffleMeter.Stats.Tests;

/// <summary>
/// Mirrors the stats site's acceptance rules for raid sub-party data and runs a real payload through them.
/// The site turns the split on only when ALL of these hold (src/server/reports/ingest-report.ts):
/// <code>
///   getRaidPartySize(battle.partySize)          -> 10 | 8 | null
///   participants.length === raidPartySize
///   hasCompleteRaidSlots  -> exactly the set {1..raidPartySize}, no duplicates
///   hasConsistentSubPartyFields -> every partySlot present, AND partyNumber is absent
///                                  OR equals (partySlot &lt;= raidPartySize/2 ? 1 : 2)
/// </code>
/// It is all-or-nothing: one disagreeing field discards the sub-party split for the entire battle. That is
/// exactly how every 8-인 공대 was being dropped — the meter derived partyNumber with a hardcoded /5, while
/// the site splits an 8-인 raid 4+4, so slot 5 disagreed on every single raid.
/// <para>These rules live in another repository, so this test is the tripwire for contract drift: if the
/// site changes them, this mirror stops describing reality and the assertions here should be revisited.</para>
/// </summary>
public sealed class StatsPayloadRaidSubPartyContractTests
{
    private static int? GetRaidPartySize(int payloadPartySize) =>
        payloadPartySize == 10 ? 10 : payloadPartySize == 8 ? 8 : null;

    private static int SlotsPerSubParty(int raidPartySize) => raidPartySize == 10 ? 5 : 4;

    private static int PartyNumberFromSlot(int partySlot, int raidPartySize) =>
        partySlot <= SlotsPerSubParty(raidPartySize) ? 1 : 2;

    private static bool HasCompleteRaidSlots(IReadOnlyList<StatsParticipantPayload> participants, int raidPartySize)
    {
        var slots = participants.Select(p => p.PartySlot).ToList();
        var distinct = slots.Distinct().ToList();
        return slots.Count == raidPartySize
            && distinct.Count == raidPartySize
            && Enumerable.Range(1, raidPartySize).All(slot => distinct.Contains(slot));
    }

    private static bool HasConsistentSubPartyFields(IReadOnlyList<StatsParticipantPayload> participants, int raidPartySize) =>
        participants.All(p => p.PartySlot is int slot
            && (p.PartyNumber is null || p.PartyNumber == PartyNumberFromSlot(slot, raidPartySize)));

    private static bool SubPartyKnown(StatsUploadPayload payload)
    {
        if (GetRaidPartySize(payload.Battle.PartySize) is not int raidPartySize)
        {
            return false;
        }

        return payload.Participants.Count == raidPartySize
            && HasCompleteRaidSlots(payload.Participants, raidPartySize)
            && HasConsistentSubPartyFields(payload.Participants, raidPartySize);
    }

    // A full raid of `size` dealers, each in its own roster slot 1..size — the ideal case the site accepts.
    private static (DataManager Dm, DpsLog Log) Raid(int size)
    {
        var dm = new DataManager();
        var contributors = new List<User>();
        var information = new Dictionary<int, DpsInformation>();
        var skillDetails = new Dictionary<int, Dictionary<string, AnalyzedSkill>>();
        var slots = new Dictionary<int, int>();

        for (int i = 1; i <= size; i++)
        {
            dm.SaveNickname(i, "P" + i, isExecutor: i == 1, server: 3, jobByte: 5);
            dm.SaveUserPower(i, 3000 + i);
            contributors.Add(dm.User(i)!);
            information[i] = new DpsInformation(1_000_000 - (i * 1000), 40_000, 100.0 / size, 80.0 / size);
            skillDetails[i] = new Dictionary<string, AnalyzedSkill>
            {
                ["11020001"] = new AnalyzedSkill { SkillCode = 11020001, Name = "강타", DamageAmount = 1_000_000, Times = 100 },
            };
            slots[i] = i;
        }

        var report = new DpsReport
        {
            Contributors = contributors,
            BattleStart = 1_000_000,
            BattleEnd = 1_030_000,
            Target = new MobInfo(100, new Mob(12345, "보스", true), remainHp: 0, maxHp: 1_000_000),
            Information = information,
            PartySlots = slots,
            PartyRosterSize = size,
        };
        return (dm, new DpsLog { Report = report, SkillDetails = skillDetails });
    }

    private static StatsUploadPayload Build(DataManager dm, DpsLog log)
    {
        var builder = new StatsPayloadBuilder(dm, publicCharacterProvider: () => false, clock: () => 1_700_000_000_000);
        return Assert.IsType<BuildResult.Payload>(builder.Build(log, "2.0.0", killConfirmed: true)).Value;
    }

    [Theory]
    [InlineData(8)]
    [InlineData(10)]
    public void A_complete_raid_roster_is_accepted_by_the_sites_rules(int size)
    {
        (DataManager dm, DpsLog log) = Raid(size);

        StatsUploadPayload payload = Build(dm, log);

        Assert.Equal(size, payload.Battle.PartySize);
        Assert.All(payload.Participants, p => Assert.NotNull(p.PartySlot));
        Assert.True(SubPartyKnown(payload), $"{size}-인 공대 payload was rejected by the site's rules");
    }

    [Fact]
    public void The_roster_size_travels_so_the_site_can_tell_a_raid_from_a_crowded_field_pull()
    {
        // The site currently decides "is this a 공대" from partySize, i.e. the dealer count — so a field boss
        // with seven people swinging at it gets labelled "7인 공대", and a raid where two members never touched
        // this boss doesn't get recognised as one. rosterSize answers that question directly.
        (DataManager dm, DpsLog log) = Raid(10);
        Assert.Equal(10, Build(dm, log).Battle.RosterSize);

        // No roster captured -> omitted rather than a misleading zero.
        (DataManager dm2, DpsLog log2) = Raid(10);
        log2.Report.PartyRosterSize = 0;
        Assert.Null(Build(dm2, log2).Battle.RosterSize);

        // And it is genuinely independent of the dealer count.
        (DataManager dm3, DpsLog log3) = Raid(10);
        log3.Report.PartyRosterSize = 10;
        log3.Report.Contributors = log3.Report.Contributors.Take(6).ToList();
        foreach (int id in log3.Report.Information.Keys.Where(k => k > 6).ToList())
        {
            log3.Report.Information.Remove(id);
        }

        StatsUploadPayload partial = Build(dm3, log3);
        Assert.Equal(6, partial.Battle.PartySize);
        Assert.Equal(10, partial.Battle.RosterSize);

        // Wire shape: camelCase, and absent (not null, not 0) when there was no roster — the site's zod
        // objects strip unknown keys, so an older site simply never sees it.
        Assert.Contains("\"rosterSize\":10", StatsJson.Serialize(partial.Battle));
        Assert.DoesNotContain("rosterSize", StatsJson.Serialize(Build(dm2, log2).Battle));
    }

    [Fact]
    public void The_old_hardcoded_party_number_would_have_been_rejected_for_an_eight_player_raid()
    {
        // Pins down the regression this fixed, so nobody re-derives partyNumber in the builder: the formula
        // (slot-1)/5+1 puts slot 5 in party 1, the site's 4+4 split puts it in party 2, and one disagreement
        // discards the split for the whole battle.
        (DataManager dm, DpsLog log) = Raid(8);
        StatsUploadPayload payload = Build(dm, log);

        List<StatsParticipantPayload> withOldFormula = payload.Participants
            .Select(p => p with { PartyNumber = p.PartySlot is int s ? ((s - 1) / 5) + 1 : null })
            .ToList();

        Assert.False(HasConsistentSubPartyFields(withOldFormula, 8));
        Assert.True(HasConsistentSubPartyFields(payload.Participants, 8)); // what we actually send now
    }
}

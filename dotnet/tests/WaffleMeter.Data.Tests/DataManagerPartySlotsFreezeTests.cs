using WaffleMeter.Capture;
using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>
/// Covers SaveBattleLog freezing the 0x9702 sub-party slots (uid -&gt; slot 1-8) onto the saved report so
/// the stats upload can tag party 1 (slots 1-4) vs party 2 (slots 5-8) for an 8-인 공대, faithfully even if
/// the upload is delayed and the live roster has since changed. Members without a recognized uid or with
/// slot 0 (header unmatched) are dropped.
/// </summary>
public sealed class DataManagerPartySlotsFreezeTests
{
    [Fact]
    public void SaveBattleLog_freezes_party_slots_matched_to_uids()
    {
        var dm = new DataManager { Clock = () => 1_000_000 };
        dm.SaveNickname(1, "Me", isExecutor: true, server: 2003, jobByte: 0);
        dm.SaveNickname(2, "Ally", isExecutor: false, server: 1019, jobByte: 0);
        dm.SavePartyRoster(new List<(string, int, int)>
        {
            ("Me", 2003, 2),       // -> uid 1, party 1
            ("Ally", 1019, 6),     // -> uid 2, party 2
            ("미인식", 2003, 4),    // no recognized uid -> dropped
            ("슬롯없음", 2003, 0),  // slot 0 (header unmatched) -> dropped
        });

        var report = new DpsReport
        {
            Contributors = new List<User> { dm.User(1)!, dm.User(2)! },
            Information = new Dictionary<int, DpsInformation> { [1] = new(1, 1, 1, 1), [2] = new(1, 1, 1, 1) },
        };

        DpsLog log = dm.SaveBattleLog(
            report,
            new Dictionary<int, Dictionary<string, AnalyzedSkill>>(),
            new Dictionary<int, List<OperatingData>>(),
            new List<OperatingData>());

        Assert.Equal(2, log.Report.PartySlots.Count);
        Assert.Equal(2, log.Report.PartySlots[1]);
        Assert.Equal(6, log.Report.PartySlots[2]);
    }

    [Fact]
    public void SaveBattleLog_freezes_empty_party_slots_when_no_roster()
    {
        var dm = new DataManager { Clock = () => 1_000_000 };
        dm.SaveNickname(1, "Me", isExecutor: true, server: 2003, jobByte: 0);

        var report = new DpsReport
        {
            Contributors = new List<User> { dm.User(1)! },
            Information = new Dictionary<int, DpsInformation> { [1] = new(1, 1, 1, 1) },
        };

        DpsLog log = dm.SaveBattleLog(
            report,
            new Dictionary<int, Dictionary<string, AnalyzedSkill>>(),
            new Dictionary<int, List<OperatingData>>(),
            new List<OperatingData>());

        Assert.Empty(log.Report.PartySlots);
    }
}

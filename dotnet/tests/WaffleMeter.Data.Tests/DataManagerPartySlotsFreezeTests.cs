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
    public void SaveBattleLog_keys_self_slot_to_current_executor_despite_stale_duplicates()
    {
        // The executor re-registers under a FRESH uid on every zone/instance load (0x3633), leaving stale
        // duplicate User entries with the same name+server (seen 5x in one real session). A plain
        // FindByNicknameAndServer would resolve the self's roster slot to a stale uid, so the actual
        // executor/uploader uid never gets its slot. The slot must key to the CURRENT executor (uid 300).
        var dm = new DataManager { Clock = () => 1_000_000 };
        dm.SaveNickname(100, "Me", isExecutor: true, server: 2003, jobByte: 0); // stale self (earlier zone)
        dm.SaveNickname(200, "Me", isExecutor: true, server: 2003, jobByte: 0); // stale self (another zone)
        dm.SaveNickname(300, "Me", isExecutor: true, server: 2003, jobByte: 0); // current executor
        dm.SaveNickname(2, "Ally", isExecutor: false, server: 1019, jobByte: 0);
        dm.SavePartyRoster(new List<(string, int, int)>
        {
            ("Me", 2003, 3),    // self, slot 3 -> must land on the current executor uid 300
            ("Ally", 1019, 6),  // 2nd party -> makes it a raid
        });

        var report = new DpsReport
        {
            Contributors = new List<User> { dm.User(300)!, dm.User(2)! }, // the executor fights under uid 300
            Information = new Dictionary<int, DpsInformation> { [300] = new(1, 1, 1, 1), [2] = new(1, 1, 1, 1) },
        };

        DpsLog log = dm.SaveBattleLog(
            report,
            new Dictionary<int, Dictionary<string, AnalyzedSkill>>(),
            new Dictionary<int, List<OperatingData>>(),
            new List<OperatingData>());

        Assert.Equal(3, log.Report.PartySlots[300]);   // current executor gets the slot
        Assert.False(log.Report.PartySlots.ContainsKey(100)); // stale selves do not capture it
        Assert.False(log.Report.PartySlots.ContainsKey(200));
        Assert.Equal(6, log.Report.PartySlots[2]);
    }

    [Fact]
    public void SaveBattleLog_keys_ally_slot_to_the_contributor_uid_despite_stale_duplicates()
    {
        // Same hazard for a NON-uploader: an ally seen under more than one uid. The slot must key to the uid
        // that actually dealt damage (the contributor uid 50), not a stale duplicate (uid 40).
        var dm = new DataManager { Clock = () => 1_000_000 };
        dm.SaveNickname(1, "Me", isExecutor: true, server: 2003, jobByte: 0);
        dm.SaveNickname(40, "Ally", isExecutor: false, server: 1019, jobByte: 0); // stale ally
        dm.SaveNickname(50, "Ally", isExecutor: false, server: 1019, jobByte: 0); // ally's combat uid
        dm.SavePartyRoster(new List<(string, int, int)>
        {
            ("Me", 2003, 2),
            ("Ally", 1019, 6),
        });

        var report = new DpsReport
        {
            Contributors = new List<User> { dm.User(1)!, dm.User(50)! },
            Information = new Dictionary<int, DpsInformation> { [1] = new(1, 1, 1, 1), [50] = new(1, 1, 1, 1) },
        };

        DpsLog log = dm.SaveBattleLog(
            report,
            new Dictionary<int, Dictionary<string, AnalyzedSkill>>(),
            new Dictionary<int, List<OperatingData>>(),
            new List<OperatingData>());

        Assert.Equal(6, log.Report.PartySlots[50]);    // contributor uid gets the slot
        Assert.False(log.Report.PartySlots.ContainsKey(40)); // stale ally does not capture it
        Assert.Equal(2, log.Report.PartySlots[1]);
    }

    [Fact]
    public void SaveBattleLog_falls_back_to_repository_for_non_damaging_party2_member()
    {
        // A party-2 member who didn't deal damage to THIS boss is not a contributor, but its slot (5-8) must
        // still be frozen so the upload's isRaid / sub-party detection fires. The repository fallback keeps it.
        var dm = new DataManager { Clock = () => 1_000_000 };
        dm.SaveNickname(1, "Me", isExecutor: true, server: 2003, jobByte: 0);
        dm.SaveNickname(2, "Ally", isExecutor: false, server: 1019, jobByte: 0);
        dm.SaveNickname(3, "Party2", isExecutor: false, server: 2005, jobByte: 0); // recognized but no damage
        dm.SavePartyRoster(new List<(string, int, int)>
        {
            ("Me", 2003, 1),
            ("Ally", 1019, 2),
            ("Party2", 2005, 7), // slot 7 -> party 2; must survive even though Party2 isn't a contributor
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

        Assert.Equal(7, log.Report.PartySlots[3]);   // party-2 slot kept -> isRaid stays detectable
        Assert.True(log.Report.PartySlots.Values.Any(s => s > 4));
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

    [Fact]
    public void Real_eight_player_raid_packet_fills_all_eight_slots_including_the_uploader_with_a_stale_self()
    {
        // End-to-end: the verbatim 6/11 무스펠의 성배 8-인 공대 0x9702 packet (콘팡 = the local player at slot 2)
        // parsed through StreamProcessor into a real DataManager, with the self carrying stale duplicate uids from
        // prior zone loads. Every participant — incl. the uploader — must get its 1-8 slot keyed to the live uid.
        var dm = new DataManager { Clock = () => 1_000_000 };
        var proc = new StreamProcessor(null, dm, null);

        dm.SaveNickname(11, "에이", isExecutor: false, server: 2003, jobByte: 0);
        dm.SaveNickname(101, "콘팡", isExecutor: true, server: 2003, jobByte: 0); // stale self (prior zone)
        dm.SaveNickname(102, "콘팡", isExecutor: true, server: 2003, jobByte: 0); // stale self (prior zone)
        dm.SaveNickname(103, "콘팡", isExecutor: true, server: 2003, jobByte: 0); // current executor / uploader
        dm.SaveNickname(13, "꼬북", isExecutor: false, server: 1019, jobByte: 0);
        dm.SaveNickname(14, "몽몽", isExecutor: false, server: 2003, jobByte: 0);
        dm.SaveNickname(15, "유꾸", isExecutor: false, server: 2003, jobByte: 0);
        dm.SaveNickname(16, "용성", isExecutor: false, server: 2001, jobByte: 0);
        dm.SaveNickname(17, "무좀", isExecutor: false, server: 2003, jobByte: 0);
        dm.SaveNickname(18, "주리오", isExecutor: false, server: 2005, jobByte: 0);

        proc.OnPacketReceived(RealEightPlayerRaidRoster(), 1000); // -> SavePartyRoster with slots 1..8

        var contributors = new List<User>
        {
            dm.User(103)!, dm.User(11)!, dm.User(13)!, dm.User(14)!,
            dm.User(15)!, dm.User(16)!, dm.User(17)!, dm.User(18)!,
        };
        var report = new DpsReport
        {
            Contributors = contributors,
            Information = contributors.ToDictionary(u => u.Id, _ => new DpsInformation(1, 1, 1, 1)),
        };

        DpsLog log = dm.SaveBattleLog(
            report,
            new Dictionary<int, Dictionary<string, AnalyzedSkill>>(),
            new Dictionary<int, List<OperatingData>>(),
            new List<OperatingData>());

        // All 8 sub-party slots present and keyed to the live combat uids; the uploader 콘팡 (current uid 103)
        // gets slot 2, NOT a stale self uid — so the web's all-or-nothing {1..8} requirement is satisfied.
        Assert.Equal(8, log.Report.PartySlots.Count);
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7, 8 }, log.Report.PartySlots.Values.OrderBy(s => s).ToArray());
        Assert.Equal(2, log.Report.PartySlots[103]);
        Assert.False(log.Report.PartySlots.ContainsKey(101));
        Assert.False(log.Report.PartySlots.ContainsKey(102));
    }

    // Verbatim decompressed 0x9702 packet from the 6/11 무스펠의 성배 8-인 공대 capture (20260611-201458):
    // party 1 = 에이/콘팡/꼬북/몽몽 (slots 1-4), party 2 = 유꾸/용성/무좀/주리오 (slots 5-8). 콘팡 = 본인.
    private static byte[] RealEightPlayerRaidRoster() => Hex(
        "E9 03 02 97 D4 7D 07 00 06 EC 84 B1 EC 97 AD 08 " +
        "F5 75 09 00 00 03 B1 0D 00 00 00 00 D3 07 FF 02 " +
        "03 08 7A 01 B1 0D 00 00 00 00 D3 07 06 EC 97 90 " +
        "EC 9D B4 0C 00 00 00 2D 00 00 00 C1 13 00 00 D3 " +
        "07 A1 0F 04 92 22 0A 00 00 00 00 00 01 02 00 00 " +
        "00 00 1C 02 00 00 00 00 00 00 7A 02 AC B3 00 00 " +
        "00 00 D3 07 06 EC BD 98 ED 8C A1 10 00 00 00 2D " +
        "00 00 00 26 13 00 00 D3 07 A1 0F 04 6C 02 0A 00 " +
        "00 00 00 00 01 02 00 00 00 00 82 00 00 00 00 00 " +
        "00 00 7A 03 3C 79 00 00 00 00 FB 03 06 EA BC AC " +
        "EB B6 81 1A 00 00 00 2D 00 00 00 D5 14 00 00 FB " +
        "03 A1 0F 04 4E EF 0A 00 00 00 00 00 01 02 00 00 " +
        "00 00 5C 01 00 00 00 00 00 00 7A 04 14 04 00 00 " +
        "00 00 D3 07 06 EB AA BD EB AA BD 20 00 00 00 2D " +
        "00 00 00 CD 12 00 00 D3 07 A1 0F 04 34 2B 09 00 " +
        "00 00 00 00 01 02 00 00 00 00 4A 01 00 00 00 00 " +
        "00 00 7A 05 B1 BB 00 00 00 00 D3 07 06 EC 9C A0 " +
        "EA BE B8 14 00 00 00 2D 00 00 00 02 14 00 00 D3 " +
        "07 A1 0F 04 51 18 0A 00 00 00 00 00 01 02 00 00 " +
        "00 00 B4 00 00 00 00 00 00 00 7A 06 9E 22 00 00 " +
        "00 00 D1 07 06 EC 9A A9 EC 84 B1 14 00 00 00 2D " +
        "00 00 00 60 13 00 00 D1 07 A1 0F 04 92 B5 09 00 " +
        "00 00 00 00 01 02 00 00 00 00 1F 01 00 00 00 00 " +
        "00 00 7A 07 D0 9C 00 00 00 00 D3 07 06 EB AC B4 " +
        "EC A2 80 24 00 00 00 2D 00 00 00 19 13 00 00 D3 " +
        "07 A1 0F 04 86 82 09 00 00 00 00 00 01 02 00 00 " +
        "00 00 6E 00 00 00 00 00 00 00 7A 08 A8 8D 00 00 " +
        "00 00 D5 07 09 EC A3 BC EB A6 AC EC 98 A4 20 00 " +
        "00 00 2D 00 00 00 0A 13 00 00 01 D5 07 A1 0F 04 " +
        "D5 3E 09 00 00 00 00 00 01 02 00 00 00 00 A5 00 " +
        "00 00 00 00 00 00 09");

    private static byte[] Hex(string hex)
    {
        string[] tokens = hex.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        var bytes = new byte[tokens.Length];
        for (int i = 0; i < tokens.Length; i++)
        {
            bytes[i] = Convert.ToByte(tokens[i], 16);
        }

        return bytes;
    }
}

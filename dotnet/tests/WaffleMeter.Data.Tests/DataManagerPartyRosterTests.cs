using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>
/// Covers DataManager.SavePartyRoster / PartyRoster — the authoritative pre-combat party source. The
/// 0x9702 roster packet gives member (nickname, server); we match each to a known uid by name+server so
/// the meter shows the party on dungeon entry, before any combat.
/// </summary>
public sealed class DataManagerPartyRosterTests
{
    [Fact]
    public void PartyRoster_matches_members_to_uids_by_name_and_server_executor_first()
    {
        long now = 1_000_000;
        var dm = new DataManager { Clock = () => now };
        dm.SaveNickname(1, "플러시", isExecutor: true, server: 2003, jobByte: 0);
        dm.SaveUserPower(1, 3000);
        dm.SaveNickname(2, "Wildz", isExecutor: false, server: 1014, jobByte: 0);
        dm.SaveUserPower(2, 5000);

        dm.SavePartyRoster(new List<(string, int)> { ("Wildz", 1014), ("플러시", 2003), ("아직없음", 1010) });

        IReadOnlyList<User> roster = dm.PartyRoster(300_000);

        Assert.Equal(2, roster.Count);  // 아직없음 has no uid yet -> excluded
        Assert.Equal(1, roster[0].Id);  // executor first, even with lower power
        Assert.Equal(2, roster[1].Id);
    }

    [Fact]
    public void PartyRoster_is_empty_when_the_snapshot_is_stale()
    {
        long now = 1_000_000;
        var dm = new DataManager { Clock = () => now };
        dm.SaveNickname(1, "플러시", isExecutor: true, server: 2003, jobByte: 0);
        dm.SavePartyRoster(new List<(string, int)> { ("플러시", 2003) });

        now += 300_001; // past the freshness window
        Assert.Empty(dm.PartyRoster(300_000));
    }

    [Fact]
    public void PartyRoster_is_empty_when_no_snapshot()
    {
        var dm = new DataManager { Clock = () => 1_000_000 };
        Assert.Empty(dm.PartyRoster(300_000));
    }
}

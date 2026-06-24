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

        dm.SavePartyRoster(new List<(string, int, int)> { ("Wildz", 1014, 2), ("플러시", 2003, 1), ("아직없음", 1010, 3) });

        IReadOnlyList<User> roster = dm.PartyRoster(300_000);

        Assert.Equal(2, roster.Count);  // 아직없음 has no uid yet -> excluded
        Assert.Equal(1, roster[0].Id);  // executor first, even with lower power
        Assert.Equal(2, roster[1].Id);
    }

    [Fact]
    public void PartyRoster_resolves_self_to_the_live_executor_despite_stale_duplicates()
    {
        // The self re-registers under a fresh uid on every zone load (0x3633), leaving stale name+server
        // duplicates. The preview's own row must be the CURRENT executor (uid 300), not a stale self (100/200),
        // or it loses self-recognition (IsExecutor=false on the demoted prior selves).
        long now = 1_000_000;
        var dm = new DataManager { Clock = () => now };
        dm.SaveNickname(100, "플러시", isExecutor: true, server: 2003, jobByte: 0); // stale self
        dm.SaveNickname(200, "플러시", isExecutor: true, server: 2003, jobByte: 0); // stale self
        dm.SaveNickname(300, "플러시", isExecutor: true, server: 2003, jobByte: 0); // current executor
        dm.SaveNickname(2, "Wildz", isExecutor: false, server: 1014, jobByte: 0);
        dm.SavePartyRoster(new List<(string, int, int)> { ("Wildz", 1014, 2), ("플러시", 2003, 1) });

        IReadOnlyList<User> roster = dm.PartyRoster(300_000);

        Assert.Equal(2, roster.Count);
        Assert.Equal(300, roster[0].Id);    // current executor, first
        Assert.True(roster[0].IsExecutor);
        Assert.Equal(2, roster[1].Id);
    }

    [Fact]
    public void PartyRoster_is_empty_when_the_snapshot_is_stale()
    {
        long now = 1_000_000;
        var dm = new DataManager { Clock = () => now };
        dm.SaveNickname(1, "플러시", isExecutor: true, server: 2003, jobByte: 0);
        dm.SavePartyRoster(new List<(string, int, int)> { ("플러시", 2003, 1) });

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

using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>
/// Covers DataManager.RecentRoster — the pre-combat party preview source. It surfaces recently-seen named
/// players (executor always included) so the meter can show the party on dungeon entry, and ages out
/// players from a previous zone (whose nickname snapshot isn't re-seen).
/// </summary>
public sealed class DataManagerRosterTests
{
    [Fact]
    public void RecentRoster_includes_recently_seen_named_players_executor_first_then_power()
    {
        long now = 1_000_000;
        var dm = new DataManager { Clock = () => now };
        dm.SaveNickname(1, "Me", isExecutor: true, server: 1001, jobByte: 13);
        dm.SaveUserPower(1, 3000);
        dm.SaveNickname(2, "Ally", isExecutor: false, server: 1001, jobByte: 29);
        dm.SaveUserPower(2, 5000);

        IReadOnlyList<User> roster = dm.RecentRoster(90_000);

        Assert.Equal(2, roster.Count);
        Assert.Equal(1, roster[0].Id); // executor first, even with lower power
        Assert.Equal(2, roster[1].Id);
    }

    [Fact]
    public void RecentRoster_ages_out_stale_non_executor_but_keeps_executor()
    {
        long now = 1_000_000;
        var dm = new DataManager { Clock = () => now };
        dm.SaveNickname(1, "Me", isExecutor: true, server: 1001, jobByte: 13);
        dm.SaveNickname(2, "PreviousZoneAlly", isExecutor: false, server: 1001, jobByte: 29);

        now += 91_000; // both last seen 91s ago, past the 90s window
        IReadOnlyList<User> roster = dm.RecentRoster(90_000);

        Assert.Single(roster);
        Assert.Equal(1, roster[0].Id); // executor always shown; stale ally dropped
    }

    [Fact]
    public void RecentRoster_is_empty_before_any_player_is_seen()
    {
        var dm = new DataManager { Clock = () => 1_000_000 };
        Assert.Empty(dm.RecentRoster(90_000));
    }
}

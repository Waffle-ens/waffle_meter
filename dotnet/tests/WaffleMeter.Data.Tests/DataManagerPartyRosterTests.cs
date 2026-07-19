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
    public void A_partial_snapshot_does_not_shrink_a_fuller_roster()
    {
        // A 0x9702 snapshot can arrive PARTIAL (byte-scan miss / incremental re-broadcast). A naive Clear+Replace
        // then shrinks a complete roster (observed live 5→4→3→2); the subset guard keeps the fuller one.
        long now = 1_000_000;
        var dm = new DataManager { Clock = () => now };
        dm.SavePartyRoster(new List<(string, int, int)> { ("A", 1, 1), ("B", 1, 2), ("C", 1, 3), ("D", 1, 4), ("E", 1, 5) });
        dm.SavePartyRoster(new List<(string, int, int)> { ("A", 1, 1), ("B", 1, 2) }); // strict subset — ignored

        Assert.Equal(5, dm.PartyMemberIdentities(300_000).Count);
    }

    [Fact]
    public void A_snapshot_with_a_new_member_replaces_the_roster()
    {
        long now = 1_000_000;
        var dm = new DataManager { Clock = () => now };
        dm.SavePartyRoster(new List<(string, int, int)> { ("A", 1, 1), ("B", 1, 2) });
        dm.SavePartyRoster(new List<(string, int, int)> { ("A", 1, 1), ("X", 1, 3) }); // a NEW member -> replaces

        IReadOnlyList<(string Nickname, int Server)> ids = dm.PartyMemberIdentities(300_000);
        Assert.Equal(2, ids.Count);
        Assert.Contains(ids, m => m.Nickname == "X");
        Assert.DoesNotContain(ids, m => m.Nickname == "B");
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

    [Fact]
    public void PartyRoster_is_cleared_on_a_character_switch()
    {
        // 콘팡 connects, a roster snapshot is taken (so the preview surfaces it), then the user switches to a
        // DIFFERENT character 마이농 (same server). The previous character's roster must not linger.
        long now = 1_000_000;
        var dm = new DataManager { Clock = () => now };
        dm.SaveNickname(1, "콘팡", isExecutor: true, server: 2003, jobByte: 0);
        dm.SavePartyRoster(new List<(string, int, int)> { ("콘팡", 2003, 1) });
        Assert.Single(dm.PartyRoster(300_000)); // sanity: 콘팡 previews before the switch

        dm.SaveNickname(2, "마이농", isExecutor: true, server: 2003, jobByte: 0); // switch

        Assert.Empty(dm.PartyRoster(300_000)); // stale 콘팡 roster dropped
    }

    [Fact]
    public void PartyRoster_is_cleared_on_a_cross_server_same_name_switch()
    {
        // Same nickname on a DIFFERENT (known) server = a cross-server alt switch, also a real switch.
        long now = 1_000_000;
        var dm = new DataManager { Clock = () => now };
        dm.SaveNickname(1, "콘팡", isExecutor: true, server: 2003, jobByte: 0);
        dm.SavePartyRoster(new List<(string, int, int)> { ("콘팡", 2003, 1) });

        dm.SaveNickname(2, "콘팡", isExecutor: true, server: 2004, jobByte: 0); // cross-server alt

        Assert.Empty(dm.PartyRoster(300_000));
    }

    [Fact]
    public void PartyRoster_survives_a_same_character_reinstance_same_server()
    {
        // Same character re-instancing under a fresh uid on a zone load (town -> dungeon) must KEEP the roster
        // that was saved first — the party about to form in the new zone is still ours. (The existing
        // self-resolution test saves the roster LAST, so it does not exercise this clear-then-read order.)
        long now = 1_000_000;
        var dm = new DataManager { Clock = () => now };
        dm.SaveNickname(1, "콘팡", isExecutor: true, server: 2003, jobByte: 0);
        dm.SavePartyRoster(new List<(string, int, int)> { ("콘팡", 2003, 1) });

        dm.SaveNickname(2, "콘팡", isExecutor: true, server: 2003, jobByte: 0); // re-instance, same identity

        IReadOnlyList<User> roster = dm.PartyRoster(300_000);
        Assert.Single(roster);
        Assert.Equal("콘팡", roster[0].Nickname);
    }

    [Fact]
    public void PartyRoster_survives_a_same_character_reinstance_with_unknown_server()
    {
        // A truncated 0x3633 own-load yields Server=-1 (User ctor default). A same-name re-instance whose new
        // load is truncated must NOT be read as a cross-server switch — the naive oldServer != newServer check
        // would false-clear a legitimate dungeon party preview here. The server>0 guard preserves it.
        long now = 1_000_000;
        var dm = new DataManager { Clock = () => now };
        dm.SaveNickname(1, "콘팡", isExecutor: true, server: 2003, jobByte: 0);
        dm.SavePartyRoster(new List<(string, int, int)> { ("콘팡", 2003, 1) });

        dm.SaveNickname(2, "콘팡", isExecutor: true, server: -1, jobByte: 0); // truncated re-instance

        IReadOnlyList<User> roster = dm.PartyRoster(300_000);
        Assert.Single(roster);
        Assert.Equal("콘팡", roster[0].Nickname);
    }

    [Fact]
    public void ExecutorIdentityChanged_fires_once_on_a_switch_and_never_on_a_reinstance()
    {
        long now = 1_000_000;
        var dm = new DataManager { Clock = () => now };
        int fired = 0;
        dm.ExecutorIdentityChanged += () => fired++;

        dm.SaveNickname(1, "콘팡", isExecutor: true, server: 2003, jobByte: 0);  // first connect (no prior executor)
        dm.SaveNickname(2, "콘팡", isExecutor: true, server: 2003, jobByte: 0);  // re-instance, same identity
        dm.SaveNickname(3, "콘팡", isExecutor: true, server: -1, jobByte: 0);    // truncated re-instance
        Assert.Equal(0, fired);

        dm.SaveNickname(4, "마이농", isExecutor: true, server: 2003, jobByte: 0); // real switch
        Assert.Equal(1, fired);
    }
}

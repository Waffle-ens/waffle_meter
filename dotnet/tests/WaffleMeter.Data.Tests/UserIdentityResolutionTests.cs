using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>
/// Covers UserRepository's bounded, newest-first identity resolution (via DataManager, the real entry point).
/// The self re-registers under a fresh uid every zone load (0x3633), leaving stale name+server duplicates;
/// FindByNicknameAndServer must return the live (newest) instance, and each identity is capped to 3 uids so the
/// long-dead ones are evicted — but never the current executor, and a reused uid re-groups to its new identity.
/// </summary>
public sealed class UserIdentityResolutionTests
{
    [Fact]
    public void FindUser_returns_the_newest_uid_among_stale_self_duplicates()
    {
        var dm = new DataManager();
        dm.SaveNickname(100, "Me", isExecutor: true, server: 2003, jobByte: 0); // stale
        dm.SaveNickname(200, "Me", isExecutor: true, server: 2003, jobByte: 0); // stale
        dm.SaveNickname(300, "Me", isExecutor: true, server: 2003, jobByte: 0); // current

        Assert.Equal(300, dm.FindUserByNicknameAndServer("Me", 2003)!.Id);
    }

    [Fact]
    public void Identity_is_capped_to_three_uids_evicting_the_oldest()
    {
        var dm = new DataManager();
        // Four same-identity registrations (non-executor so the executor guard isn't in play): the oldest (1) is
        // evicted, the newest three survive, and the lookup returns the newest.
        dm.SaveNickname(1, "Ally", isExecutor: false, server: 1019, jobByte: 0);
        dm.SaveNickname(2, "Ally", isExecutor: false, server: 1019, jobByte: 0);
        dm.SaveNickname(3, "Ally", isExecutor: false, server: 1019, jobByte: 0);
        dm.SaveNickname(4, "Ally", isExecutor: false, server: 1019, jobByte: 0);

        Assert.Null(dm.User(1));            // oldest evicted
        Assert.NotNull(dm.User(2));
        Assert.NotNull(dm.User(4));
        Assert.Equal(4, dm.FindUserByNicknameAndServer("Ally", 1019)!.Id);
    }

    [Fact]
    public void Eviction_never_drops_the_current_executor_even_when_it_is_oldest()
    {
        var dm = new DataManager();
        dm.SaveNickname(1, "Me", isExecutor: true, server: 2003, jobByte: 0);  // executor, oldest in the group
        dm.SaveNickname(2, "Me", isExecutor: false, server: 2003, jobByte: 0); // newer same-identity entries
        dm.SaveNickname(3, "Me", isExecutor: false, server: 2003, jobByte: 0);
        dm.SaveNickname(4, "Me", isExecutor: false, server: 2003, jobByte: 0); // pushes the group past the cap

        Assert.Equal(1, dm.ExecutorId());
        Assert.NotNull(dm.User(1));   // executor kept despite being the oldest
        Assert.Null(dm.User(2));      // the next-oldest is evicted instead
        Assert.NotNull(dm.User(4));
    }

    [Fact]
    public void FindUser_with_a_blank_name_returns_null_even_with_a_provisional_user_present()
    {
        var dm = new DataManager();
        dm.EnsureUser(999); // provisional damaging actor: null nickname, no server yet
        Assert.Null(dm.FindUserByNicknameAndServer("", 2003));
        Assert.Null(dm.FindUserByNicknameAndServer("   ", 2003));
    }

    [Fact]
    public void Reused_uid_taking_a_new_identity_is_regrouped_off_the_old_name()
    {
        var dm = new DataManager();
        dm.SaveNickname(50, "OldName", isExecutor: false, server: 1019, jobByte: 0);
        dm.SaveNickname(50, "NewName", isExecutor: false, server: 1019, jobByte: 0); // same uid, different player

        Assert.Null(dm.FindUserByNicknameAndServer("OldName", 1019)); // no longer resolvable under the old name
        Assert.Equal(50, dm.FindUserByNicknameAndServer("NewName", 1019)!.Id);
    }
}

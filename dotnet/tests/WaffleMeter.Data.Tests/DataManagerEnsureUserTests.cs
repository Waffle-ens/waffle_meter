using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>
/// Covers DataManager.EnsureUser — the provisional-user seam behind the 난입 (mid-join) own-DPS fix. When a
/// damaging actor's identity packet (0x3633 own / 0x3645 other) hasn't arrived yet, DpsCalculator
/// provisionally registers the actor so its DPS row shows; the SAME object must then be enriched in place
/// when the real nickname/executor packet finally lands, so naming + self-color reconcile automatically.
/// </summary>
public sealed class DataManagerEnsureUserTests
{
    [Fact]
    public void EnsureUser_creates_and_persists_a_bare_user()
    {
        var dm = new DataManager();

        User u = dm.EnsureUser(13601);

        Assert.Same(u, dm.User(13601));          // persisted + retrievable
        Assert.Null(u.Nickname);                  // bare: no identity yet
        Assert.Equal(-1, u.Server);
        Assert.Null(u.Job);
        Assert.False(u.IsExecutor);
        Assert.Equal(0, u.Power);
    }

    [Fact]
    public void EnsureUser_is_idempotent_and_returns_the_same_object()
    {
        var dm = new DataManager();

        User first = dm.EnsureUser(13601);
        User second = dm.EnsureUser(13601);

        Assert.Same(first, second);
    }

    [Fact]
    public void EnsureUser_does_not_clobber_an_already_known_user()
    {
        var dm = new DataManager();
        dm.SaveNickname(13601, "플러시", isExecutor: false, server: 2003, jobByte: 0);

        User u = dm.EnsureUser(13601);

        Assert.Equal("플러시", u.Nickname); // existing identity preserved
        Assert.Equal(2003, u.Server);
    }

    [Fact]
    public void A_later_SaveNickname_enriches_the_same_provisional_object_in_place()
    {
        var dm = new DataManager();
        // Provisional registration during a fight (executor 0x3633 not here yet).
        User provisional = dm.EnsureUser(13601);

        // 0x3633 finally arrives: same uid, now named + executor.
        dm.SaveNickname(13601, "플러시", isExecutor: true, server: 2003, jobByte: 32); // 32 -> CLERIC

        // The reference the live row holds reflects the enrichment (no second object).
        Assert.Same(provisional, dm.User(13601));
        Assert.Equal("플러시", provisional.Nickname);
        Assert.Equal(2003, provisional.Server);
        Assert.Equal(JobClass.CLERIC, provisional.Job);
        Assert.True(provisional.IsExecutor);
        Assert.Equal(13601, dm.ExecutorId()); // self-color now resolves to this uid
    }
}

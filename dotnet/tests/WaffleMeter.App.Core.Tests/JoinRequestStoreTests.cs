using WaffleMeter.App.Core;
using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

/// <summary>
/// Spec for <see cref="JoinRequestStore"/>: dedupe-by-requester, remove/refuse/clear semantics, the
/// 20s staleness filter in Snapshot, and Changed firing — mirrors the web useJoinRequestStore.
/// </summary>
public class JoinRequestStoreTests
{
    private static JoinRequestUser User(int requester, long arrivedAt, int power = 0) => new()
    {
        Requester = requester,
        Nickname = $"u{requester}",
        ArrivedAt = arrivedAt,
        Power = power,
    };

    [Fact]
    public void Add_dedupes_by_requester_newest_wins()
    {
        var store = new JoinRequestStore(() => 1000);
        store.Add(User(1, 100, power: 10));
        store.Add(User(1, 200, power: 99)); // same requester -> replace

        var snap = store.Snapshot();
        Assert.Single(snap);
        Assert.Equal(99, snap[0].Power);
        Assert.Equal(200, snap[0].ArrivedAt);
    }

    [Fact]
    public void Snapshot_is_newest_first()
    {
        var store = new JoinRequestStore(() => 1000);
        store.Add(User(1, 100));
        store.Add(User(2, 300));
        store.Add(User(3, 200));

        var snap = store.Snapshot();
        Assert.Equal([2, 3, 1], snap.Select(r => r.Requester));
    }

    [Fact]
    public void Snapshot_drops_entries_older_than_20s()
    {
        long now = 100_000;
        var store = new JoinRequestStore(() => now);
        store.Add(User(1, now - 25_000)); // stale (>20s)
        store.Add(User(2, now - 5_000));  // fresh

        var snap = store.Snapshot();
        Assert.Single(snap);
        Assert.Equal(2, snap[0].Requester);
    }

    [Fact]
    public void Remove_drops_by_requester()
    {
        var store = new JoinRequestStore(() => 1000);
        store.Add(User(1, 100));
        store.Add(User(2, 200));
        store.Remove(1);

        Assert.Equal([2], store.Snapshot().Select(r => r.Requester));
    }

    [Fact]
    public void RefuseOldest_drops_min_arrivedAt()
    {
        var store = new JoinRequestStore(() => 1000);
        store.Add(User(1, 300));
        store.Add(User(2, 100)); // oldest
        store.Add(User(3, 200));
        store.RefuseOldest();

        Assert.DoesNotContain(2, store.Snapshot().Select(r => r.Requester));
        Assert.Equal(2, store.Snapshot().Count);
    }

    [Fact]
    public void ClearAll_empties()
    {
        var store = new JoinRequestStore(() => 1000);
        store.Add(User(1, 100));
        store.Add(User(2, 200));
        store.ClearAll();

        Assert.Empty(store.Snapshot());
    }

    [Fact]
    public void Changed_fires_on_mutation()
    {
        var store = new JoinRequestStore(() => 1000);
        int fired = 0;
        store.Changed += () => fired++;

        store.Add(User(1, 100));     // +1
        store.Add(User(1, 150));     // +1 (replace)
        store.Remove(1);             // +1
        store.Remove(1);             // no-op (already gone) -> no fire
        store.RefuseOldest();        // no-op (empty) -> no fire
        store.ClearAll();            // no-op (empty) -> no fire

        Assert.Equal(3, fired);
    }
}

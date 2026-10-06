using WaffleMeter.App.Core;
using WaffleMeter.Capture;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

/// <summary>
/// Spec for the session currency ledger: how item-key stacks become per-container balances, which balances it may
/// claim to know, and when they may be filed under a character. The numbers are the 2026-10-07 서버 창고
/// experiment's (밀피@2003): 각인 13,000 / 키나 4,454,882 / 서버 창고 325,000,000 / AP 25,611 / 몽환 18,615 /
/// 극복 3,000, then 출고 (inventory 329,454,882, warehouse 0) and 입고 (back to 4,454,882 and 325,000,000 under a
/// new key).
/// </summary>
public sealed class CurrencyLedgerTests
{
    private const long SnapshotAt = 1_791_298_889_113;  // 00:01:29.113 — 0x5611
    private const long IdentityAt = 1_791_298_896_306;  // 00:01:36.306 — 0x3633, 7.19 s later
    private const long WithdrawAt = 1_791_298_909_309;  // 00:01:49.309
    private const long DepositAt = 1_791_298_913_809;   // 00:01:53.809

    private const long InventoryKey = 0x00026E00000B0327;
    private const long OldWarehouseKey = 0x0001EA000017884D;
    private const long NewWarehouseKey = 0x0002400000128B82;

    private static IReadOnlyList<CurrencyItem> TenSevenSnapshot() =>
    [
        new(0x20300001697A3, CurrencyItemParser.AbyssPointId, 25_611, 1),
        new(0x21000000B0FAD, CurrencyItemParser.DreamShardId, 18_615, 1),
        new(0x26300000F8176, CurrencyItemParser.TrialMarkId, 3_000, 1),
        new(InventoryKey, CurrencyItemParser.KinaId, 4_454_882, 1),
        new(0x187000018A1B0, CurrencyItemParser.BoundKinaId, 13_000, 1),
        new(OldWarehouseKey, CurrencyItemParser.KinaId, 325_000_000, 2),
    ];

    private static IReadOnlyList<CurrencyItemChange> Withdraw() =>
    [
        new(ItemChangeType.Update, new CurrencyItem(InventoryKey, CurrencyItemParser.KinaId, 329_454_882, 1)),
        new(ItemChangeType.Remove, new CurrencyItem(OldWarehouseKey, CurrencyItemParser.KinaId, 0, 2)),
    ];

    private static IReadOnlyList<CurrencyItemChange> Deposit() =>
    [
        new(ItemChangeType.Update, new CurrencyItem(InventoryKey, CurrencyItemParser.KinaId, 4_454_882, 1)),
        new(ItemChangeType.Add, new CurrencyItem(NewWarehouseKey, CurrencyItemParser.KinaId, 325_000_000, 2)),
    ];

    private static long? Balance(CurrencyLedger ledger, string slug)
    {
        CurrencyInfo info = CurrencyCatalog.BySlug(slug)!.Value;
        foreach (CurrencyBalance b in ledger.KnownBalances())
        {
            if (b.ItemId == info.ItemId && b.Container == info.Container)
            {
                return b.Count;
            }
        }

        return null;
    }

    [Fact]
    public void An_exact_snapshot_states_every_balance_and_absent_means_zero()
    {
        var ledger = new CurrencyLedger();
        ledger.ApplySnapshot(TenSevenSnapshot(), exact: true, SnapshotAt);

        Assert.Equal(13_000, Balance(ledger, CurrencyCatalog.BoundKina));
        Assert.Equal(4_454_882, Balance(ledger, CurrencyCatalog.Kina));
        Assert.Equal(325_000_000, Balance(ledger, CurrencyCatalog.ServerStorageKina));
        Assert.Equal(25_611, Balance(ledger, CurrencyCatalog.AbyssPoint));
        Assert.Equal(18_615, Balance(ledger, CurrencyCatalog.DreamShard));
        Assert.Equal(3_000, Balance(ledger, CurrencyCatalog.TrialMark));

        // No 캐릭터 창고 stack in the dump: the game omits empty stacks, so a complete list makes that a zero.
        Assert.Equal(0, Balance(ledger, CurrencyCatalog.CharacterStorageKina));
        Assert.Equal(CurrencyCatalog.All.Count, ledger.KnownBalances().Count);
    }

    /// <summary>The experiment, end to end. Withdrawing removes the warehouse stack and raises the inventory one;
    /// depositing creates a NEW warehouse key. Keyed by item key and summed per container, both round trips land
    /// exactly.</summary>
    [Fact]
    public void Withdraw_and_deposit_survive_the_item_key_churn()
    {
        var ledger = new CurrencyLedger();
        ledger.ApplySnapshot(TenSevenSnapshot(), exact: true, SnapshotAt);

        ledger.ApplyChanges(Withdraw(), WithdrawAt);
        Assert.Equal(329_454_882, Balance(ledger, CurrencyCatalog.Kina));
        Assert.Equal(0, Balance(ledger, CurrencyCatalog.ServerStorageKina));

        ledger.ApplyChanges(Deposit(), DepositAt);
        Assert.Equal(4_454_882, Balance(ledger, CurrencyCatalog.Kina));
        Assert.Equal(325_000_000, Balance(ledger, CurrencyCatalog.ServerStorageKina));

        // Nothing else moved.
        Assert.Equal(13_000, Balance(ledger, CurrencyCatalog.BoundKina));
        Assert.Equal(25_611, Balance(ledger, CurrencyCatalog.AbyssPoint));
    }

    /// <summary>A change stamps only the balances it touched — the store's "never older over newer" rule needs
    /// each balance's own observation time, not the filing time.</summary>
    [Fact]
    public void Each_balance_carries_the_time_it_was_last_stated()
    {
        var ledger = new CurrencyLedger();
        ledger.ApplySnapshot(TenSevenSnapshot(), exact: true, SnapshotAt);
        ledger.ApplyChanges(Withdraw(), WithdrawAt);

        IReadOnlyList<CurrencyBalance> balances = ledger.KnownBalances();
        Assert.Equal(WithdrawAt, balances.Single(b => b is { ItemId: CurrencyItemParser.KinaId, Container: 1 }).ObservedAtMs);
        Assert.Equal(WithdrawAt, balances.Single(b => b is { ItemId: CurrencyItemParser.KinaId, Container: 2 }).ObservedAtMs);
        Assert.Equal(SnapshotAt, balances.Single(b => b.ItemId == CurrencyItemParser.AbyssPointId).ObservedAtMs);
    }

    /// <summary>A meter started mid-session sees no snapshot until the next world entry. Changes still fill in the
    /// balances they touch — including the warehouse emptied by a stack the ledger never saw — and everything else
    /// stays unknown rather than being reported as zero.</summary>
    [Fact]
    public void Without_a_snapshot_only_the_balances_a_change_touched_are_known()
    {
        var ledger = new CurrencyLedger();
        ledger.ApplyChanges(Withdraw(), WithdrawAt);

        Assert.Equal(329_454_882, Balance(ledger, CurrencyCatalog.Kina));
        Assert.Equal(0, Balance(ledger, CurrencyCatalog.ServerStorageKina));
        Assert.Null(Balance(ledger, CurrencyCatalog.BoundKina));
        Assert.Null(Balance(ledger, CurrencyCatalog.AbyssPoint));
        Assert.Equal(2, ledger.KnownBalances().Count);

        Assert.True(ledger.TryTakeFiling(identityAtMs: 0, nowMs: WithdrawAt, out IReadOnlyList<CurrencyBalance> filed));
        Assert.Equal(2, filed.Count);
    }

    [Fact]
    public void Remove_drops_the_stack()
    {
        var ledger = new CurrencyLedger();
        ledger.ApplySnapshot(TenSevenSnapshot(), exact: true, SnapshotAt);
        ledger.ApplyChanges(
            [new CurrencyItemChange(ItemChangeType.Remove, new CurrencyItem(0x26300000F8176, CurrencyItemParser.TrialMarkId, 0, 1))],
            WithdrawAt);

        Assert.Equal(0, Balance(ledger, CurrencyCatalog.TrialMark));
    }

    /// <summary>The fallback snapshot vouches only for what its scan found. Everything else is UNKNOWN — never
    /// zero — so a patch that reassigns the opcode cannot wipe a balance it simply failed to find.</summary>
    [Fact]
    public void A_fallback_snapshot_never_states_what_it_did_not_find()
    {
        var ledger = new CurrencyLedger();
        ledger.ApplySnapshot(
            [new CurrencyItem(InventoryKey, CurrencyItemParser.KinaId, 4_454_882, 1)],
            exact: false,
            SnapshotAt);

        Assert.Equal(4_454_882, Balance(ledger, CurrencyCatalog.Kina));
        Assert.Null(Balance(ledger, CurrencyCatalog.ServerStorageKina));
        Assert.Null(Balance(ledger, CurrencyCatalog.BoundKina));
        Assert.Single(ledger.KnownBalances());
    }

    /// <summary>A world entry may be a different character, so a new snapshot starts over — the previous one's
    /// stacks must not be summed into the new one's balances.</summary>
    [Fact]
    public void A_new_snapshot_replaces_the_previous_one_entirely()
    {
        var ledger = new CurrencyLedger();
        ledger.ApplySnapshot(TenSevenSnapshot(), exact: true, SnapshotAt);
        ledger.ApplySnapshot(
            [new CurrencyItem(0x999, CurrencyItemParser.KinaId, 1_000, 1)],
            exact: true,
            SnapshotAt + 600_000);

        Assert.Equal(1_000, Balance(ledger, CurrencyCatalog.Kina));
        Assert.Equal(0, Balance(ledger, CurrencyCatalog.ServerStorageKina));
        Assert.Equal(0, Balance(ledger, CurrencyCatalog.BoundKina));
    }

    // ---- attribution ----

    /// <summary>The snapshot lands 7.19 s before the packet naming its character. Until an identity at or after
    /// it exists, nothing is filed — that identity, when it comes, is by construction the one the snapshot
    /// describes.</summary>
    [Fact]
    public void A_snapshot_waits_for_the_identity_that_follows_it()
    {
        var ledger = new CurrencyLedger();
        long previousIdentity = SnapshotAt - 3_600_000; // the character played before this world entry
        ledger.ApplySnapshot(TenSevenSnapshot(), exact: true, SnapshotAt);

        Assert.True(ledger.HasPendingSnapshot);
        Assert.False(ledger.TryTakeFiling(previousIdentity, SnapshotAt + 1_000, out _));

        Assert.True(ledger.TryTakeFiling(IdentityAt, IdentityAt, out IReadOnlyList<CurrencyBalance> filed));
        Assert.False(ledger.HasPendingSnapshot);
        Assert.Equal(CurrencyCatalog.All.Count, filed.Count);
    }

    /// <summary>A change that lands while the snapshot is still waiting belongs to the snapshot's character. It
    /// updates the ledger and goes out WITH the snapshot — it is not filed on its own under the outgoing
    /// character.</summary>
    [Fact]
    public void A_change_during_the_wait_is_filed_with_the_snapshot_not_before_it()
    {
        var ledger = new CurrencyLedger();
        long previousIdentity = SnapshotAt - 3_600_000;
        ledger.ApplySnapshot(TenSevenSnapshot(), exact: true, SnapshotAt);
        ledger.ApplyChanges(Withdraw(), SnapshotAt + 2_000);

        Assert.False(ledger.TryTakeFiling(previousIdentity, SnapshotAt + 2_000, out _));

        Assert.True(ledger.TryTakeFiling(IdentityAt, IdentityAt, out IReadOnlyList<CurrencyBalance> filed));
        Assert.Equal(329_454_882, filed.Single(b => b is { ItemId: CurrencyItemParser.KinaId, Container: 1 }).Count);
        Assert.Equal(0, filed.Single(b => b is { ItemId: CurrencyItemParser.KinaId, Container: 2 }).Count);
    }

    /// <summary>Once filed, a change goes out at once — the identity settled long ago — and a quiet ledger has
    /// nothing to file.</summary>
    [Fact]
    public void After_the_snapshot_is_filed_changes_file_immediately()
    {
        var ledger = new CurrencyLedger();
        ledger.ApplySnapshot(TenSevenSnapshot(), exact: true, SnapshotAt);
        Assert.True(ledger.TryTakeFiling(IdentityAt, IdentityAt, out _));
        Assert.False(ledger.TryTakeFiling(IdentityAt, IdentityAt + 1_000, out _));

        ledger.ApplyChanges(Withdraw(), WithdrawAt);

        Assert.True(ledger.TryTakeFiling(IdentityAt, WithdrawAt, out IReadOnlyList<CurrencyBalance> filed));
        Assert.Equal(329_454_882, filed.Single(b => b is { ItemId: CurrencyItemParser.KinaId, Container: 1 }).Count);
    }

    /// <summary>A re-sent snapshot that no naming packet follows must not freeze forever: past the settle window
    /// it files under whoever is current, the rule every 0x610B-style dump uses.</summary>
    [Fact]
    public void A_snapshot_with_no_naming_packet_files_after_the_settle_window()
    {
        var ledger = new CurrencyLedger();
        long earlierIdentity = SnapshotAt - 60_000;
        ledger.ApplySnapshot(TenSevenSnapshot(), exact: true, SnapshotAt);

        Assert.False(ledger.TryTakeFiling(earlierIdentity, SnapshotAt + WeeklyContentOwnership.SettleMs - 1, out _));
        Assert.True(ledger.TryTakeFiling(earlierIdentity, SnapshotAt + WeeklyContentOwnership.SettleMs, out _));
    }

    /// <summary>The switch keeps a snapshot that landed just before it — that is the incoming character's, since
    /// the snapshot always beats its naming packet.</summary>
    [Fact]
    public void A_character_switch_keeps_the_snapshot_that_just_arrived()
    {
        var ledger = new CurrencyLedger();
        ledger.ApplySnapshot(TenSevenSnapshot(), exact: true, SnapshotAt);

        ledger.OnCharacterSwitch(IdentityAt);

        Assert.True(ledger.HasPendingSnapshot);
        Assert.Equal(4_454_882, Balance(ledger, CurrencyCatalog.Kina));
    }

    /// <summary>With no fresh snapshot, what the ledger holds is the character being left. Kept, a change on the
    /// new character would be summed onto the old one's stacks — so it is dropped, and the new character's
    /// balances fill in from its own changes.</summary>
    [Fact]
    public void A_character_switch_without_a_fresh_snapshot_forgets_the_previous_character()
    {
        var ledger = new CurrencyLedger();
        ledger.ApplySnapshot(TenSevenSnapshot(), exact: true, SnapshotAt);
        Assert.True(ledger.TryTakeFiling(IdentityAt, IdentityAt, out _));

        long switchAt = SnapshotAt + 3_600_000;
        ledger.OnCharacterSwitch(switchAt);

        Assert.Empty(ledger.KnownBalances());
        Assert.False(ledger.TryTakeFiling(switchAt, switchAt, out _));

        ledger.ApplyChanges(
            [new CurrencyItemChange(ItemChangeType.Update, new CurrencyItem(0x777, CurrencyItemParser.KinaId, 52_000, 1))],
            switchAt + 5_000);
        Assert.Equal(52_000, Balance(ledger, CurrencyCatalog.Kina));
        Assert.Null(Balance(ledger, CurrencyCatalog.ServerStorageKina));
    }
}

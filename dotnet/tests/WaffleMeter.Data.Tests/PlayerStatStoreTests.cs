using WaffleMeter.Capture;
using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>
/// The character stat dictionary store. The two behaviours that matter are both about IDENTITY, not about
/// arithmetic: the stat frames arrive before the packet that says which entity is the local player (measured
/// lead ≈ 6 s), and a stranger's frame must never become "your stats".
/// </summary>
public sealed class PlayerStatStoreTests
{
    private static (int, int)[] Stats(params (int, int)[] pairs) => pairs;

    [Fact]
    public void Nothing_is_reported_before_an_owner_is_known()
    {
        var store = new PlayerStatStore();
        store.Accept(1234, Stats((PlayerStatIds.Attack, 3238)), fullSnapshot: false, arrivedAt: 100);

        Assert.Null(store.Current);
    }

    [Fact]
    public void Stats_that_arrived_before_the_identity_are_replayed_when_the_owner_is_confirmed()
    {
        // This is the whole reason the store buffers: dropping these would mean the sheet is only ever
        // complete on a lucky login where the identity packet happened to win the race.
        var store = new PlayerStatStore();
        store.Accept(1234, Stats((PlayerStatIds.Attack, 3238), (PlayerStatIds.HardHitPercent, 5952)),
            fullSnapshot: false, arrivedAt: 100);

        store.SetOwner(1234);

        PlayerStatSheet sheet = Assert.IsType<PlayerStatSheet>(store.Current);
        Assert.Equal(3238, sheet.Raw(PlayerStatIds.Attack));
        Assert.Equal(59.52, sheet.Percent(PlayerStatIds.HardHitPercent)!.Value, 6);
    }

    [Fact]
    public void An_entity_less_full_snapshot_is_the_local_players_and_is_replayed_too()
    {
        // The full-sheet broadcast carries no entity id — it is the local player's by construction, and the
        // receiver binds it to whoever it currently believes it is.
        var store = new PlayerStatStore();
        store.Accept(0, Stats((PlayerStatIds.Attack, 3238)), fullSnapshot: true, arrivedAt: 100);

        store.SetOwner(1234);

        PlayerStatSheet sheet = Assert.IsType<PlayerStatSheet>(store.Current);
        Assert.Equal(3238, sheet.Raw(PlayerStatIds.Attack));
        Assert.True(sheet.FullSnapshotSeen);
    }

    [Fact]
    public void A_strangers_stats_never_become_mine()
    {
        var store = new PlayerStatStore();
        store.Accept(999, Stats((PlayerStatIds.Attack, 99999)), fullSnapshot: false, arrivedAt: 100);

        store.SetOwner(1234);

        Assert.Null(store.Current);
    }

    [Fact]
    public void A_full_snapshot_replaces_rather_than_merges()
    {
        // A stat that vanished (an unequipped item's bonus) has to vanish here too, or the sheet reports a
        // bonus the character no longer has.
        var store = new PlayerStatStore();
        store.SetOwner(1234);
        store.Accept(1234, Stats((PlayerStatIds.Attack, 3238), (PlayerStatIds.Penetration, 500)),
            fullSnapshot: false, arrivedAt: 100);
        store.Accept(1234, Stats((PlayerStatIds.Attack, 3400)), fullSnapshot: true, arrivedAt: 200);

        PlayerStatSheet sheet = Assert.IsType<PlayerStatSheet>(store.Current);
        Assert.Equal(3400, sheet.Raw(PlayerStatIds.Attack));
        Assert.Null(sheet.Raw(PlayerStatIds.Penetration));
    }

    [Fact]
    public void A_delta_merges_onto_what_is_already_there()
    {
        var store = new PlayerStatStore();
        store.SetOwner(1234);
        store.Accept(1234, Stats((PlayerStatIds.Attack, 3238), (PlayerStatIds.Penetration, 500)),
            fullSnapshot: true, arrivedAt: 100);
        store.Accept(1234, Stats((PlayerStatIds.Attack, 3400)), fullSnapshot: false, arrivedAt: 200);

        PlayerStatSheet sheet = Assert.IsType<PlayerStatSheet>(store.Current);
        Assert.Equal(3400, sheet.Raw(PlayerStatIds.Attack));
        Assert.Equal(500, sheet.Raw(PlayerStatIds.Penetration));
        Assert.Equal(200, sheet.UpdatedAt);
    }

    [Fact]
    public void Switching_character_clears_the_sheet_rather_than_carrying_it_over()
    {
        // Stats belong to a character. Showing the previous character's numbers under the new one's name is
        // worse than showing none — the user cannot tell they are stale.
        var store = new PlayerStatStore();
        store.SetOwner(1234);
        store.Accept(1234, Stats((PlayerStatIds.Attack, 3238)), fullSnapshot: true, arrivedAt: 100);

        store.SetOwner(5678);

        Assert.Null(store.Current);
    }

    [Fact]
    public void Held_stats_expire_so_a_busy_zone_cannot_grow_the_buffer_without_limit()
    {
        // Every nearby player's incremental updates come through the same opcode, so the hold has to be
        // bounded by age as well as by count.
        var store = new PlayerStatStore();
        store.Accept(1234, Stats((PlayerStatIds.Attack, 3238)), fullSnapshot: false, arrivedAt: 0);
        // A later frame from someone else advances the store's notion of "now" past the TTL.
        store.Accept(4321, Stats((PlayerStatIds.Attack, 1)), fullSnapshot: false, arrivedAt: 120_000);

        store.SetOwner(1234);

        Assert.Null(store.Current);
    }

    [Fact]
    public void Cooldown_reduction_is_summed_and_sign_flipped()
    {
        // The server splits it across a base and a bonus id and reports the REDUCTION as positive; a human
        // reads a cooldown reduction as a negative number.
        var store = new PlayerStatStore();
        store.SetOwner(1);
        store.Accept(1, Stats((PlayerStatIds.CooldownBasePercent, 215), (PlayerStatIds.CooldownBonusPercent, 433)),
            fullSnapshot: true, arrivedAt: 10);

        PlayerStatSheet sheet = Assert.IsType<PlayerStatSheet>(store.Current);
        Assert.Equal(-6.48, sheet.CooldownPercent()!.Value, 6);
    }

    [Fact]
    public void Absent_cooldown_ids_report_null_rather_than_zero()
    {
        var store = new PlayerStatStore();
        store.SetOwner(1);
        store.Accept(1, Stats((PlayerStatIds.Attack, 1)), fullSnapshot: true, arrivedAt: 10);

        Assert.Null(Assert.IsType<PlayerStatSheet>(store.Current).CooldownPercent());
    }
}

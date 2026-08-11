using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>Aether (오드) balance state on <see cref="DataManager"/>. Every broadcast carries both pools
/// authoritatively — 자연회복 (the number shown outside the parentheses) and 추가 (inside) — so the data layer
/// stores what it is told and derives the spendable total. Nothing is back-computed.</summary>
public sealed class DataManagerAetherTests
{
    [Fact]
    public void No_value_until_first_update()
    {
        var dm = new DataManager();
        Assert.False(dm.CurrentAether.HasValue);
    }

    [Fact]
    public void Update_sets_natural_bonus_and_the_derived_total()
    {
        var dm = new DataManager();
        dm.SaveAetherStatus(baseVal: 90, bonus: 870);

        (int b, int bonus, int total, bool has) = dm.CurrentAether;
        Assert.True(has);
        Assert.Equal(90, b);
        Assert.Equal(870, bonus);
        Assert.Equal(960, total);
    }

    /// <summary>The 2026-07-30 regression. A 오드 회복 소모품 arrives as a 추가-only broadcast; the number
    /// outside the parentheses must not move. (The old back-compute treated that packet as a total and
    /// absorbed its delta into 자연회복.)</summary>
    [Fact]
    public void A_consumable_grant_moves_only_the_additional_pool()
    {
        var dm = new DataManager();
        dm.SaveAetherStatus(baseVal: 375, bonus: 385); // 375(+385)
        dm.SaveAetherStatus(baseVal: 375, bonus: 395); // 오드 회복 소모품 +10

        Assert.Equal((375, 395, 770), (dm.CurrentAether.Base, dm.CurrentAether.Bonus, dm.CurrentAether.Total));
    }

    /// <summary>Natural regeneration ticks the 자연회복 pool by 15 and leaves 추가 alone.</summary>
    [Fact]
    public void A_natural_tick_moves_only_the_natural_pool()
    {
        var dm = new DataManager();
        dm.SaveAetherStatus(baseVal: 520, bonus: 385);
        dm.SaveAetherStatus(baseVal: 535, bonus: 385);

        Assert.Equal((535, 385, 920), (dm.CurrentAether.Base, dm.CurrentAether.Bonus, dm.CurrentAether.Total));
    }

    /// <summary>A pool the packet omits is zero, not "unchanged" — spending 80 out of 80(+750) empties
    /// 자연회복, and the game then broadcasts the 추가 pool alone.</summary>
    [Fact]
    public void An_omitted_pool_is_zero_not_carried_over()
    {
        var dm = new DataManager();
        dm.SaveAetherStatus(baseVal: 80, bonus: 750);
        dm.SaveAetherStatus(baseVal: 0, bonus: 750); // 추가-only broadcast after the 80 spend

        Assert.Equal((0, 750, 750), (dm.CurrentAether.Base, dm.CurrentAether.Bonus, dm.CurrentAether.Total));
    }

    [Fact]
    public void Restore_seeds_the_balance_when_empty()
    {
        var dm = new DataManager();
        dm.RestoreAetherStatus(240, 295);

        (int b, int bonus, int total, bool has) = dm.CurrentAether;
        Assert.True(has);
        Assert.Equal((240, 295, 535), (b, bonus, total));
    }

    [Fact]
    public void Restore_does_not_clobber_a_live_value()
    {
        var dm = new DataManager();
        dm.SaveAetherStatus(baseVal: 100, bonus: 50); // live broadcast arrived first
        dm.RestoreAetherStatus(240, 295);             // a late restore must not override it

        Assert.Equal((100, 50, 150), (dm.CurrentAether.Base, dm.CurrentAether.Bonus, dm.CurrentAether.Total));
    }

    [Fact]
    public void Hard_reset_clears_the_balance()
    {
        var dm = new DataManager();
        dm.SaveAetherStatus(90, 870);
        dm.HardReset();
        Assert.False(dm.CurrentAether.HasValue);
    }

    [Fact]
    public void Change_event_fires_on_update()
    {
        var dm = new DataManager();
        int fired = 0;
        dm.AetherStatusChanged += () => fired++;
        dm.SaveAetherStatus(90, 870);
        Assert.Equal(1, fired);
    }

    // ---- character switch: whose balance is the one we are holding? ----
    //
    // The 0x610B login dump arrives BEFORE the own-load packet that names its character (measured ~4-6 s, no
    // counter-example), so at the instant a switch is detected the newest reading is the INCOMING character's.
    // Clearing unconditionally — as this did until 2026-08-11 — threw away the one correct value we had and left
    // the footer badge blank until the game next chose to broadcast.

    private const long T0 = 1_786_000_000_000L;

    [Fact]
    public void A_switch_keeps_the_balance_that_arrived_with_the_incoming_login_dump()
    {
        long clock = T0;
        var dm = new DataManager { Clock = () => clock };
        dm.SaveNickname(9549, "콘팡", isExecutor: true, server: 2003, jobByte: 16);
        dm.SaveAetherStatus(300, 100, fromSnapshot: false); // 콘팡's own balance, long settled

        clock += 60_000;                                    // log out, character select, load
        dm.SaveAetherStatus(45, 900, fromSnapshot: true);   // 마이농's 0x610B dump lands FIRST
        clock += 5_000;
        dm.SaveNickname(9550, "마이농", isExecutor: true, server: 2003, jobByte: 12); // ...then its name

        (int b, int bonus, int total, bool has) = dm.CurrentAether;
        Assert.True(has);
        Assert.Equal((45, 900, 945), (b, bonus, total));
    }

    [Fact]
    public void A_switch_drops_a_balance_that_predates_the_handover()
    {
        long clock = T0;
        var dm = new DataManager { Clock = () => clock };
        dm.SaveNickname(9549, "콘팡", isExecutor: true, server: 2003, jobByte: 16);
        dm.SaveAetherStatus(300, 100, fromSnapshot: false);

        clock += 60_000; // no dump arrived for the incoming character — this really is the old one's
        dm.SaveNickname(9550, "마이농", isExecutor: true, server: 2003, jobByte: 12);

        Assert.False(dm.CurrentAether.HasValue);
    }

    [Fact]
    public void A_same_character_reinstance_never_drops_the_balance()
    {
        long clock = T0;
        var dm = new DataManager { Clock = () => clock };
        dm.SaveNickname(9549, "콘팡", isExecutor: true, server: 2003, jobByte: 16);
        dm.SaveAetherStatus(300, 100);

        clock += 10L * 60 * 1000;                            // a zone load, an hour into the session
        dm.SaveNickname(9600, "콘팡", isExecutor: true, server: 2003, jobByte: 16); // same name, fresh uid

        Assert.True(dm.CurrentAether.HasValue);
        Assert.Equal(300, dm.CurrentAether.Base);
    }

    [Fact]
    public void A_restored_balance_never_passes_as_the_incoming_dump()
    {
        long clock = T0;
        var dm = new DataManager { Clock = () => clock };
        dm.SaveNickname(9549, "콘팡", isExecutor: true, server: 2003, jobByte: 16);
        dm.RestoreAetherStatus(300, 100); // a cache, not an observation — arrival stamp stays 0

        clock += 1_000;                   // well inside the handover grace, and still not the new character's
        dm.SaveNickname(9550, "마이농", isExecutor: true, server: 2003, jobByte: 12);

        Assert.False(dm.CurrentAether.HasValue);
    }

    [Fact]
    public void Origin_tells_a_live_reading_apart_from_a_restore()
    {
        long clock = T0;
        var dm = new DataManager { Clock = () => clock };

        dm.SaveAetherStatus(90, 870, fromSnapshot: true);
        Assert.Equal((T0, true), dm.AetherOrigin);

        dm.SaveAetherStatus(90, 880, fromSnapshot: false);
        Assert.Equal((T0, false), dm.AetherOrigin);

        dm.RestoreAetherStatus(1, 2, onlyIfEmpty: false);
        Assert.Equal((0L, false), dm.AetherOrigin); // a restore has no observation time
    }
}

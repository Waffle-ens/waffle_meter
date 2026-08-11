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

    /// <summary>Only the 0x610B DUMP earns the handover grace. A 0x610C change notice fires when a balance
    /// CHANGES, which means its character was logged in and playing — so it is the outgoing character's, and
    /// letting one through would pin their 오드 to the new character and suppress the re-seed that corrects it.</summary>
    [Fact]
    public void A_change_notice_inside_the_grace_window_is_still_the_outgoing_characters()
    {
        long clock = T0;
        var dm = new DataManager { Clock = () => clock };
        dm.SaveNickname(9549, "콘팡", isExecutor: true, server: 2003, jobByte: 16);

        clock += 60_000;
        dm.SaveAetherStatus(300, 100, fromSnapshot: false); // 콘팡 spent 오드 seconds before logging out
        clock += 5_000;
        dm.SaveNickname(9550, "마이농", isExecutor: true, server: 2003, jobByte: 12);

        Assert.False(dm.CurrentAether.HasValue);
    }

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
        Assert.Equal((T0, true, true), dm.AetherOrigin);

        dm.SaveAetherStatus(90, 880, fromSnapshot: false);
        Assert.Equal((T0, false, true), dm.AetherOrigin);

        // A restore carries the time the reading it revives was ORIGINALLY taken — that is what the offline
        // 자연회복 projection measures elapsed time from — but is never marked live.
        dm.RestoreAetherStatus(1, 2, observedAtMs: T0 - 90_000, onlyIfEmpty: false);
        Assert.Equal((T0 - 90_000, false, false), dm.AetherOrigin);
    }

    [Fact]
    public void A_restore_is_never_treated_as_authoritative_over_a_live_reading()
    {
        long clock = T0;
        var dm = new DataManager { Clock = () => clock };
        dm.SaveAetherStatus(100, 50);

        dm.RestoreAetherStatus(999, 999, observedAtMs: T0 - 1000); // onlyIfEmpty defaults to true

        Assert.Equal((100, 50), (dm.CurrentAether.Base, dm.CurrentAether.Bonus));
        Assert.True(dm.AetherOrigin.IsLive);
    }

    [Fact]
    public void Dropping_a_restored_balance_leaves_a_live_one_alone()
    {
        long clock = T0;
        var dm = new DataManager { Clock = () => clock };

        dm.RestoreAetherStatus(300, 100, observedAtMs: T0 - 1000);
        dm.DropRestoredAether();
        Assert.False(dm.CurrentAether.HasValue); // no record for this character: better empty than a stranger's

        dm.SaveAetherStatus(120, 30);
        dm.DropRestoredAether();
        Assert.True(dm.CurrentAether.HasValue);
        Assert.Equal(120, dm.CurrentAether.Base);
    }

    // ---- the shugo-festa key rides the same packet but must be judged on its OWN arrival ----

    /// <summary>The key parser has no empty-mask branch, so a character holding ZERO keys produces no reading at
    /// all. Judging the key by the AETHER stamp therefore kept the previous character's count alive on the new
    /// character — the clear that used to save us was vetoed by a resource that did arrive.</summary>
    [Fact]
    public void A_switch_drops_the_key_count_even_when_the_aether_reading_is_kept()
    {
        long clock = T0;
        var dm = new DataManager { Clock = () => clock };
        dm.SaveNickname(9549, "콘팡", isExecutor: true, server: 2003, jobByte: 16);
        dm.SaveShugoKey(3, 0);                              // 콘팡 holds three keys

        clock += 60_000;
        dm.SaveAetherStatus(45, 900, fromSnapshot: true);   // 마이농's dump: 오드 present, keys absent (zero)
        clock += 5_000;
        dm.SaveNickname(9550, "마이농", isExecutor: true, server: 2003, jobByte: 12);

        Assert.True(dm.CurrentAether.HasValue);             // the 오드 reading IS the incoming character's
        Assert.False(dm.CurrentShugoKey.HasValue);          // ...but the key count was 콘팡's and must go
    }

    [Fact]
    public void A_switch_keeps_a_key_count_that_arrived_with_the_incoming_login_dump()
    {
        long clock = T0;
        var dm = new DataManager { Clock = () => clock };
        dm.SaveNickname(9549, "콘팡", isExecutor: true, server: 2003, jobByte: 16);
        dm.SaveShugoKey(3, 0);

        clock += 60_000;
        dm.SaveShugoKey(7, 0, fromSnapshot: true);          // 마이농 holds seven, so its record IS carried
        clock += 5_000;
        dm.SaveNickname(9550, "마이농", isExecutor: true, server: 2003, jobByte: 12);

        Assert.True(dm.CurrentShugoKey.HasValue);
        Assert.Equal(7, dm.CurrentShugoKey.Base);
    }
}

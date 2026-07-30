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
}

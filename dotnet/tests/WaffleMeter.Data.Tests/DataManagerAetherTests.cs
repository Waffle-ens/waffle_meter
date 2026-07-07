using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>Aether (오드) balance state on <see cref="DataManager"/>: split updates set base/bonus directly;
/// total-only updates back-compute base/bonus from the previous value (spend base first, then bonus).</summary>
public sealed class DataManagerAetherTests
{
    [Fact]
    public void No_value_until_first_update()
    {
        var dm = new DataManager();
        Assert.False(dm.CurrentAether.HasValue);
    }

    [Fact]
    public void Split_update_sets_base_bonus_total()
    {
        var dm = new DataManager();
        dm.SaveAetherStatus(split: true, baseVal: 90, bonus: 870, total: 960);

        (int b, int bonus, int total, bool has) = dm.CurrentAether;
        Assert.True(has);
        Assert.Equal(90, b);
        Assert.Equal(870, bonus);
        Assert.Equal(960, total);
    }

    [Fact]
    public void Total_only_spend_reduces_base_first_then_bonus()
    {
        var dm = new DataManager();
        dm.SaveAetherStatus(split: true, baseVal: 100, bonus: 50, total: 150);

        dm.SaveAetherStatus(split: false, 0, 0, total: 130); // spent 20 → all from base
        Assert.Equal((80, 50, 130), (dm.CurrentAether.Base, dm.CurrentAether.Bonus, dm.CurrentAether.Total));

        dm.SaveAetherStatus(split: false, 0, 0, total: 40);  // spent 90 → 80 from base, 10 from bonus
        Assert.Equal((0, 40, 40), (dm.CurrentAether.Base, dm.CurrentAether.Bonus, dm.CurrentAether.Total));
    }

    [Fact]
    public void Restore_seeds_the_balance_when_empty()
    {
        var dm = new DataManager();
        dm.RestoreAetherStatus(240, 295, 535);

        (int b, int bonus, int total, bool has) = dm.CurrentAether;
        Assert.True(has);
        Assert.Equal((240, 295, 535), (b, bonus, total));
    }

    [Fact]
    public void Restore_does_not_clobber_a_live_value()
    {
        var dm = new DataManager();
        dm.SaveAetherStatus(split: true, baseVal: 100, bonus: 50, total: 150); // live broadcast arrived first
        dm.RestoreAetherStatus(240, 295, 535); // a late restore must not override it

        Assert.Equal((100, 50, 150), (dm.CurrentAether.Base, dm.CurrentAether.Bonus, dm.CurrentAether.Total));
    }

    [Fact]
    public void Total_only_gain_adds_to_base()
    {
        var dm = new DataManager();
        dm.SaveAetherStatus(split: true, baseVal: 100, bonus: 50, total: 150);
        dm.SaveAetherStatus(split: false, 0, 0, total: 200); // +50 → base grows

        Assert.Equal((150, 50, 200), (dm.CurrentAether.Base, dm.CurrentAether.Bonus, dm.CurrentAether.Total));
    }

    [Fact]
    public void Hard_reset_clears_the_balance()
    {
        var dm = new DataManager();
        dm.SaveAetherStatus(split: true, 90, 870, 960);
        dm.HardReset();
        Assert.False(dm.CurrentAether.HasValue);
    }

    [Fact]
    public void Change_event_fires_on_update()
    {
        var dm = new DataManager();
        int fired = 0;
        dm.AetherStatusChanged += () => fired++;
        dm.SaveAetherStatus(split: true, 90, 870, 960);
        Assert.Equal(1, fired);
    }
}

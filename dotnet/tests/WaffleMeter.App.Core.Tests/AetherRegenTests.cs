using WaffleMeter.App.Core;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

/// <summary>Carrying a remembered 오드 balance over the time the meter was not watching. The 자연회복 pool accrues
/// on the server whether or not anyone is logged in; the 추가 pool does not.</summary>
public class AetherRegenTests
{
    private const long Hour = 60L * 60 * 1000;
    private static long Now => 1_786_000_000_000L; // a plausible present, so the age guards behave as in the app

    [Fact]
    public void Adds_one_tick_per_three_hours()
    {
        Assert.Equal((115, 200), AetherRegen.Project(100, 200, Now - (3 * Hour), Now));
        Assert.Equal((130, 200), AetherRegen.Project(100, 200, Now - (6 * Hour), Now));
    }

    [Fact]
    public void A_full_day_is_a_hundred_and_twenty()
    {
        (int b, int bonus) = AetherRegen.Project(100, 0, Now - (24 * Hour), Now);
        Assert.Equal(220, b);
        Assert.Equal(0, bonus);
    }

    [Fact]
    public void Partial_ticks_are_floored_never_rounded_up()
    {
        // 2h59m is not a tick, and 5h59m is one — the projection may only ever under-state.
        Assert.Equal(100, AetherRegen.Project(100, 0, Now - ((3 * Hour) - 60_000), Now).Base);
        Assert.Equal(115, AetherRegen.Project(100, 0, Now - ((6 * Hour) - 60_000), Now).Base);
    }

    [Fact]
    public void The_bonus_pool_never_regenerates()
    {
        // 추가 오드 comes from 소모품 and grants; nothing accrues it on a timer.
        Assert.Equal(500, AetherRegen.Project(0, 500, Now - (48 * Hour), Now).Bonus);
    }

    [Fact]
    public void Stops_at_the_natural_cap()
    {
        Assert.Equal(AetherRegen.BaseCap, AetherRegen.Project(800, 0, Now - (24 * Hour), Now).Base);
        Assert.Equal(AetherRegen.BaseCap, AetherRegen.Project(AetherRegen.BaseCap, 0, Now - (24 * Hour), Now).Base);
    }

    /// <summary>The cap is a growth ceiling, not a correction. A reading above it is passed through: the cap is
    /// what the game is understood to allow, so a balance over it more likely means that understanding is out of
    /// date than that the reading is wrong — and editing an observed number downward is the worse failure.</summary>
    [Fact]
    public void A_reading_above_the_cap_is_passed_through_not_edited_down()
    {
        Assert.Equal((900, 2500), AetherRegen.Project(900, 2500, Now - (24 * Hour), Now));
    }

    [Fact]
    public void Absurd_values_from_a_hand_edited_file_are_still_bounded()
    {
        Assert.Equal((10_000, 10_000), AetherRegen.Project(999_999, 999_999, Now - (24 * Hour), Now));
        Assert.Equal((0, 0), AetherRegen.Project(-5, -5, Now, Now));
    }

    [Fact]
    public void Zero_is_a_balance_and_still_regenerates()
    {
        // A character that spent everything is the case that used to render no badge at all.
        Assert.Equal((15, 0), AetherRegen.Project(0, 0, Now - (3 * Hour), Now));
    }

    [Fact]
    public void A_reading_older_than_the_window_is_left_alone()
    {
        // Not saturated to the cap: past a week this is "we have no idea", and a confident 840 would read as a
        // measurement. Guards a hand-edited / 1970-epoch timestamp too.
        Assert.Equal((100, 50), AetherRegen.Project(100, 50, Now - (8L * 24 * Hour), Now));
        Assert.Equal((100, 50), AetherRegen.Project(100, 50, 1000, Now));
    }

    [Fact]
    public void A_clock_that_went_backwards_never_grows_the_balance()
    {
        Assert.Equal((100, 50), AetherRegen.Project(100, 50, Now + (5 * Hour), Now));
        Assert.Equal((100, 50), AetherRegen.Project(100, 50, 0, Now));
    }

    [Fact]
    public void Ticks_since_matches_the_projection()
    {
        Assert.Equal(0, AetherRegen.TicksSince(Now - (2 * Hour), Now));
        Assert.Equal(8, AetherRegen.TicksSince(Now - (24 * Hour), Now));
        Assert.Equal(0, AetherRegen.TicksSince(Now - (8L * 24 * Hour), Now)); // outside the window
    }
}

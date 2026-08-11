using WaffleMeter.App.Core;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

/// <summary>Carrying a remembered 오드 balance over the time the meter was not watching.
/// <para>The 자연회복 tick is a server-wide three-hourly CLOCK GRID (02:00 / 05:00 / … KST), not a stopwatch
/// started at the last reading — so these tests are written against grid boundaries rather than durations. The
/// distinction is the whole point: a 29-minute gap that straddles a boundary pays +15, and a 2h-59m gap that
/// straddles none pays nothing.</para></summary>
public class AetherRegenTests
{
    private const long Minute = 60L * 1000;
    private const long Hour = 60 * Minute;

    /// <summary>The k-th tick boundary at or after 2026-08-11. Anchored on the real grid so the tests fail if
    /// the phase constant is ever changed by accident.</summary>
    private static long Boundary(int k)
    {
        const long anchor = 1_786_000_000_000L;
        long index = ((anchor - AetherRegen.TickPhaseMs) / AetherRegen.TickIntervalMs) + 1;
        return AetherRegen.TickPhaseMs + ((index + k) * AetherRegen.TickIntervalMs);
    }

    [Fact]
    public void The_grid_is_three_hourly_at_two_past_the_hour_KST()
    {
        // 02:00:10 / 05:00:10 / … — hour % 3 == 2 in KST (and in UTC: +9 is an exact multiple of 3).
        var kst = new TimeSpan(9, 0, 0);
        DateTimeOffset tick = DateTimeOffset.FromUnixTimeMilliseconds(Boundary(0)).ToOffset(kst);

        Assert.Equal(2, tick.Hour % 3);
        Assert.Equal(0, tick.Minute);
        Assert.Equal(10, tick.Second);
    }

    [Fact]
    public void Pays_fifteen_for_each_boundary_crossed()
    {
        Assert.Equal((115, 200), AetherRegen.Project(100, 200, Boundary(0) - Minute, Boundary(0) + Minute));
        Assert.Equal((130, 200), AetherRegen.Project(100, 200, Boundary(0) - Minute, Boundary(1) + Minute));
    }

    /// <summary>The case that refutes the elapsed-time model outright: 29 minutes apart, one boundary between,
    /// +15. Straight from the corpus (콘팡, 2026-06-11 19:46 → 20:15 KST).</summary>
    [Fact]
    public void A_short_gap_across_a_boundary_still_pays()
    {
        Assert.Equal(30, AetherRegen.Project(15, 0, Boundary(0) - (14 * Minute), Boundary(0) + (15 * Minute)).Base);
    }

    /// <summary>And its mirror: nearly three hours, no boundary, nothing paid.</summary>
    [Fact]
    public void A_long_gap_that_crosses_nothing_pays_nothing()
    {
        Assert.Equal(15, AetherRegen.Project(15, 0, Boundary(0) + Minute, Boundary(1) - Minute).Base);
    }

    [Fact]
    public void A_full_day_is_eight_ticks_and_a_hundred_and_twenty()
    {
        Assert.Equal(8, AetherRegen.TicksBetween(Boundary(0) - Minute, Boundary(0) - Minute + (24 * Hour)));
        Assert.Equal(220, AetherRegen.Project(100, 0, Boundary(0) - Minute, Boundary(0) - Minute + (24 * Hour)).Base);
    }

    [Fact]
    public void A_boundary_exactly_at_the_reading_is_already_in_it()
    {
        // (from, to] — the tick at `from` is what produced the value being carried, so counting it again would
        // pay it twice.
        Assert.Equal(0, AetherRegen.TicksBetween(Boundary(0), Boundary(1) - Minute));
        Assert.Equal(1, AetherRegen.TicksBetween(Boundary(0) - 1, Boundary(0)));
    }

    [Fact]
    public void The_bonus_pool_never_regenerates()
    {
        // 추가 오드 comes from 소모품 and grants; nothing accrues it on a timer.
        Assert.Equal(500, AetherRegen.Project(0, 500, Boundary(0), Boundary(0) + (48 * Hour)).Bonus);
    }

    [Fact]
    public void Stops_at_the_natural_cap()
    {
        Assert.Equal(AetherRegen.BaseCap, AetherRegen.Project(800, 0, Boundary(0), Boundary(0) + (24 * Hour)).Base);

        // An empty pool needs 56 ticks to fill, which is exactly the seven days this will project across — so
        // the cap is reachable from zero only right at the edge of the window, and never overshoots it.
        Assert.Equal(AetherRegen.BaseCap, AetherRegen.Project(0, 0, Boundary(0), Boundary(0) + (7 * 24 * Hour)).Base);
        Assert.Equal(720, AetherRegen.Project(0, 0, Boundary(0), Boundary(0) + (6 * 24 * Hour)).Base);
    }

    /// <summary>The cap is a growth ceiling, not a correction. A reading above it is passed through: the cap is
    /// the game's rule rather than our measurement, and editing an observed number downward is the worse
    /// failure.</summary>
    [Fact]
    public void A_reading_above_the_cap_is_passed_through_not_edited_down()
    {
        Assert.Equal((900, 2500), AetherRegen.Project(900, 2500, Boundary(0), Boundary(0) + (24 * Hour)));
    }

    [Fact]
    public void Absurd_values_from_a_hand_edited_file_are_still_bounded()
    {
        Assert.Equal((10_000, 10_000), AetherRegen.Project(999_999, 999_999, Boundary(0), Boundary(0) + Hour));
        Assert.Equal((0, 0), AetherRegen.Project(-5, -5, Boundary(0), Boundary(0)));
    }

    [Fact]
    public void Zero_is_a_balance_and_still_regenerates()
    {
        // A character that spent everything is the case that used to render no badge at all.
        Assert.Equal((15, 0), AetherRegen.Project(0, 0, Boundary(0) - Minute, Boundary(0) + Minute));
    }

    [Fact]
    public void A_reading_older_than_the_window_is_left_alone()
    {
        // Not saturated to the cap: past a week this is "we have no idea", and a confident 840 would read as a
        // measurement. Guards a hand-edited / 1970-epoch timestamp too.
        Assert.Equal((100, 50), AetherRegen.Project(100, 50, Boundary(0), Boundary(0) + (8 * 24 * Hour)));
        Assert.Equal((100, 50), AetherRegen.Project(100, 50, 1000, Boundary(0)));
    }

    [Fact]
    public void A_clock_that_went_backwards_never_grows_the_balance()
    {
        Assert.Equal((100, 50), AetherRegen.Project(100, 50, Boundary(1), Boundary(0)));
        Assert.Equal((100, 50), AetherRegen.Project(100, 50, 0, Boundary(0)));
        Assert.Equal(0, AetherRegen.TicksBetween(-5_000, Boundary(0)));
    }

    /// <summary>Base values are multiples of five, not fifteen — spending comes out of this pool first and
    /// arrives in units of 80. Nothing here may assume divisibility by the tick size.</summary>
    [Fact]
    public void An_off_grid_balance_is_carried_as_is()
    {
        Assert.Equal(70, AetherRegen.Project(55, 0, Boundary(0) - Minute, Boundary(0) + Minute).Base);
    }
}

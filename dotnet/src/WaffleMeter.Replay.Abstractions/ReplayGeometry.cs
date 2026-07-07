namespace WaffleMeter.Replay;

/// <summary>
/// Pure playback geometry shared by any replay player (the in-meter WPF window today; the stats-web player
/// mirrors the same rules). Kept here — in the public model assembly — so it is unit-testable without a UI
/// and so the on-wire behavior is specified in one place. No allocation, no state.
/// </summary>
public static class ReplayGeometry
{
    /// <summary>Default: how far (world units) a real entity can travel per millisecond before a sample
    /// pair reads as a teleport (recall/blink). ~6 u/ms = ~6000 u/s ≈ 4.5x measured sprint, with headroom
    /// for mounts/gliding; observed teleports jump tens of thousands of units in one tick — an order of
    /// magnitude past this — so they are still caught with a wide margin.</summary>
    public const double DefaultMaxTravelWorldPerMs = 6.0;

    /// <summary>Default floor (world units) so the normal ~10 Hz cadence (tiny time spans) can't trip the
    /// teleport test on sub-unit jitter.</summary>
    public const double DefaultMinTeleportDistWorld = 2500;

    /// <summary>
    /// True when two consecutive samples imply a speed no legit locomotion can reach — a recall/blink.
    /// Time-normalized (distance vs. the span between them) so a long stride across a sampling gap isn't
    /// mistaken for a teleport. Players snap across it instead of drawing a map-crossing glide line.
    /// </summary>
    public static bool IsTeleport(
        in ReplayPoint a,
        in ReplayPoint b,
        double maxTravelWorldPerMs = DefaultMaxTravelWorldPerMs,
        double minTeleportDistWorld = DefaultMinTeleportDistWorld)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double dtMs = Math.Max(1, b.TMs - a.TMs); // guard against a zero/negative span
        double maxDist = Math.Max(minTeleportDistWorld, maxTravelWorldPerMs * dtMs);
        return dx * dx + dy * dy > maxDist * maxDist;
    }
}

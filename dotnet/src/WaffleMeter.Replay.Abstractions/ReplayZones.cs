namespace WaffleMeter.Replay;

/// <summary>
/// One boss mechanic resolved for a moment in time: where its zone sits right now, and whether the floor
/// is still telegraphing it or it has landed.
/// </summary>
/// <param name="Zone">The shape, from the client's catalog.</param>
/// <param name="Cast">The cast that fired it (carries the facing and the boss's HP at that moment).</param>
/// <param name="TargetUid">The entity this particular zone is anchored to (a spread paints one per player).</param>
/// <param name="CentreX">Zone centre in world space, already resolved for the anchor.</param>
/// <param name="CentreY">Zone centre in world space.</param>
/// <param name="Telegraphing">True while the floor is warning; false during the hit itself.</param>
public readonly record struct ActiveZone(
    ReplaySkillZone Zone,
    ReplayCast Cast,
    int TargetUid,
    double CentreX,
    double CentreY,
    bool Telegraphing);

/// <summary>
/// Turns the recorded boss casts + the client's zone catalog into the shapes to draw at a given playback
/// time. Pure and UI-free, so the in-meter player and the stats-web player follow identical rules and the
/// timing/anchoring is unit-testable.
/// </summary>
public static class ReplayZones
{
    /// <summary>How long a landed zone stays on screen after the hit (ms). The game clears its decal at
    /// once, but a zero-length flash is invisible on a scrub bar, so the hit is held briefly.</summary>
    public const double LingerMs = 900;

    /// <summary>A mechanic with no telegraph at all is still shown this long before it lands, so an
    /// instant hit doesn't pop in and vanish inside a single frame.</summary>
    public const double MinLeadMs = 500;

    /// <summary>
    /// Every zone on screen at <paramref name="nowMs"/> (ms from the battle start).
    /// <para>
    /// Anchoring, per the client's own flag:
    /// <list type="bullet">
    /// <item><see cref="ZoneAnchor.Caster"/> — on the boss's body. ONE zone, wherever the boss is; a cast
    /// that marks six players still only paints one cone. The boss's live track is preferred (the cast
    /// itself only carries the boss's position when it targeted ITSELF).</item>
    /// <item><see cref="ZoneAnchor.TargetLocation"/> — a puddle on the ground where each marked player stood
    /// at cast. It does not move: leaving it IS the dodge.</item>
    /// <item><see cref="ZoneAnchor.Target"/> — a marker stuck to each marked player: it follows their track,
    /// so it cannot be outrun.</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="bossUid">The battle's boss, for caster-anchored zones. 0 = fall back to the cast's own
    /// coordinates (correct for a self-cast, approximate otherwise).</param>
    public static List<ActiveZone> ActiveAt(
        IReadOnlyList<ReplayCast> casts,
        ReplaySkillShapes shapes,
        double nowMs,
        Func<int, double, (double X, double Y)?>? positionAt = null,
        int bossUid = 0)
    {
        var active = new List<ActiveZone>();
        foreach (ReplayCast cast in casts)
        {
            if (cast.Targets.Count == 0)
            {
                continue;
            }

            foreach (ReplaySkillZone zone in shapes.For(cast.SkillCode))
            {
                double lead = Math.Max(zone.NoticeMs, MinLeadMs);
                if (nowMs < cast.TMs - lead || nowMs > cast.TMs + LingerMs)
                {
                    continue;
                }

                bool telegraphing = nowMs < cast.TMs;
                if (zone.Anchor == ZoneAnchor.Caster)
                {
                    ReplayCastTarget primary = cast.Targets[0];
                    (double X, double Y) centre = (bossUid != 0 ? positionAt?.Invoke(bossUid, cast.TMs) : null)
                        ?? (primary.X, primary.Y);

                    active.Add(new ActiveZone(zone, cast, bossUid != 0 ? bossUid : primary.Uid, centre.X, centre.Y, telegraphing));
                    continue;
                }

                foreach (ReplayCastTarget target in cast.Targets)
                {
                    (double X, double Y) centre = zone.Anchor == ZoneAnchor.Target
                        ? positionAt?.Invoke(target.Uid, nowMs) ?? (target.X, target.Y)
                        : (target.X, target.Y);

                    active.Add(new ActiveZone(zone, cast, target.Uid, centre.X, centre.Y, telegraphing));
                }
            }
        }

        return active;
    }

    /// <summary>
    /// The zone's outline in WORLD space, ready for the caller to project onto its map. A radially
    /// symmetric shape (circle / donut) returns closed loops — outer boundary first, then the inner hole;
    /// a directional one (cone / line) is rotated by the caster's facing at cast PLUS the shape's own
    /// rotation, which is how a 4-way quadrant mechanic ships as four cones at 0/90/180/270.
    /// </summary>
    public static List<List<(double X, double Y)>> Outline(in ActiveZone active, int arcSteps = 48)
    {
        ReplaySkillZone z = active.Zone;
        double facing = z.IsDirectional
            ? (active.Cast.FacingDeg + z.RotationDeg) * Math.PI / 180.0
            : 0;

        // The client's offsets live in the caster's local frame: forward = (cos, sin) along the facing,
        // right = (sin, -cos), its perpendicular.
        double cos = Math.Cos(facing), sin = Math.Sin(facing);
        (double X, double Y) Local(double ox, double oy, double fwd, double right)
            => (ox + fwd * cos + right * sin, oy + fwd * sin - right * cos);

        (double cx, double cy) = Local(active.CentreX, active.CentreY, z.OffsetForward, z.OffsetRight);

        var loops = new List<List<(double X, double Y)>>();
        switch (z.Kind)
        {
            case "Rectangle":
            {
                // A beam: starts at the anchor, runs Radius (= length) forward and Width across.
                double halfW = z.Width / 2;
                loops.Add(
                [
                    Local(cx, cy, 0, -halfW),
                    Local(cx, cy, z.Radius, -halfW),
                    Local(cx, cy, z.Radius, halfW),
                    Local(cx, cy, 0, halfW),
                ]);
                break;
            }

            case "Arc":
            case "RingArc":
            {
                double half = z.AngleDeg / 2 * Math.PI / 180.0;
                var pts = new List<(double X, double Y)>();
                if (z.Kind == "RingArc" && z.InnerRadius > 0)
                {
                    // A band between two radii: out along one edge, back along the other.
                    AppendArc(pts, cx, cy, z.Radius, facing - half, facing + half, arcSteps);
                    AppendArc(pts, cx, cy, z.InnerRadius, facing + half, facing - half, arcSteps);
                }
                else
                {
                    pts.Add((cx, cy)); // the cone's apex sits on the caster
                    AppendArc(pts, cx, cy, z.Radius, facing - half, facing + half, arcSteps);
                }

                loops.Add(pts);
                break;
            }

            default: // Circle / Sphere / Ring
            {
                var outer = new List<(double X, double Y)>();
                AppendArc(outer, cx, cy, z.Radius, 0, 2 * Math.PI, arcSteps);
                loops.Add(outer);

                if (z.InnerRadius > 0)
                {
                    var inner = new List<(double X, double Y)>();
                    AppendArc(inner, cx, cy, z.InnerRadius, 0, 2 * Math.PI, arcSteps);
                    loops.Add(inner); // the safe hole
                }

                break;
            }
        }

        return loops;
    }

    /// <summary>
    /// Which way the boss faced at a playback time, in degrees — read off its casts (every cast states the
    /// caster's facing, and a boss casts near-continuously). Held from the nearest cast within
    /// <paramref name="maxAgeMs"/>; null when the boss hasn't cast anywhere near this moment, so the caller
    /// can hide the head/back markers rather than point them at a stale direction.
    /// </summary>
    public static double? FacingAt(IReadOnlyList<ReplayCast> casts, double nowMs, double maxAgeMs = 6000)
    {
        double bestAge = maxAgeMs;
        double? facing = null;
        foreach (ReplayCast c in casts)
        {
            double age = Math.Abs(nowMs - c.TMs);
            if (age <= bestAge)
            {
                bestAge = age;
                facing = c.FacingDeg;
            }
        }

        return facing;
    }

    private static void AppendArc(
        List<(double X, double Y)> into, double cx, double cy, double r, double from, double to, int steps)
    {
        for (int i = 0; i <= steps; i++)
        {
            double a = from + (to - from) * i / steps;
            into.Add((cx + r * Math.Cos(a), cy + r * Math.Sin(a)));
        }
    }
}

using WaffleMeter.Replay;
using Xunit;

namespace WaffleMeter.Replay.Tests;

/// <summary>
/// Spec for the boss-mechanic playback rules: WHEN a zone is on screen (telegraph → hit), WHERE it sits
/// (fixed on the ground vs. stuck to the player it marked), and HOW it is oriented (a cone/line rotates
/// with the boss's facing at cast). Shared with the web player, so this is the contract.
/// </summary>
public class ReplayZonesTests
{
    private static readonly ReplaySkillShapes Shapes = ReplaySkillShapes.Parse("""
        {
          "111":[{"i":1,"t":"Circle","v":[0,0,0,0,500,500],"n":3000,"a":"target"}],
          "222":[{"i":1,"t":"Circle","v":[0,0,0,0,700,400],"n":1500,"a":"targetLoc"}],
          "333":[{"i":1,"t":"Rectangle","v":[0,0,0,0,3000,600,400,0],"n":2000,"a":"caster"}],
          "444":[{"i":1,"t":"Ring","v":[0,0,0,0,1400,700,500],"n":400,"a":"caster"}],
          "555":[{"i":1,"t":"Arc","v":[0,0,0,90,1000,60,300],"n":1000,"a":"caster"}]
        }
        """);

    private static ReplayCast Cast(int skill, int tMs, float facing = 0f, params ReplayCastTarget[] targets)
        => new() { SkillCode = skill, TMs = tMs, FacingDeg = facing, Targets = targets };

    [Fact]
    public void A_zone_shows_for_its_telegraph_then_the_hit_then_clears()
    {
        // A marker with a 3 s telegraph, cast at t=10 s.
        var casts = new[] { Cast(111, 10_000, targets: new ReplayCastTarget(7, 0, 0, 0)) };

        Assert.Empty(ReplayZones.ActiveAt(casts, Shapes, 6_500)); // before the floor lights up
        Assert.True(ReplayZones.ActiveAt(casts, Shapes, 8_000)[0].Telegraphing); // warning
        Assert.False(ReplayZones.ActiveAt(casts, Shapes, 10_200)[0].Telegraphing); // landed
        Assert.Empty(ReplayZones.ActiveAt(casts, Shapes, 11_500)); // gone again
    }

    [Fact]
    public void An_instant_mechanic_still_gets_a_moment_on_screen()
    {
        // notice=400 ms would flash by; the player leads it a little so a scrub can catch it.
        var casts = new[] { Cast(444, 5_000, targets: new ReplayCastTarget(9, 0, 0, 0)) };

        Assert.NotEmpty(ReplayZones.ActiveAt(casts, Shapes, 4_700)); // within MinLeadMs
        Assert.Empty(ReplayZones.ActiveAt(casts, Shapes, 4_400));
    }

    [Fact]
    public void A_marker_stuck_to_a_player_follows_them_a_ground_puddle_does_not()
    {
        // Both were cast on the player standing at (100, 100); by playback time they have run to (900, 100).
        var marker = new[] { Cast(111, 10_000, targets: new ReplayCastTarget(7, 100, 100, 0)) };
        var puddle = new[] { Cast(222, 10_000, targets: new ReplayCastTarget(7, 100, 100, 0)) };
        (double X, double Y)? Moved(int uid, double ms) => (900, 100);

        ActiveZone follows = Assert.Single(ReplayZones.ActiveAt(marker, Shapes, 9_500, Moved));
        Assert.Equal(900, follows.CentreX); // the marker rode along — you cannot outrun it

        ActiveZone stays = Assert.Single(ReplayZones.ActiveAt(puddle, Shapes, 9_500, Moved));
        Assert.Equal(100, stays.CentreX); // the puddle stayed on the ground — running out is the dodge
    }

    [Fact]
    public void A_spread_paints_one_zone_per_marked_player()
    {
        var casts = new[]
        {
            Cast(111, 10_000, targets:
            [
                new ReplayCastTarget(7, 100, 100, 0),
                new ReplayCastTarget(8, 500, 100, 0),
                new ReplayCastTarget(9, 900, 100, 0),
            ]),
        };

        List<ActiveZone> zones = ReplayZones.ActiveAt(casts, Shapes, 9_000);

        Assert.Equal(3, zones.Count);
        Assert.Equal([7, 8, 9], zones.Select(z => z.TargetUid));
    }

    [Fact]
    public void A_caster_zone_is_drawn_once_on_the_boss_no_matter_how_many_players_it_marked()
    {
        // A cast that marks three players but whose zone is anchored on the BOSS's body: the boss paints
        // one ring, not three. And it sits where the boss stands, not on the players it named.
        var casts = new[]
        {
            Cast(444, 10_000, targets:
            [
                new ReplayCastTarget(7, 100, 100, 0),
                new ReplayCastTarget(8, 500, 100, 0),
                new ReplayCastTarget(9, 900, 100, 0),
            ]),
        };
        (double X, double Y)? BossAt(int uid, double ms) => uid == 42 ? (-2000, -3000) : null;

        ActiveZone ring = Assert.Single(ReplayZones.ActiveAt(casts, Shapes, 10_000, BossAt, bossUid: 42));

        Assert.Equal(42, ring.TargetUid);
        Assert.Equal(-2000, ring.CentreX);
        Assert.Equal(-3000, ring.CentreY);
    }

    [Fact]
    public void A_caster_zone_falls_back_to_the_casts_own_position_without_a_boss_track()
    {
        // A self-cast carries the boss's own position, so the zone still lands correctly.
        var casts = new[] { Cast(444, 10_000, targets: new ReplayCastTarget(42, 700, 800, 0)) };

        ActiveZone ring = Assert.Single(ReplayZones.ActiveAt(casts, Shapes, 10_000));

        Assert.Equal(700, ring.CentreX);
        Assert.Equal(800, ring.CentreY);
    }

    [Fact]
    public void A_line_points_where_the_boss_faced()
    {
        // Facing 90° = +Y. The beam runs 3000 forward from the boss at the origin, 600 wide.
        var casts = new[] { Cast(333, 10_000, facing: 90f, targets: new ReplayCastTarget(1, 0, 0, 0)) };

        ActiveZone beam = Assert.Single(ReplayZones.ActiveAt(casts, Shapes, 9_000));
        List<(double X, double Y)> quad = Assert.Single(ReplayZones.Outline(beam));

        Assert.Equal(4, quad.Count);
        Assert.All(quad, p => Assert.InRange(p.X, -301, 301));   // half-width either side of the boss
        Assert.Contains(quad, p => p.Y > 2_990);                  // and it reaches 3000 up the +Y axis
        Assert.DoesNotContain(quad, p => p.Y < -1);               // never behind him
    }

    [Fact]
    public void A_cone_carries_its_own_rotation_on_top_of_the_facing()
    {
        // The shape row is rotated 90° (that's how a quadrant mechanic ships as four cones); the boss faces
        // 0°, so this cone must open along +Y, not +X.
        var casts = new[] { Cast(555, 10_000, facing: 0f, targets: new ReplayCastTarget(1, 0, 0, 0)) };

        ActiveZone cone = Assert.Single(ReplayZones.ActiveAt(casts, Shapes, 9_500));
        List<(double X, double Y)> pts = Assert.Single(ReplayZones.Outline(cone));

        Assert.Equal((0, 0), (pts[0].X, pts[0].Y)); // apex on the caster
        Assert.All(pts.Skip(1), p => Assert.True(p.Y > 800, $"the cone should open along +Y, got {p}"));
    }

    [Fact]
    public void A_donut_returns_its_hole_as_a_second_loop()
    {
        var casts = new[] { Cast(444, 10_000, targets: new ReplayCastTarget(1, 0, 0, 0)) };

        List<List<(double X, double Y)>> loops =
            ReplayZones.Outline(Assert.Single(ReplayZones.ActiveAt(casts, Shapes, 10_000)));

        Assert.Equal(2, loops.Count);
        Assert.All(loops[0], p => Assert.Equal(1400, Math.Sqrt(p.X * p.X + p.Y * p.Y), 0)); // outer
        Assert.All(loops[1], p => Assert.Equal(700, Math.Sqrt(p.X * p.X + p.Y * p.Y), 0));  // the safe hole
    }

    [Fact]
    public void An_uncatalogued_cast_draws_nothing()
    {
        var casts = new[] { Cast(999_999, 10_000, targets: new ReplayCastTarget(1, 0, 0, 0)) };

        Assert.Empty(ReplayZones.ActiveAt(casts, Shapes, 10_000));
    }
}

using WaffleMeter.Replay;
using Xunit;

namespace WaffleMeter.Replay.Tests;

/// <summary>Spec for the bundled boss-mechanic zone catalog: the client's raw EffectRangeValues must read
/// back as the right radius / inner hole / cone angle / beam width, and the anchor must survive.</summary>
public class ReplaySkillShapesTests
{
    // Real rows from the client (dungeon 붉은 연심의 거울 / 로타르 · 롭스티노 · 크로메데의 심연).
    private const string Json = """
        {
          "1806450":[{"i":2,"t":"Rectangle","v":[0,0,0,0,3000,600,400,0],"n":2000,"a":"caster"}],
          "1806420":[{"i":2,"t":"Circle","v":[0,0,0,0,700,400],"n":1500,"a":"targetLoc"},
                     {"i":3,"t":"Ring","v":[0,0,0,0,1400,700,500],"n":400,"a":"caster"},
                     {"i":4,"t":"Ring","v":[0,0,0,0,2100,1400,500],"n":400,"a":"caster"}],
          "1608080":[{"i":1,"t":"Arc","v":[0,0,0,0,3000,90,300],"n":0,"a":"caster"},
                     {"i":2,"t":"Arc","v":[0,0,0,90,3000,90,300],"n":0,"a":"caster"}],
          "1807111":[{"i":1,"t":"Circle","v":[0,0,0,0,500,500],"n":3000,"a":"target"}]
        }
        """;

    [Fact]
    public void Reads_a_line_mechanic()
    {
        ReplaySkillZone line = Assert.Single(ReplaySkillShapes.Parse(Json).For(1806450));

        Assert.Equal("Rectangle", line.Kind);
        Assert.Equal(3000, line.Radius); // length
        Assert.Equal(600, line.Width);
        Assert.Equal(2000, line.NoticeMs); // 2 s telegraph
        Assert.Equal(ZoneAnchor.Caster, line.Anchor);
        Assert.True(line.IsDirectional); // must be rotated by the boss's facing
    }

    [Fact]
    public void Reads_a_multi_stage_shockwave_in_cast_order()
    {
        IReadOnlyList<ReplaySkillZone> zones = ReplaySkillShapes.Parse(Json).For(1806420);

        Assert.Equal(3, zones.Count);
        Assert.Equal(ZoneAnchor.TargetLocation, zones[0].Anchor); // a puddle dropped where the player stood
        Assert.Equal(700, zones[0].Radius);

        Assert.Equal("Ring", zones[1].Kind);
        Assert.Equal(1400, zones[1].Radius);      // outer
        Assert.Equal(700, zones[1].InnerRadius);  // the safe hole
        Assert.Equal(2100, zones[2].Radius);      // the wave expands
        Assert.Equal(1400, zones[2].InnerRadius);
        Assert.False(zones[1].IsDirectional);     // a ring is radially symmetric
    }

    [Fact]
    public void Reads_a_quadrant_cone_and_its_own_rotation()
    {
        IReadOnlyList<ReplaySkillZone> arcs = ReplaySkillShapes.Parse(Json).For(1608080);

        Assert.Equal(3000, arcs[0].Radius);
        Assert.Equal(90, arcs[0].AngleDeg);
        Assert.Equal(0, arcs[0].RotationDeg);
        Assert.Equal(90, arcs[1].RotationDeg); // the second quadrant is the same cone, rotated
        Assert.True(arcs[1].IsDirectional);
    }

    [Fact]
    public void Reads_a_player_attached_marker()
    {
        ReplaySkillZone mark = Assert.Single(ReplaySkillShapes.Parse(Json).For(1807111));

        Assert.Equal(ZoneAnchor.Target, mark.Anchor); // follows the marked player
        Assert.Equal(500, mark.Radius);
        Assert.Equal(3000, mark.NoticeMs);
    }

    [Fact]
    public void Reads_a_triangle_wedge()
    {
        // A wedge: reach in V4, opening angle in V5 (like an Arc), a straight base — must rotate with facing.
        ReplaySkillZone tri = Assert.Single(ReplaySkillShapes.Parse(
            """{"1601900":[{"i":1,"t":"Triangle","v":[0,0,0,0,1500,90,250,0],"n":1300,"a":"caster"}]}""").For(1601900));

        Assert.Equal("Triangle", tri.Kind);
        Assert.Equal(1500, tri.Radius);   // reach (apex -> base)
        Assert.Equal(90, tri.AngleDeg);   // opening angle
        Assert.True(tri.IsDirectional);
    }

    [Fact]
    public void Reads_a_cross_of_two_bars()
    {
        // A Cross row is [.., lenA(4), lenB(5), height(6), widthA(7), widthB(8)] — two perpendicular bars.
        ReplaySkillZone cross = Assert.Single(ReplaySkillShapes.Parse(
            """{"1220300":[{"i":1,"t":"Cross","v":[0,0,0,45,600,700,200,150,250],"n":800,"a":"caster"}]}""").For(1220300));

        Assert.Equal("Cross", cross.Kind);
        Assert.Equal(600, cross.Radius);       // bar A length (along facing)
        Assert.Equal(700, cross.CrossArmB);    // bar B length (across)
        Assert.Equal(150, cross.CrossWidthA);  // bar A thickness
        Assert.Equal(250, cross.CrossWidthB);  // bar B thickness
        Assert.True(cross.IsDirectional);
    }

    [Fact]
    public void The_base_code_fallback_refuses_a_family_that_disagrees_on_kind()
    {
        // 1230120 is a Rectangle but its sibling 1230125 is an Arc — the trailing digit is a different mechanic,
        // not a stage. So an ABSENT sibling must NOT inherit the base's beam; a consistent family still falls back.
        ReplaySkillShapes shapes = ReplaySkillShapes.Parse("""
            {
              "1230120":[{"i":1,"t":"Rectangle","v":[0,0,0,0,3000,600,400,0],"n":0,"a":"caster"}],
              "1230125":[{"i":1,"t":"Arc","v":[0,0,0,0,1000,60,300],"n":0,"a":"caster"}],
              "1806450":[{"i":1,"t":"Rectangle","v":[0,0,0,0,3000,600,400,0],"n":0,"a":"caster"}]
            }
            """);

        Assert.Empty(shapes.For(1230121));                 // ambiguous base -> no guess
        Assert.Equal("Arc", Assert.Single(shapes.For(1230125)).Kind); // the present sibling is unaffected
        Assert.Equal("Rectangle", Assert.Single(shapes.For(1806451)).Kind); // consistent family still falls back
    }

    [Fact]
    public void A_variant_of_a_mechanic_borrows_the_zone_from_its_base_code()
    {
        // The client defines a mechanic's zone once, on the base code; a cast may arrive as a stage/level
        // variant of it (…1, …3). Live, that left 36 casts of a 침식의 정화소 boss drawing nothing.
        ReplaySkillShapes shapes = ReplaySkillShapes.Parse(Json);

        Assert.Equal(shapes.For(1806450), shapes.For(1806451)); // same zone, not an empty list
        Assert.Equal(3000, Assert.Single(shapes.For(1806453)).Radius);
    }

    [Fact]
    public void An_uncatalogued_or_non_zone_skill_yields_no_zones()
    {
        Assert.Empty(ReplaySkillShapes.Parse(Json).For(1234567)); // a plain melee swing
        Assert.Empty(ReplaySkillShapes.Parse(Json).For(1234560)); // …and its base isn't catalogued either
        Assert.Empty(ReplaySkillShapes.Empty.For(1806450));
    }

    [Fact]
    public void A_corrupt_catalog_is_survivable()
    {
        Assert.Equal(0, ReplaySkillShapes.Parse("{ this is not json").SkillCount);
        Assert.Equal(0, ReplaySkillShapes.Parse("""{"1":[{"i":1,"t":"Circle","v":[0,0]}]}""").SkillCount); // too few values
    }
}

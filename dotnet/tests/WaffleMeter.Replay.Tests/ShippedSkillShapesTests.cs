using System.Text.Json;
using WaffleMeter.Replay;
using Xunit;

namespace WaffleMeter.Replay.Tests;

/// <summary>
/// Guards the SHIPPED boss-mechanic zone catalog (Assets/json/skill-shapes.json). Every other test in this
/// file's neighbours feeds the parser synthetic JSON, so nothing checked the real asset — a regeneration that
/// leaked player skills, an unrenderable shape kind, or a row with too few value slots for its kind would ship
/// silently and the replay would paint the wrong floor (or none). Regenerate with
/// <c>dotnet/tools/skill-shapes-export.py</c> against a fresh client datamine.
/// </summary>
public sealed class ShippedSkillShapesTests
{
    /// <summary>Kinds <see cref="ReplayZones.Outline"/> draws deliberately. Anything else falls into its
    /// default arm and is silently painted as a plain circle.</summary>
    private static readonly HashSet<string> Renderable =
        ["Circle", "Sphere", "Ring", "RingArc", "Arc", "Rectangle", "Triangle", "Cross"];

    // 7-digit codes are the mob/boss band; player skills are 8-digit and must never reach this catalog.
    private const int MobSkillMin = 1_000_000;
    private const int MobSkillMax = 2_999_999;

    // The exporter drops floor decals bigger than a dungeon room; a value past this means the gate broke.
    private const double ExtentCut = 25_000;

    private static string AssetsDir()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Assets", "json", "skill-shapes.json")))
            {
                return Path.Combine(dir.FullName, "Assets");
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Assets/json/skill-shapes.json not found above " + AppContext.BaseDirectory);
    }

    private static Dictionary<string, JsonElement[]> Raw() =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement[]>>(
            File.ReadAllText(Path.Combine(AssetsDir(), "json", "skill-shapes.json")))!;

    [Fact]
    public void Every_entry_survives_the_parser()
    {
        Dictionary<string, JsonElement[]> raw = Raw();
        ReplaySkillShapes shapes = ReplaySkillShapes.Load(AssetsDir());

        // A silent parser dropout (bad kind string, short value array) would show up as a shortfall here.
        Assert.Equal(raw.Count, shapes.SkillCount);
        Assert.True(shapes.SkillCount > 4000, $"catalog collapsed to {shapes.SkillCount} skills");

        foreach ((string code, JsonElement[] entries) in raw)
        {
            Assert.NotEmpty(entries);
            Assert.Equal(entries.Length, shapes.For(int.Parse(code)).Count);
        }
    }

    [Fact]
    public void Holds_only_mob_skill_codes()
    {
        string[] offenders = [.. Raw().Keys.Where(k => !int.TryParse(k, out int c) || c is < MobSkillMin or > MobSkillMax)];

        Assert.Empty(offenders);
    }

    [Fact]
    public void Every_zone_is_renderable_and_fully_dimensioned()
    {
        ReplaySkillShapes shapes = ReplaySkillShapes.Load(AssetsDir());
        List<string> bad = [];

        foreach (string code in Raw().Keys)
        {
            foreach (ReplaySkillZone z in shapes.For(int.Parse(code)))
            {
                void Require(bool ok, string why)
                {
                    if (!ok)
                    {
                        bad.Add($"{code} {z.Kind}: {why} [{string.Join(",", z.Values)}]");
                    }
                }

                Require(Renderable.Contains(z.Kind), "kind not handled by the renderer");
                Require(z.Radius > 0, "primary extent <= 0");
                Require(Math.Max(z.Radius, z.CrossArmB) < ExtentCut, "extent past the map-decal cut");

                switch (z.Kind)
                {
                    case "Ring" or "RingArc":
                        Require(z.InnerRadius >= 0 && z.InnerRadius < z.Radius, "inner radius not inside the outer");
                        break;
                    case "Rectangle":
                        Require(z.Width > 0, "beam has no width");
                        break;
                    case "Cross":
                        Require(z.CrossArmB > 0 && z.CrossWidthA > 0 && z.CrossWidthB > 0, "a cross bar is degenerate");
                        break;
                }

                if (z.Kind is "Arc" or "Triangle" or "RingArc")
                {
                    Require(z.AngleDeg is > 0 and <= 360, "cone angle outside (0, 360]");
                }
            }
        }

        Assert.Empty(bad);
    }
}

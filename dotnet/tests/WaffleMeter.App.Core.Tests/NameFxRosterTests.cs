using WaffleMeter.App.Core;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

/// <summary>
/// The roster is a decoration fed by a document this build did not write. Every test here is about the same
/// property: a malformed, stale or newer document must degrade to "nobody has an effect", never to an
/// exception — a supporter badge is not worth a meter that will not start.
/// </summary>
public sealed class NameFxRosterTests
{
    private const long Now = 1_800_000_000_000; // fixed clock; the roster must never read the wall clock itself

    private static bool KnownName(string id) => id is "syrup" or "goldleaf";

    private static bool KnownGauge(string id) => id is "prism";

    [Fact]
    public void Reads_the_optional_gauge_skin()
    {
        NameFxRoster r = NameFxRoster.Parse(
            """{"schemaVersion":1,"entries":[{"h":"AA","e":"goldleaf","k":"ranker","g":"prism"}]}""", Now, KnownName, KnownGauge);

        Assert.Equal("prism", r.Find("AA")?.GaugeId);
    }

    [Fact]
    public void An_unknown_gauge_does_not_take_the_nickname_effect_down_with_it()
    {
        // The gauge field was added after the first release. A grant naming a gauge this build cannot draw must
        // still deliver the nickname effect — dropping the whole entry would punish the user for our staleness.
        NameFxRoster r = NameFxRoster.Parse(
            """{"schemaVersion":1,"entries":[{"h":"AA","e":"goldleaf","g":"gauge-from-the-future"}]}""", Now, KnownName, KnownGauge);

        NameFxEntry? e = r.Find("AA");
        Assert.NotNull(e);
        Assert.Equal("goldleaf", e!.EffectId);
        Assert.Null(e.GaugeId);
    }

    [Fact]
    public void A_gauge_id_in_the_nickname_slot_is_rejected()
    {
        // The two slots take ids from two different tables, and one shared "is it in the catalogue" predicate
        // let a gauge id through here — where it resolves to a real brush and paints a bar-sized gradient
        // across a nickname. The asymmetry only shows up in this direction, so it needs its own test.
        NameFxRoster r = NameFxRoster.Parse(
            """{"schemaVersion":1,"entries":[{"h":"AA","e":"prism"}]}""", Now, KnownName, KnownGauge);

        Assert.Equal(0, r.Count);
    }

    [Fact]
    public void A_nickname_effect_id_in_the_gauge_slot_is_rejected()
    {
        NameFxRoster r = NameFxRoster.Parse(
            """{"schemaVersion":1,"entries":[{"h":"AA","e":"syrup","g":"goldleaf"}]}""", Now, KnownName, KnownGauge);

        Assert.Equal("syrup", r.Find("AA")?.EffectId);
        Assert.Null(r.Find("AA")?.GaugeId);
    }

    [Fact]
    public void A_grant_without_a_gauge_is_normal()
    {
        NameFxRoster r = NameFxRoster.Parse(
            """{"schemaVersion":1,"entries":[{"h":"AA","e":"syrup"}]}""", Now, KnownName, KnownGauge);

        Assert.Null(r.Find("AA")?.GaugeId);
    }

    [Fact]
    public void Parses_entries_and_finds_by_hash()
    {
        NameFxRoster r = NameFxRoster.Parse(
            """{"schemaVersion":1,"entries":[{"h":"AABB","e":"syrup","k":"supporter","x":0}]}""", Now, KnownName, KnownGauge);

        Assert.Equal(1, r.Count);
        NameFxEntry? e = r.Find("AABB");
        Assert.NotNull(e);
        Assert.Equal("syrup", e!.EffectId);
        Assert.Equal("supporter", e.Kind);
    }

    [Fact]
    public void Hash_lookup_is_case_insensitive()
    {
        // The hash is lowercase hex from StatsIdentity, but a hand-edited document is a real input.
        NameFxRoster r = NameFxRoster.Parse(
            """{"schemaVersion":1,"entries":[{"h":"aabb","e":"syrup"}]}""", Now, KnownName, KnownGauge);

        Assert.NotNull(r.Find("AABB"));
    }

    [Fact]
    public void Drops_entries_whose_effect_this_build_cannot_draw()
    {
        // A newer server may grant an effect added after this client shipped. Rendering nothing is right;
        // rendering a fallback colour would misrepresent which grant the character actually holds.
        NameFxRoster r = NameFxRoster.Parse(
            """{"schemaVersion":1,"entries":[{"h":"AA","e":"syrup"},{"h":"BB","e":"effect-from-the-future"}]}""",
            Now, KnownName, KnownGauge);

        Assert.Equal(1, r.Count);
        Assert.Null(r.Find("BB"));
    }

    [Fact]
    public void Drops_expired_entries_at_load()
    {
        NameFxRoster r = NameFxRoster.Parse(
            $$"""{"schemaVersion":1,"entries":[{"h":"AA","e":"syrup","x":{{Now - 1}}},{"h":"BB","e":"syrup","x":{{Now + 1}}}]}""",
            Now, KnownName, KnownGauge);

        Assert.Null(r.Find("AA"));
        Assert.NotNull(r.Find("BB"));
    }

    [Fact]
    public void Expiry_still_bites_later_in_a_long_session()
    {
        // Loaded while valid, then the lease runs out without the app restarting.
        NameFxRoster r = NameFxRoster.Parse(
            $$"""{"schemaVersion":1,"entries":[{"h":"AA","e":"syrup","x":{{Now + 1000}}}]}""", Now, KnownName, KnownGauge);

        Assert.NotNull(r.Find("AA", Now));
        Assert.Null(r.Find("AA", Now + 2000));
    }

    [Fact]
    public void Zero_expiry_means_no_expiry()
    {
        NameFxRoster r = NameFxRoster.Parse(
            """{"schemaVersion":1,"entries":[{"h":"AA","e":"syrup","x":0}]}""", Now, KnownName, KnownGauge);

        Assert.NotNull(r.Find("AA", Now + 999_999_999));
    }

    [Fact]
    public void Refuses_a_document_newer_than_this_build_understands()
    {
        // The one change that must reach clients BEFORE the server publishes it. Refusing whole beats
        // rendering half a document whose meaning we are guessing at.
        NameFxRoster r = NameFxRoster.Parse(
            """{"schemaVersion":99,"entries":[{"h":"AA","e":"syrup"}]}""", Now, KnownName, KnownGauge);

        Assert.Same(NameFxRoster.Empty, r);
    }

    [Fact]
    public void Ignores_unknown_fields_so_additive_changes_do_not_break_old_clients()
    {
        NameFxRoster r = NameFxRoster.Parse(
            """{"schemaVersion":1,"note":"hi","entries":[{"h":"AA","e":"syrup","tier":"gold","note":2}]}""",
            Now, KnownName, KnownGauge);

        Assert.Equal(1, r.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("{}")]
    [InlineData("""{"schemaVersion":1}""")]
    [InlineData("""{"schemaVersion":1,"entries":[]}""")]
    [InlineData("""{"schemaVersion":1,"entries":[{"e":"syrup"}]}""")]
    public void Bad_documents_yield_an_empty_roster_without_throwing(string json)
    {
        Assert.Equal(0, NameFxRoster.Parse(json, Now, KnownName, KnownGauge).Count);
    }

    [Fact]
    public void Missing_file_yields_an_empty_roster()
    {
        string dir = Path.Combine(Path.GetTempPath(), "wm_namefx_" + Guid.NewGuid().ToString("N"));
        Assert.Equal(0, NameFxRoster.Load(dir, Now, KnownName, KnownGauge).Count);
    }

    [Fact]
    public void Loads_from_the_user_data_folder()
    {
        string dir = Path.Combine(Path.GetTempPath(), "wm_namefx_" + Guid.NewGuid().ToString("N"));
        try
        {
            string path = NameFxRoster.FilePath(dir);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, """{"schemaVersion":1,"entries":[{"h":"AA","e":"goldleaf","k":"ranker"}]}""");

            NameFxRoster r = NameFxRoster.Load(dir, Now, KnownName, KnownGauge);
            Assert.Equal("goldleaf", r.Find("AA")?.EffectId);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* temp */ }
        }
    }
}

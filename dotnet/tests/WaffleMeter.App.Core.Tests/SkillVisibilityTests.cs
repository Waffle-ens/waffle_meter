using WaffleMeter.App.Core;
using WaffleMeter.Services;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

/// <summary>
/// The picker's whole job is to still be there tomorrow, so every test here spans a restart: write through one
/// <see cref="SkillVisibility"/>, then read through a second one over the same properties file.
///
/// <para>This class shipped with no tests at all, and the defect it hid was not subtle — a size heuristic threw
/// away any selection under 40 codes on every single load. It survived because nothing ever round-tripped a
/// realistic selection. That is the shape of test this file owes: not "does Set() mutate the set", but "does a
/// selection a real person would make come back".</para>
/// </summary>
public sealed class SkillVisibilityTests : IDisposable
{
    private readonly string _dir;
    private readonly PropertyHandler _props;

    public SkillVisibilityTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "waffle_skillvis_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _props = new PropertyHandler(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // A temp dir that outlives the run is not worth failing a green suite over.
        }
    }

    /// <summary>A second instance over the same file — i.e. what the next launch sees.</summary>
    private SkillVisibility AfterRestart() => new(new PropertyHandler(_dir));

    private static int[] Catalog => SkillCatalog.DefaultVisibleCodes.ToArray();

    [Fact]
    public void First_run_shows_every_skill()
    {
        var vis = new SkillVisibility(_props);

        Assert.Equal(Catalog.Length, vis.Codes.Count);
        Assert.All(Catalog, c => Assert.True(vis.IsVisible(c)));
    }

    /// <summary>
    /// The reported bug, pinned. Ten codes is far under the old &lt;40 threshold, so this is the exact case that
    /// came back as 전체 선택.
    /// </summary>
    [Fact]
    public void A_small_selection_survives_a_restart()
    {
        int[] keep = Catalog.Take(10).ToArray();

        var first = new SkillVisibility(_props);
        first.SetMany(Catalog.Except(keep), visible: false);
        Assert.Equal(10, first.Codes.Count);

        SkillVisibility second = AfterRestart();

        Assert.Equal(10, second.Codes.Count);
        Assert.Equal(keep.OrderBy(c => c), second.Codes.OrderBy(c => c));
    }

    /// <summary>
    /// The old threshold was 40 and job groups are 17–20 skills, so "keep two whole jobs" (up to 39) sat just
    /// under the cliff. Sweeping across it is what turns a fixed constant into a regression fence.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(18)]
    [InlineData(39)]
    [InlineData(40)]
    [InlineData(41)]
    public void Selections_of_any_size_survive_a_restart(int keepCount)
    {
        int[] keep = Catalog.Take(keepCount).ToArray();

        var first = new SkillVisibility(_props);
        first.SetMany(Catalog.Except(keep), visible: false);

        Assert.Equal(keepCount, AfterRestart().Codes.Count);
    }

    /// <summary>Deselecting everything used to serialise as "", which read back as "never configured".</summary>
    [Fact]
    public void Deselecting_everything_survives_a_restart()
    {
        var first = new SkillVisibility(_props);
        first.SetMany(Catalog, visible: false);
        Assert.Empty(first.Codes);

        Assert.Empty(AfterRestart().Codes);
    }

    [Fact]
    public void Selecting_everything_again_survives_a_restart()
    {
        var first = new SkillVisibility(_props);
        first.SetMany(Catalog, visible: false);
        first.SetMany(Catalog, visible: true);

        Assert.Equal(Catalog.Length, AfterRestart().Codes.Count);
    }

    /// <summary>
    /// Storing the complement is what buys this: a skill added by a later patch is in nobody's hidden-list, so
    /// it is visible without anyone having to notice the catalogue grew. The old kept-list had no way to say
    /// this, which is what the "catalogue grew → reset everything" heuristic was groping for.
    /// </summary>
    [Fact]
    public void A_skill_added_after_the_user_chose_is_visible()
    {
        // A file written by an older build: the user hid everything the catalogue held AT THE TIME, which we
        // model by hiding all but the last five codes and calling those five "the ones a later patch added".
        int[] addedLater = Catalog.TakeLast(5).ToArray();
        int[] knownThen = Catalog.Except(addedLater).ToArray();
        _props.SetProperty("joinSkills.hidden", string.Join(",", knownThen));

        SkillVisibility vis = AfterRestart();

        // Nothing had to notice the catalogue grew: absence from the hidden list IS visibility.
        Assert.Equal(addedLater.OrderBy(c => c), vis.Codes.OrderBy(c => c));
        Assert.All(addedLater, c => Assert.True(vis.IsVisible(c)));
    }

    [Fact]
    public void The_hidden_list_is_what_reaches_disk()
    {
        int[] hide = Catalog.Take(3).ToArray();

        var vis = new SkillVisibility(_props);
        vis.SetMany(hide, visible: false);

        Assert.Equal(hide.OrderBy(c => c), ParseHidden().OrderBy(c => c));
        Assert.Null(new PropertyHandler(_dir).GetProperty("visibleSkillCodes"));
    }

    /// <summary>
    /// The upgrade path. A pre-2.10.4 file holds the kept list — and because the old guard reset without ever
    /// writing, a selection it had been ignoring is still sitting there. Converting it restores that choice
    /// rather than confirming the loss.
    /// </summary>
    [Fact]
    public void A_legacy_selection_is_carried_over_and_the_old_key_is_dropped()
    {
        int[] keep = Catalog.Take(12).ToArray();
        _props.SetProperty("visibleSkillCodes", string.Join(",", keep));

        var vis = new SkillVisibility(new PropertyHandler(_dir));

        Assert.Equal(ExpectedFromLegacy(keep), vis.Codes.OrderBy(c => c));

        var reread = new PropertyHandler(_dir);
        Assert.Null(reread.GetProperty("visibleSkillCodes"));   // one-shot: removed, so it cannot re-fire
        Assert.NotNull(reread.GetProperty("joinSkills.hidden"));
        Assert.Equal(ExpectedFromLegacy(keep), AfterRestart().Codes.OrderBy(c => c));
    }

    [Fact]
    public void A_legacy_full_selection_carries_over_as_nothing_hidden()
    {
        _props.SetProperty("visibleSkillCodes", string.Join(",", Catalog));

        var vis = new SkillVisibility(new PropertyHandler(_dir));

        Assert.Equal(Catalog.Length, vis.Codes.Count);
        Assert.Empty(ParseHidden());
    }

    /// <summary>Codes the catalogue has since dropped must not be resurrected into the visible set.</summary>
    [Fact]
    public void A_legacy_selection_naming_unknown_codes_ignores_them()
    {
        int[] keep = Catalog.Take(4).ToArray();
        _props.SetProperty("visibleSkillCodes", string.Join(",", keep) + ",12345678,0,junk");

        var vis = new SkillVisibility(new PropertyHandler(_dir));

        Assert.Equal(ExpectedFromLegacy(keep), vis.Codes.OrderBy(c => c));
    }

    /// <summary>
    /// The pre-2.0 build wrote this key as JSON — <c>JSON.stringify(codes)</c> into the very same
    /// waffle_meter.v1.4/settings.properties. Splitting on ',' alone eats the bracketed first and last entry,
    /// which for a short list means the whole selection parses to nothing.
    /// </summary>
    [Fact]
    public void A_legacy_selection_in_the_pre_2_0_json_format_carries_over_whole()
    {
        int[] keep = Catalog.Take(6).ToArray();
        _props.SetProperty("visibleSkillCodes", "[" + string.Join(",", keep) + "]");

        var vis = new SkillVisibility(new PropertyHandler(_dir));

        Assert.Equal(ExpectedFromLegacy(keep), vis.Codes.OrderBy(c => c));
        Assert.Equal(ExpectedFromLegacy(keep), AfterRestart().Codes.OrderBy(c => c));
    }

    [Fact]
    public void A_legacy_json_selection_of_a_single_skill_carries_over()
    {
        int only = Catalog[0];
        _props.SetProperty("visibleSkillCodes", "[" + only + "]");

        Assert.Equal(ExpectedFromLegacy(new[] { only }),
            new SkillVisibility(new PropertyHandler(_dir)).Codes.OrderBy(c => c));
    }

    /// <summary>An old build's 전체 해제 serialised as "". Present-but-empty is a choice, not "never set".</summary>
    [Fact]
    public void A_legacy_empty_selection_falls_back_to_everything_visible()
    {
        _props.SetProperty("visibleSkillCodes", string.Empty);

        var vis = new SkillVisibility(new PropertyHandler(_dir));

        Assert.Equal(Catalog.Length, vis.Codes.Count);
        Assert.Equal(Catalog.Length, AfterRestart().Codes.Count);
        Assert.Null(new PropertyHandler(_dir).GetProperty("visibleSkillCodes")); // must not leak into exports
    }

    /// <summary>
    /// A value we could not read must not be persisted as "hide everything" — that writes a blank picker that
    /// looks like a deliberate choice and destroys the original in the same write.
    /// </summary>
    [Fact]
    public void A_legacy_selection_we_cannot_read_falls_back_to_everything_visible()
    {
        _props.SetProperty("visibleSkillCodes", "12345678,87654321");

        var vis = new SkillVisibility(new PropertyHandler(_dir));

        Assert.Equal(Catalog.Length, vis.Codes.Count);
        Assert.Null(new PropertyHandler(_dir).GetProperty("visibleSkillCodes")); // still one-shot
        Assert.Equal(Catalog.Length, AfterRestart().Codes.Count);
    }

    /// <summary>A settings import writes the file underneath us, then calls Reload. The set must be updated in
    /// place — <c>JoinRequestViewModel</c> and the picker hold this very instance — and Changed must fire.</summary>
    [Fact]
    public void Reload_replaces_the_set_in_place_and_announces_it()
    {
        var vis = new SkillVisibility(_props);
        HashSet<int> handedOut = vis.Codes;
        int fired = 0;
        vis.Changed += () => fired++;

        // Through the same handler the import uses: SettingsBundleApplier writes into this very instance and
        // then calls Reload, so the handler's map is the thing that has to be re-read (not the file behind it).
        int[] imported = Catalog.Take(7).ToArray();
        _props.SetProperty("joinSkills.hidden", string.Join(",", Catalog.Except(imported)));

        vis.Reload();

        Assert.Same(handedOut, vis.Codes);
        Assert.Equal(imported.OrderBy(c => c), vis.Codes.OrderBy(c => c));
        Assert.Equal(1, fired);
    }

    /// <summary>An import from a pre-2.10.4 build writes the old key; Reload has to convert it too.</summary>
    [Fact]
    public void Reload_carries_over_a_legacy_key_written_by_an_import()
    {
        var vis = new SkillVisibility(_props);
        int[] imported = Catalog.Take(6).ToArray();
        _props.SetProperty("visibleSkillCodes", string.Join(",", imported));

        vis.Reload();

        Assert.Equal(ExpectedFromLegacy(imported), vis.Codes.OrderBy(c => c));
        Assert.Null(new PropertyHandler(_dir).GetProperty("visibleSkillCodes"));
    }

    [Fact]
    public void Set_persists_a_single_toggle()
    {
        int code = Catalog[0];

        var first = new SkillVisibility(_props);
        first.Set(code, visible: false);

        SkillVisibility second = AfterRestart();
        Assert.False(second.IsVisible(code));
        Assert.Equal(Catalog.Length - 1, second.Codes.Count);
    }

    private int[] ParseHidden()
    {
        string? raw = new PropertyHandler(_dir).GetProperty("joinSkills.hidden");
        return string.IsNullOrWhiteSpace(raw)
            ? Array.Empty<int>()
            : raw.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
    }

    /// <summary>What a legacy kept-list must convert to now: the codes it named, PLUS every 권성 skill when it
    /// named none. That job joined the catalogue after these files were written, so its absence is not a
    /// choice - see SkillVisibility.LoadInto.</summary>
    private static IEnumerable<int> ExpectedFromLegacy(IEnumerable<int> kept)
    {
        var set = new HashSet<int>(kept);
        if (!set.Any(c => c / 1_000_000 == 19))
        {
            foreach (int code in Catalog.Where(c => c / 1_000_000 == 19))
            {
                set.Add(code);
            }
        }

        return set.OrderBy(c => c);
    }

    /// <summary>
    /// The reported bug (2026-08-23). A kept-list written before 권성 shipped names 11-18 and nothing else;
    /// converting it as a plain complement marks all 19 권성 skills hidden, so that user's join panel never
    /// shows a 권성 applicant's badges again. The picker only dims a hidden chip, which nobody reads as "off".
    /// </summary>
    [Fact]
    public void A_legacy_list_from_before_the_last_job_shipped_keeps_that_job_visible()
    {
        int[] preFighter = Catalog.Where(c => c / 1_000_000 is >= 11 and <= 18).ToArray();
        _props.SetProperty("visibleSkillCodes", string.Join(",", preFighter));

        var vis = new SkillVisibility(new PropertyHandler(_dir));

        Assert.Equal(Catalog.Length, vis.Codes.Count);
        Assert.All(Catalog.Where(c => c / 1_000_000 == 19), code => Assert.Contains(code, vis.Codes));
        Assert.Empty(ParseHidden());
    }

    /// <summary>The rescue stays narrow: a list that DOES mention that job has an opinion about it, and the
    /// skills it left out stay hidden.</summary>
    [Fact]
    public void A_legacy_list_that_mentions_the_last_job_is_taken_at_its_word()
    {
        int[] fighter = Catalog.Where(c => c / 1_000_000 == 19).ToArray();
        int[] keep = Catalog.Where(c => c / 1_000_000 is >= 11 and <= 18).Concat(fighter.Take(3)).ToArray();
        _props.SetProperty("visibleSkillCodes", string.Join(",", keep));

        var vis = new SkillVisibility(new PropertyHandler(_dir));

        Assert.Equal(keep.OrderBy(c => c), vis.Codes.OrderBy(c => c));
        Assert.DoesNotContain(fighter[^1], vis.Codes);
    }

    /// <summary>Turning one job off is a real choice and must survive the conversion - the rescue must not
    /// generalise into "any job with no codes comes back".</summary>
    [Fact]
    public void A_legacy_list_that_turned_a_job_off_keeps_it_off()
    {
        int[] keep = Catalog.Where(c => c / 1_000_000 != 12).ToArray();
        _props.SetProperty("visibleSkillCodes", string.Join(",", keep));

        var vis = new SkillVisibility(new PropertyHandler(_dir));

        Assert.DoesNotContain(Catalog.First(c => c / 1_000_000 == 12), vis.Codes);
    }

    /// <summary>
    /// A settings bundle can carry the legacy key. Re-running the conversion would overwrite a selection made
    /// since the upgrade and re-apply the sender's losses. The current key wins; the stale one is dropped.
    /// </summary>
    [Fact]
    public void A_legacy_key_arriving_next_to_an_existing_selection_is_discarded()
    {
        _props.SetProperty("joinSkills.hidden", Catalog[0].ToString());
        _props.SetProperty("visibleSkillCodes", string.Join(",", Catalog.Take(3)));

        var vis = new SkillVisibility(new PropertyHandler(_dir));

        Assert.Equal(Catalog.Length - 1, vis.Codes.Count);
        Assert.DoesNotContain(Catalog[0], vis.Codes);
        Assert.Null(new PropertyHandler(_dir).GetProperty("visibleSkillCodes"));
    }
}

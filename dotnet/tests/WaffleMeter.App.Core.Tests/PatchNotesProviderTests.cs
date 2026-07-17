using WaffleMeter.App.Core;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

/// <summary>Covers PatchNotesProvider — pulling one version's section out of the bundled RELEASE_NOTES.md for
/// the one-time post-update popup.</summary>
public sealed class PatchNotesProviderTests
{
    private const string Notes =
        "# 패치노트 v2.7.6 (2026-07-18)\n" +
        "\n" +
        "## [추가] 기능 A\n" +
        "- 항목 1\n" +
        "- 항목 2\n" +
        "\n" +
        "## [수정] 버그 B\n" +
        "- 항목 3\n" +
        "\n" +
        "\n" +
        "# 패치노트 v2.7.5 (2026-07-17)\n" +
        "\n" +
        "## [추가] 옛기능\n" +
        "- 옛항목\n";

    [Theory]
    [InlineData("2.7.6", "2.7.6")]
    [InlineData("2.7.6-dev", "2.7.6")]      // dev build → base matches the release notes
    [InlineData("2.7.6+abc123", "2.7.6")]   // SDK +commit metadata stripped
    [InlineData("  2.7.6  ", "2.7.6")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void BaseVersion_strips_prerelease_and_metadata(string? input, string expected)
    {
        Assert.Equal(expected, PatchNotesProvider.BaseVersion(input));
    }

    [Fact]
    public void SectionForVersion_returns_only_that_version_body_without_the_heading()
    {
        string? body = PatchNotesProvider.SectionForVersion(Notes, "2.7.6");

        Assert.NotNull(body);
        Assert.DoesNotContain("# 패치노트", body);   // heading line excluded
        Assert.Contains("## [추가] 기능 A", body);
        Assert.Contains("항목 1", body);
        Assert.Contains("## [수정] 버그 B", body);
        Assert.Contains("항목 3", body);
        Assert.DoesNotContain("옛기능", body!);       // stops at the next version section
        Assert.DoesNotContain("옛항목", body!);
    }

    [Fact]
    public void SectionForVersion_matches_a_dev_build_to_the_release_section()
    {
        string? body = PatchNotesProvider.SectionForVersion(Notes, "2.7.6-dev");

        Assert.NotNull(body);
        Assert.Contains("기능 A", body!);
    }

    [Fact]
    public void SectionForVersion_returns_null_for_a_version_with_no_section()
    {
        Assert.Null(PatchNotesProvider.SectionForVersion(Notes, "2.7.4"));
    }

    [Fact]
    public void SectionForVersion_does_not_match_a_longer_version_token()
    {
        // "v2.7.6" must not match a "v2.7.60" heading — a whole-token boundary check.
        const string longer = "# 패치노트 v2.7.60 (2026-07-18)\n\n## [추가] X\n- Y\n";
        Assert.Null(PatchNotesProvider.SectionForVersion(longer, "2.7.6"));
        Assert.NotNull(PatchNotesProvider.SectionForVersion(longer, "2.7.60"));
    }

    [Fact]
    public void SectionForVersion_handles_the_last_section_and_crlf()
    {
        // The oldest section (no trailing "# ") + Windows line endings.
        string crlf = Notes.Replace("\n", "\r\n");
        string? body = PatchNotesProvider.SectionForVersion(crlf, "2.7.5");

        Assert.NotNull(body);
        Assert.Contains("옛기능", body!);
        Assert.DoesNotContain("기능 A", body!);
    }

    [Fact]
    public void SectionForVersion_null_on_empty_inputs()
    {
        Assert.Null(PatchNotesProvider.SectionForVersion("", "2.7.6"));
        Assert.Null(PatchNotesProvider.SectionForVersion(Notes, ""));
        Assert.Null(PatchNotesProvider.SectionForVersion(null, "2.7.6"));
    }
}

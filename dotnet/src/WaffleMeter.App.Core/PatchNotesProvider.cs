namespace WaffleMeter.App.Core;

/// <summary>
/// Pulls ONE version's section out of the bundled <c>RELEASE_NOTES.md</c> — pure/testable string work, kept
/// out of the WPF project (the App loads the embedded file and hands the text in). RELEASE_NOTES.md is a
/// concatenation of <c>"# 패치노트 vX.Y.Z (date)"</c> sections, newest first; each body is <c>"## [태그] 제목"</c>
/// sub-headings + <c>"- "</c> bullets. Used by the one-time post-update patch-note popup.
/// </summary>
public static class PatchNotesProvider
{
    /// <summary>The base <c>"X.Y.Z"</c> of a running version string — drops a <c>-dev</c>/<c>-rc</c> prerelease
    /// suffix and any <c>+commit</c> metadata, so a dev build (<c>"2.7.6-dev"</c>) still matches the
    /// <c>"v2.7.6"</c> notes.</summary>
    public static string BaseVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return "";
        }

        int cut = version.IndexOfAny(['-', '+']);
        return (cut >= 0 ? version[..cut] : version).Trim();
    }

    /// <summary>The markdown body of the <c>"# 패치노트 v{version}"</c> section (heading line excluded), trimmed —
    /// or <c>null</c> when that version has no section. Matches on the base version and on a whole version token
    /// (so <c>v2.7.6</c> never matches <c>v2.7.60</c>).</summary>
    public static string? SectionForVersion(string? releaseNotesMarkdown, string? version)
    {
        if (string.IsNullOrWhiteSpace(releaseNotesMarkdown))
        {
            return null;
        }

        string baseVer = BaseVersion(version);
        if (baseVer.Length == 0)
        {
            return null;
        }

        string[] lines = releaseNotesMarkdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        int start = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            if (IsTopHeading(lines[i]) && HeadingMatchesVersion(lines[i], baseVer))
            {
                start = i + 1;
                break;
            }
        }

        if (start < 0)
        {
            return null;
        }

        int end = lines.Length;
        for (int i = start; i < lines.Length; i++)
        {
            if (IsTopHeading(lines[i]))
            {
                end = i;
                break;
            }
        }

        string body = string.Join("\n", lines[start..end]).Trim();
        return body.Length > 0 ? body : null;
    }

    /// <summary>A top-level <c>"# "</c> heading (a version section start), not a <c>"## "</c> sub-heading.</summary>
    private static bool IsTopHeading(string line)
    {
        string t = line.TrimStart();
        return t.StartsWith("# ", StringComparison.Ordinal);
    }

    /// <summary>True when the heading contains the version as a WHOLE token: <c>v{baseVer}</c> followed by a
    /// non-<c>[0-9.]</c> char (space, <c>(</c>, end), so <c>v2.7.6</c> does not match <c>v2.7.60</c> or
    /// <c>v2.7.61</c>.</summary>
    private static bool HeadingMatchesVersion(string heading, string baseVer)
    {
        string token = "v" + baseVer;
        int from = 0;
        while (true)
        {
            int idx = heading.IndexOf(token, from, StringComparison.Ordinal);
            if (idx < 0)
            {
                return false;
            }

            int after = idx + token.Length;
            char next = after < heading.Length ? heading[after] : ' ';
            if (!char.IsDigit(next) && next != '.')
            {
                return true;
            }

            from = idx + 1;
        }
    }
}

using System.Text.RegularExpressions;
using WaffleMeter.App.Core;
using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

/// <summary>
/// The packs are addressed by a hash of the spoken line, so a wording change orphans every clip for that line
/// — and does it silently: the alert still fires, just a beat late over the network, or as a chime offline.
/// Nothing in the build joins the wording to the rendered files.
///
/// <para>So these tests reconstruct what the app can actually say — from the same catalogues the app reads,
/// and through the same filters the app applies — and require a clip to exist for every one. Reading the
/// source is the same trick <see cref="SettingsKeyCatalogTests"/> uses, and for the same reason: a
/// hand-maintained duplicate of the wording would drift with the thing it is supposed to be checking.</para>
///
/// <para><b>Why the buff axis is derived from buff_names.json and not buff_catalog.json.</b> It used to read
/// the latter, and that was the whole failure. buff_catalog.json is the ~70-entry curated subset the picker
/// lists <i>before</i> anything is observed; the picker's actual rows are
/// <c>observed ∪ curated</c>, labelled out of <c>buff_names.json</c> (433 entries). Deriving the expected
/// lines from the curated file meant the test asked the packs to contain exactly what the packs had been
/// baked from — it re-confirmed the bake instead of checking it, and 살성 '환영 분신' and 300-odd others
/// fell straight through to the network. The set below is the picker's real upper bound.</para>
/// </summary>
public sealed class ShippedVoicePackTests
{
    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "dotnet", "Assets", "voice")))
        {
            dir = dir.Parent;
        }

        return dir ?? throw new InvalidOperationException("dotnet/Assets/voice 를 찾지 못했습니다.");
    }

    private static string RepoPath(params string[] parts) => Path.Combine(RepoRoot().FullName, Path.Combine(parts));

    private static string VoiceRoot() => RepoPath("dotnet", "Assets", "voice");

    private static string ReadSource(params string[] parts) => File.ReadAllText(RepoPath(parts));

    /// <summary>Every line the app can speak from a fixed catalogue, built the way the app builds it.</summary>
    private static IEnumerable<string> ExpectedLines()
    {
        foreach (int lead in new[] { 10, 5, 1 })
        {
            yield return $"슈고 페스타, {lead}분 뒤 시작합니다";
            yield return $"감시자 카이라, {lead}분 뒤 정각 출현 가능";
        }

        yield return "슈고 페스타, 지금 시작합니다";
        yield return "감시자 카이라, 지금 정각 출현 가능";

        // A custom alarm's title is free text and cannot be baked, but the default title is not: the composer
        // in AlarmToastViewModel substitutes "알람" for a blank one, and the 새 알람 field is pre-filled with
        // it, so this exact line is the default path rather than an edge case.
        yield return "알람, 지금입니다";

        foreach (string boss in FieldBossNames())
        {
            foreach (int lead in new[] { 5, 10, 30 })
            {
                yield return $"{SpokenName.Of(boss)}, {lead}분 뒤 리젠";
            }
        }

        foreach (string buff in BuffNames())
        {
            yield return $"{buff} 온";
            yield return $"{buff} 오프";
        }
    }

    private static IEnumerable<string> FieldBossNames()
    {
        string src = ReadSource("dotnet", "src", "WaffleMeter.Capture", "FieldBossCatalog.cs");
        string body = src[src.IndexOf("Bosses =", StringComparison.Ordinal)
                          ..src.IndexOf("public static bool HasOwnAlarm", StringComparison.Ordinal)];
        const string Hourly = "2600089"; // 감시자 카이라 has its own cue
        return Regex.Matches(body, @"new\(\s*(\d+),\s*""([^""]+)""")
            .Where(m => m.Groups[1].Value != Hourly)
            .Select(m => m.Groups[2].Value)
            .Distinct(StringComparer.Ordinal);
    }

    /// <summary>
    /// The skill-icon codes that ship as PNGs. Read out of the source because <c>SkillIconManifest</c> is
    /// internal to App.Wpf, which this project does not reference — the same reason FieldBossNames parses text.
    /// </summary>
    private static HashSet<int> IconCodes()
    {
        string src = ReadSource("dotnet", "src", "WaffleMeter.App.Wpf", "SkillIconManifest.cs");
        string body = src[src.IndexOf("Codes = new()", StringComparison.Ordinal)..];
        return Regex.Matches(body, @"\b(\d{7,9})\b").Select(m => int.Parse(m.Groups[1].Value)).ToHashSet();
    }

    /// <summary>
    /// Every buff name the picker can offer a voice for. Mirrors two filters the app applies, in order:
    /// the row must have a bundled icon (<c>BuffPickerViewModel</c> skips the rest), and its label comes from
    /// the buff-name table. This is deliberately the upper bound — some of these codes never actually land on
    /// the local player, so a clip for them is dead weight rather than a miss, and over-covering is the safe
    /// direction for a test whose job is to catch silence.
    /// </summary>
    private static IEnumerable<string> BuffNames()
    {
        HashSet<int> icons = IconCodes();
        bool HasIcon(int code) =>
            icons.Contains(code)
            || (code is >= 11_000_000 and <= 19_999_999 && icons.Contains(code / 10_000 * 10_000));

        var names = ReferenceJson.LoadBuffNames(RepoPath("dotnet", "Assets", "json", "buff_names.json"))
            .Where(x => HasIcon(x.Code) && !string.IsNullOrEmpty(x.Name))
            .Select(x => x.Name)
            .ToList();

        // 회생의 계약's cooldown rider is registered into the same name table at RUNTIME under a synthesized
        // code (base + 7), so it reaches the picker and the voice path exactly like a real buff while being
        // absent from every JSON on disk. The name is a constant, so it is enumerable here — it just has to
        // be added by hand.
        names.Add("회계·회복");

        return names.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Lines that are known to have no clip yet. Rendering a pack needs the local voice model, so the gap is
    /// closed in batches; this file is what keeps the test a regression guard in the meantime instead of one
    /// permanently red assertion. A line is removed from here the moment it is baked — the tests below
    /// enforce both directions so it cannot rot into a place where misses go to be forgotten.
    /// </summary>
    private static HashSet<string> UnbakedBaseline()
    {
        string path = Path.Combine(VoiceRoot(), "_source", "unbaked.txt");
        if (!File.Exists(path))
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        return File.ReadAllLines(path)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static bool HasClip(string pack, string line) =>
        File.Exists(Path.Combine(VoiceRoot(), pack, BakedVoicePack.FileNameFor(line)));

    [Fact]
    public void Every_line_the_app_can_speak_has_a_clip_in_every_pack()
    {
        HashSet<string> unbaked = UnbakedBaseline();
        string[] missing = (from pack in BakedVoicePack.All
                            from line in ExpectedLines().Distinct(StringComparer.Ordinal)
                            where !HasClip(pack, line) && !unbaked.Contains(line)
                            select $"{pack} / {line}").ToArray();

        Assert.True(missing.Length == 0,
            $"음성 팩에 없는 문구 {missing.Length}건 — 문구를 바꿨다면 그 줄을 다시 구워야 합니다.\n  "
            + string.Join("\n  ", missing.Take(10)));
    }

    /// <summary>The baseline is an admission of a gap, not a suppression list: once a line is baked into both
    /// packs it has to leave, or the next rename of that line would land in a hole nobody is watching.</summary>
    [Fact]
    public void The_unbaked_baseline_lists_nothing_that_is_already_baked()
    {
        string[] stale = UnbakedBaseline()
            .Where(line => BakedVoicePack.All.All(pack => HasClip(pack, line)))
            .ToArray();

        Assert.True(stale.Length == 0,
            $"이미 구워진 문구가 unbaked.txt 에 남아 있습니다 {stale.Length}건 — 지워 주세요.\n  "
            + string.Join("\n  ", stale.Take(10)));
    }

    /// <summary>A baseline entry that no longer matches a line the app can say means the wording moved and the
    /// entry was left behind — exactly the drift the hash addressing makes invisible.</summary>
    [Fact]
    public void The_unbaked_baseline_lists_nothing_the_app_can_no_longer_say()
    {
        var speakable = ExpectedLines().ToHashSet(StringComparer.Ordinal);
        string[] orphaned = UnbakedBaseline().Where(l => !speakable.Contains(l)).ToArray();

        Assert.True(orphaned.Length == 0,
            $"앱이 더 이상 말하지 않는 문구가 unbaked.txt 에 있습니다 {orphaned.Length}건 — 문구가 바뀌었는지 확인하세요.\n  "
            + string.Join("\n  ", orphaned.Take(10)));
    }

    /// <summary>A clip nothing asks for is dead weight in the installer, and usually the fossil of an old
    /// wording that was re-rendered under a new key.</summary>
    [Fact]
    public void No_pack_carries_a_clip_nothing_asks_for()
    {
        var wanted = ExpectedLines().Select(BakedVoicePack.FileNameFor).ToHashSet(StringComparer.Ordinal);
        foreach (string pack in BakedVoicePack.All)
        {
            string[] orphans = Directory.GetFiles(Path.Combine(VoiceRoot(), pack), "*.mp3")
                .Select(Path.GetFileName)
                .Where(f => !wanted.Contains(f!))
                .ToArray()!;
            Assert.True(orphans.Length == 0, $"{pack}: 아무도 찾지 않는 클립 {orphans.Length}개");
        }
    }

    /// <summary>The preview button is the one place a user hears the pack on purpose; a near-miss there
    /// demonstrates the online fallback while looking like it worked.</summary>
    [Fact]
    public void The_voice_preview_button_speaks_a_line_the_pack_contains()
    {
        string src = ReadSource("dotnet", "src", "WaffleMeter.App.Wpf", "SettingsViewModel.cs");
        Match m = Regex.Match(src, @"TestTts\(\)\s*=>\s*TtsSpeech\.Speak\(""([^""]+)""");
        Assert.True(m.Success, "TestTts 의 발화 문구를 찾지 못했습니다 — 아래 정규식을 함께 고치세요.");
        Assert.Contains(m.Groups[1].Value, ExpectedLines());
        Assert.All(BakedVoicePack.All, pack => Assert.True(HasClip(pack, m.Groups[1].Value)));
    }

    /// <summary>Clips are all short single utterances; an empty or truncated render would slip past a
    /// file-exists check.</summary>
    [Fact]
    public void No_clip_is_empty_or_absurdly_short()
    {
        foreach (string pack in BakedVoicePack.All)
        {
            foreach (string f in Directory.GetFiles(Path.Combine(VoiceRoot(), pack), "*.mp3"))
            {
                Assert.True(new FileInfo(f).Length > 2000, $"{pack}/{Path.GetFileName(f)} 가 너무 작습니다");
            }
        }
    }

    [Fact]
    public void Both_packs_hold_the_same_lines()
    {
        var sets = BakedVoicePack.All
            .Select(p => Directory.GetFiles(Path.Combine(VoiceRoot(), p), "*.mp3")
                .Select(Path.GetFileName).ToHashSet(StringComparer.Ordinal))
            .ToArray();
        Assert.Equal(sets[0].Count, sets[1].Count);
        Assert.Empty(sets[0].Except(sets[1]));
    }
}

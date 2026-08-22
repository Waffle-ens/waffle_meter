using System.Text.Json;
using System.Text.RegularExpressions;
using WaffleMeter.App.Core;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

/// <summary>
/// The packs are addressed by a hash of the spoken line, so a wording change orphans every clip for that line
/// — and does it silently: the alert still fires, just a beat late over the network, or as a chime offline.
/// Nothing in the build joins the wording to the rendered files.
///
/// <para>So these tests reconstruct what the app can actually say — from the same catalogues the app reads,
/// and from the format strings in <c>AlarmToastViewModel</c> itself rather than a copy of them — and require a
/// clip to exist for every one. Reading the source is the same trick <see cref="SettingsKeyCatalogTests"/>
/// uses, and for the same reason: a hand-maintained duplicate of the wording would drift with the thing it is
/// supposed to be checking.</para>
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

    private static string VoiceRoot() => Path.Combine(RepoRoot().FullName, "dotnet", "Assets", "voice");

    private static string ReadSource(params string[] parts) =>
        File.ReadAllText(Path.Combine(RepoRoot().FullName, Path.Combine(parts)));

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

    private static IEnumerable<string> BuffNames()
    {
        string json = ReadSource("dotnet", "Assets", "json", "buff_catalog.json");
        using JsonDocument doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("buffs").EnumerateObject()
            .Select(p => p.Value.GetProperty("n").GetString()!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    [Fact]
    public void Every_line_the_app_can_speak_has_a_clip_in_every_pack()
    {
        string[] missing = (from pack in BakedVoicePack.All
                            let dir = Path.Combine(VoiceRoot(), pack)
                            from line in ExpectedLines()
                            let file = Path.Combine(dir, BakedVoicePack.FileNameFor(line))
                            where !File.Exists(file)
                            select $"{pack} / {line}").ToArray();

        Assert.True(missing.Length == 0,
            $"음성 팩에 없는 문구 {missing.Length}건 — 문구를 바꿨다면 그 줄을 다시 구워야 합니다.\n  "
            + string.Join("\n  ", missing.Take(10)));
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

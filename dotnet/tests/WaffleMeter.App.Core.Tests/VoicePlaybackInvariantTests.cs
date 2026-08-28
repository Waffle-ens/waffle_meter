using System.Text.RegularExpressions;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

/// <summary>
/// Two invariants of the voice playback path, checked by reading the source — the same trick
/// <see cref="ShippedVoicePackTests"/> and <see cref="SettingsKeyCatalogTests"/> use, and for the same reason:
/// what has to hold lives in <c>WaffleMeter.App.Wpf</c>, which no test project references, and a rule kept only
/// in a comment is a rule that comes back.
///
/// <para><b>Why these two.</b> "The voice alert cuts off near the end" was diagnosed and shipped as fixed twice.
/// The first fix (v2.11.2) added a grace period after <c>MediaEnded</c> and changed nothing, because the clip
/// was dying before <c>MediaEnded</c> ever fired: the player was a local inside a dispatcher lambda, so once
/// <c>Invoke</c> returned the only references to it were the delegates hanging off its own events — a cycle,
/// which is not a GC root — and any ephemeral collection landing inside the clip finalized the media handle and
/// stopped the sound mid-word. The second invariant is the same class of trap one layer down: the timer that
/// bounds the wait was a parameterless <c>DispatcherTimer</c>, which defaults to
/// <c>DispatcherPriority.Background</c> and is therefore starved by exactly the busy dispatcher it exists to
/// survive.</para>
/// </summary>
public sealed class VoicePlaybackInvariantTests
{
    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "dotnet", "src", "WaffleMeter.App.Wpf")))
        {
            dir = dir.Parent;
        }

        return dir ?? throw new InvalidOperationException("dotnet/src/WaffleMeter.App.Wpf 를 찾지 못했습니다.");
    }

    private static string WpfRoot() => Path.Combine(RepoRoot().FullName, "dotnet", "src", "WaffleMeter.App.Wpf");

    private static IEnumerable<string> WpfSources() =>
        Directory.EnumerateFiles(WpfRoot(), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    /// <summary>A playing <c>MediaPlayer</c> must be reachable from a static field, so build exactly one of them
    /// and park it in <c>TtsSpeech.Players</c>.</summary>
    [Fact]
    public void Every_MediaPlayer_is_built_in_TtsSpeech_and_kept_in_a_static_field()
    {
        // Both construction forms, because the code this guards used the object-initialiser one:
        // `new MediaPlayer { Volume = ... }` has no parentheses. `new MediaPlayer?[2]` is the field, not a player.
        var built = new Regex(@"new\s+MediaPlayer\s*(?:\(|\{|\r?\n\s*\{)");
        List<string> where = WpfSources()
            .SelectMany(f => built.Matches(File.ReadAllText(f)).Select(_ => Path.GetFileName(f)))
            .OfType<string>()
            .OrderBy(n => n)
            .ToList();

        Assert.True(
            where is ["TtsSpeech.cs"],
            "재생 중인 MediaPlayer 를 지역변수로 두지 마라 — 자기 이벤트 델리게이트를 통한 순환은 GC 루트가 아니라서 " +
            "클립이 재생 도중 수거되고, 소리가 그 자리에서 멎으며 MediaEnded 도 오지 않는다(2026-08-28. v2.11.2 가 " +
            "MediaEnded '이후'만 지키려다 헛짚은 바로 그 버그). MediaPlayer 는 TtsSpeech 에서만 만들고 static 필드에 " +
            $"담아 루트한다. 지금 만드는 곳: {string.Join(", ", built)}");

        string tts = File.ReadAllText(Path.Combine(WpfRoot(), "TtsSpeech.cs"));
        Assert.Contains("private static readonly MediaPlayer?[] Players", tts);
        Assert.Contains("Players[slot] = player;", tts);
    }

    /// <summary>The parameterless ctor defaults to <c>Background</c>, which sits behind Render and DataBind: a
    /// busy dispatcher starves the timer exactly when whatever it guards is needed. Spell the priority out.</summary>
    [Fact]
    public void No_DispatcherTimer_is_left_on_the_default_Background_priority()
    {
        List<string> bare = WpfSources()
            .Where(f => Regex.IsMatch(
                File.ReadAllText(f),
                @"new\s+(?:System\.Windows\.Threading\.)?DispatcherTimer\s*(?:\(\s*\)|\{|\r?\n\s*\{)"))
            .Select(Path.GetFileName)
            .OfType<string>()
            .OrderBy(n => n)
            .ToList();

        Assert.True(
            bare.Count == 0,
            "인자 없는 new DispatcherTimer() 는 DispatcherPriority.Background 로 떨어진다 — Render·DataBind 뒤라 " +
            "바쁜 디스패처에서 굶는다. 우선순위를 명시하라(음성 재생 워치독은 Normal). " +
            $"해당 파일: {string.Join(", ", bare)}");
    }
}

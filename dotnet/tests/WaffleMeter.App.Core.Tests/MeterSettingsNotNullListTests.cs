using System.Text.RegularExpressions;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

/// <summary>
/// <c>MeterSettings</c>' constructor delegates to <c>Reload</c>, which the compiler will not follow, so a
/// <c>[MemberNotNull]</c> list on <c>Reload</c> is what keeps two dozen non-nullable fields from each
/// reporting "maybe null after construction".
///
/// <para>The list is hand-maintained, and forgetting an entry costs one warning — which is exactly the
/// problem, because the list exists to stop fake warnings from burying real ones. It shipped that way in
/// v2.11.0: <c>_ttsVoice</c> was added without being listed, and the warning rode all the way into the
/// release build. This test makes the omission fail the suite instead.</para>
/// </summary>
public sealed class MeterSettingsNotNullListTests
{
    private static string Source()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "dotnet", "src", "WaffleMeter.App.Core", "MeterSettings.cs");
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("MeterSettings.cs 를 찾지 못했습니다 — 저장소 밖에서 실행됐습니다.");
    }

    [Fact]
    public void Every_reference_field_Reload_assigns_is_declared_not_null()
    {
        string src = Source();

        // 선언이 참조 타입인 것만 — 값 타입(bool/int/double)은 애초에 null 이 될 수 없어 목록에 필요 없다.
        var referenceFields = Regex.Matches(src, @"^\s*private (?:string|List<[^>]+>|Dictionary<[^>]+>)\??\s+(_\w+)\s*;",
                                            RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
        Assert.NotEmpty(referenceFields);

        // 널 허용으로 선언한 필드는 목록에 들어가면 안 되고, 들어갈 이유도 없다.
        var nullable = Regex.Matches(src, @"^\s*private (?:string|List<[^>]+>|Dictionary<[^>]+>)\?\s+(_\w+)\s*;",
                                     RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        string attr = Regex.Match(src, @"\[MemberNotNull\((.*?)\)\]", RegexOptions.Singleline).Groups[1].Value;
        Assert.False(string.IsNullOrWhiteSpace(attr), "[MemberNotNull] 목록을 찾지 못했습니다.");
        var listed = Regex.Matches(attr, @"nameof\((_\w+)\)")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        string reloadBody = src[src.IndexOf("public void Reload()", StringComparison.Ordinal)..];
        string[] missing = referenceFields
            .Except(nullable)
            .Where(f => Regex.IsMatch(reloadBody, $@"\b{Regex.Escape(f)}\s*="))   // Reload 가 실제로 채우는 것만
            .Where(f => !listed.Contains(f))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToArray();

        Assert.True(missing.Length == 0,
            "Reload 가 채우는데 [MemberNotNull] 목록에 없는 필드입니다. 빠지면 그 필드마다 가짜 경고가 하나씩 " +
            "생기고, 그게 진짜 경고를 묻습니다.\n  누락: " + string.Join(", ", missing));
    }
}

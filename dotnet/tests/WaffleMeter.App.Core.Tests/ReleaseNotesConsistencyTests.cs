using WaffleMeter.App.Core;
using WaffleMeter.Services;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

/// <summary>
/// 릴리스 정합성 가드. 앱은 업데이트 직후 <c>RELEASE_NOTES.md</c>(빌드 시 임베드)에서 <b>그 버전의 섹션</b>을
/// 찾아 패치노트 팝업을 띄우는데, 찾지 못하면 예외도 로그도 없이 조용히 넘어간다. 그래서 버전만 올리고 노트
/// 섹션을 빠뜨리거나 제목 형식을 흘리면 CI는 그린, 릴리스는 성공, 사용자에게는 아무 고지도 안 되는 실패가
/// 난다 — 배포하고 나서야 알게 되는 종류다.
/// <para>다른 패치노트 테스트들은 인라인 fixture만 파싱하므로 이 구멍을 못 막는다. 여기서는 저장소의 실제
/// 파일을 실제 버전 상수로 조회한다.</para>
/// </summary>
public sealed class ReleaseNotesConsistencyTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "RELEASE_NOTES.md")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir); // 저장소 밖에서 돌고 있다면 이 테스트의 전제가 깨진 것이다
        return dir!.FullName;
    }

    [Fact]
    public void The_shipping_version_has_a_patch_notes_section_the_app_can_find()
    {
        string root = RepoRoot();
        string notes = File.ReadAllText(Path.Combine(root, "RELEASE_NOTES.md"));

        string version = VersionConfig.Fallback; // 릴리스 커밋이 csproj와 함께 올리는 그 값
        string? section = PatchNotesProvider.SectionForVersion(notes, version);

        Assert.False(
            string.IsNullOrWhiteSpace(section),
            $"RELEASE_NOTES.md에 v{PatchNotesProvider.BaseVersion(version)} 섹션이 없거나 제목 형식이 달라 "
            + "앱이 찾지 못합니다. 제목은 '# 패치노트 v<버전> (날짜)' 형식이어야 합니다.");
    }

    [Fact]
    public void The_newest_section_is_the_shipping_version()
    {
        // 노트를 새로 쓰고 버전 bump를 빠뜨리면(또는 그 반대) 최상단 섹션과 출하 버전이 어긋난다.
        string notes = File.ReadAllText(Path.Combine(RepoRoot(), "RELEASE_NOTES.md"));
        string first = notes.Split('\n')[0].Trim();
        string expected = "v" + PatchNotesProvider.BaseVersion(VersionConfig.Fallback);

        Assert.True(
            first.Contains(expected, StringComparison.Ordinal),
            $"RELEASE_NOTES.md 최상단 제목('{first}')이 출하 버전({expected})과 다릅니다.");
    }

    [Fact]
    public void The_readme_update_log_mentions_the_shipping_version()
    {
        string readme = File.ReadAllText(Path.Combine(RepoRoot(), "README.md"));
        string expected = "v" + PatchNotesProvider.BaseVersion(VersionConfig.Fallback);

        Assert.Contains(expected, readme, StringComparison.Ordinal);
    }
}

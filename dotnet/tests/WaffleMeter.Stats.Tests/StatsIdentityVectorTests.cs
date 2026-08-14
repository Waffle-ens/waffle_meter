using WaffleMeter.Stats;
using Xunit;

namespace WaffleMeter.Stats.Tests;

/// <summary>
/// Cross-implementation vectors for the character identity hash.
/// <para>The same hash is computed independently by the meter (<see cref="StatsIdentity"/>) and by the stats web
/// (<c>src/shared/identity.ts</c> — <c>createIdentityHash</c>). Nothing in either build compares them, so a
/// divergence shows up as data that silently does not match: an upload attributed to nobody, or a supporter grant
/// that never lands. Both sides assert the SAME literals below; the twin file is
/// <c>tests/unit/identity-hash-vectors.test.ts</c> in the stats web repo. Change one, change both.</para>
/// <para>The meter is the authority. It produced the hashes already stored against every past upload, so where
/// the two normalisations disagree the server is what moves.</para>
/// </summary>
public sealed class StatsIdentityVectorTests
{
    [Theory]
    // 평범한 ASCII
    [InlineData(1, "Waffle", "ec12dde616310dff5cbfe382bdf9d8da66675efc205e66d32e5ff5b058f32d68")]
    // 한글 — 실제 닉네임의 대다수
    [InlineData(2, "와플메터", "9b8167de329f01afa8e5e73fbf6c8d2a2575530d5e78ee1707d35ab2c53689d8")]
    // 앞뒤 공백은 잘린다 (관리자가 복사-붙여넣기로 입력하는 경로가 여기에 걸린다)
    [InlineData(1, "  Waffle  ", "ec12dde616310dff5cbfe382bdf9d8da66675efc205e66d32e5ff5b058f32d68")]
    // 대소문자는 무시된다
    [InlineData(1, "WAFFLE", "ec12dde616310dff5cbfe382bdf9d8da66675efc205e66d32e5ff5b058f32d68")]
    [InlineData(7, "a1b2", "345ddf64e1001ebee1da312ffaa1ca4f6bf95f7b90af11917b300ab6a1808de6")]
    // 비ASCII 라틴 — 대소문자 매핑이 문화권 의존이 아님을 고정한다
    [InlineData(3, "Ωmega", "633e4b628a7dfa025ebfa8ac632b90f7dd80830c0dd757783b4344c2a8f976aa")]
    public void Vector(int server, string nickname, string expected)
    {
        Assert.Equal(expected, StatsIdentity.CharacterIdentityHash(server, nickname));
    }

    /// <summary>
    /// 두 런타임의 정규화가 실제로 갈리는 입력들(실측). 닉네임 출처가 패킷이고 mojibake 전력이 있으므로
    /// "도달 불가"로 단정하지 않는다. 갈린 결과는 예외가 아니라 <b>부여가 조용히 안 먹는 것</b>으로 나타나므로,
    /// 여기서 미터 쪽 동작을 고정해 두고 서버가 이쪽에 맞춘다.
    /// <para>문자는 코드로 만든다 — U+0085 는 C# 렉서에게도 줄바꿈이라 소스에 직접 적으면 문자열 상수가
    /// 그 자리에서 끊긴다(이 파일이 실제로 그렇게 한 번 깨졌다).</para>
    /// </summary>
    [Theory]
    // U+0085 NEL: .NET Trim 은 자르고, JS trim 은 자르지 않는다.
    [InlineData(0x0085, false, "a")]
    // U+FEFF BOM: .NET Trim 은 자르지 않고, JS trim 은 자른다.
    [InlineData(0xFEFF, true, null)]
    // U+200B ZWSP: 양쪽 다 남긴다 — 갈리지 않는 대조군.
    [InlineData(0x200B, false, null)]
    public void Normalisation_of_the_inputs_the_two_runtimes_disagree_about(int code, bool leading, string? expected)
    {
        char c = (char)code;
        string nickname = leading ? c + "A" : "A" + c;
        string normalised = expected ?? (leading ? c + "a" : "a" + c);

        Assert.Equal(
            StatsIdentity.Sha256($"{StatsIdentity.IdentityHashVersion}|1|{normalised}"),
            StatsIdentity.CharacterIdentityHash(1, nickname));
    }

    [Fact]
    public void Dotted_capital_I_is_left_alone_rather_than_decomposed()
    {
        // U+0130 İ. .NET ToLowerInvariant 는 그대로 두고, JS toLowerCase 는 "i" + U+0307 로 분해한다.
        // 같은 닉네임이 두 해시를 갖는다는 뜻이고, 위 셋 중 눈에 띄지 않기로는 이게 최악이다.
        string dotted = ((char)0x0130).ToString();

        Assert.Equal(
            StatsIdentity.Sha256($"{StatsIdentity.IdentityHashVersion}|1|{dotted}"),
            StatsIdentity.CharacterIdentityHash(1, dotted));
    }

    [Theory]
    [InlineData(0, "Waffle")]
    [InlineData(-1, "Waffle")]
    [InlineData(1, "")]
    [InlineData(1, "   ")]
    [InlineData(1, null)]
    public void Refuses_to_hash_an_incomplete_identity(int server, string? nickname)
    {
        // 서버 0/음수와 빈 닉네임은 null 이다. 여기서 아무 문자열이나 돌려주면 '알 수 없는 캐릭터' 전부가
        // 같은 해시로 뭉쳐 한 사람으로 보인다.
        Assert.Null(StatsIdentity.CharacterIdentityHash(server, nickname));
    }
}

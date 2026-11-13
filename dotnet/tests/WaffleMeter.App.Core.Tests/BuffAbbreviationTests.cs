using WaffleMeter.App.Core;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

/// <summary>
/// '스킬명 축약 알림'의 축약어 표(<see cref="BuffAbbreviation"/>). 표는 이름을 키로 쓰므로, 버프 이름이 바뀌면
/// 옛 키가 아무 말 없이 고아가 되고 새 이름은 축약 없이 읽힌다 — 그 두 방향을 출하 버프 목록과 대조해 잡는다.
/// 버프 목록은 <see cref="ShippedVoicePackTests.BuffNames"/> 와 같은 것(앱 로더 경유)을 쓴다.
/// </summary>
public sealed class BuffAbbreviationTests
{
    private static int HangulCount(string s) => s.Count(c => c is >= '가' and <= '힣');

    [Fact]
    public void Of_returns_the_abbreviation_from_the_table()
    {
        Assert.Equal("빙갑", BuffAbbreviation.Of("빙설의 갑주"));
        Assert.Equal("폭풍", BuffAbbreviation.Of("폭풍의 주문"));
        Assert.Equal("고정", BuffAbbreviation.Of("소환: 고대의 정령"));
    }

    [Theory]
    [InlineData("광분")] // 3글자 이하 — 축약 대상 아님
    [InlineData("지원 사격")] // 오너가 축약하지 않기로 함
    [InlineData("정밀")] // 스킬명(표적 화살)이 아니라 걸리는 버프 이름으로 카탈로그가 바뀐 행
    [InlineData("회생의 계약")]
    [InlineData("없는 버프 이름")]
    [InlineData("")]
    public void Of_returns_the_name_itself_when_it_has_no_abbreviation(string name) =>
        Assert.Equal(name, BuffAbbreviation.Of(name));

    [Fact]
    public void Every_abbreviation_key_is_a_current_buff_name()
    {
        var names = ShippedVoicePackTests.BuffNames().ToHashSet(StringComparer.Ordinal);
        string[] stale = BuffAbbreviation.All.Keys.Where(k => !names.Contains(k)).ToArray();

        Assert.True(stale.Length == 0,
            $"지금 버프 목록에 없는 축약어 키 {stale.Length}건 — 버프 이름이 바뀌었으면 표의 키도 새 이름으로: {string.Join(", ", stale)}");
    }

    [Fact]
    public void Every_exempt_entry_is_a_current_buff_name_and_not_also_abbreviated()
    {
        var names = ShippedVoicePackTests.BuffNames().ToHashSet(StringComparer.Ordinal);

        Assert.Empty(BuffAbbreviation.Exempt.Where(e => !names.Contains(e)));
        Assert.Empty(BuffAbbreviation.Exempt.Where(e => BuffAbbreviation.All.ContainsKey(e)));
    }

    /// <summary>공백·기호를 뺀 한글 4글자 이상인 이름은 표에 있거나, 오너가 줄이지 않기로 한 목록(Exempt)에
    /// 있어야 한다. 패치로 새 버프가 들어오면 여기서 걸린다 — 축약어는 오너가 정한다.</summary>
    [Fact]
    public void Every_long_buff_name_has_an_abbreviation()
    {
        string[] uncovered = ShippedVoicePackTests.BuffNames()
            .Where(n => HangulCount(n) >= 4)
            .Where(n => !BuffAbbreviation.All.ContainsKey(n) && !BuffAbbreviation.Exempt.Contains(n))
            .ToArray();

        Assert.True(uncovered.Length == 0,
            $"축약어가 없는 4글자 이상 버프 {uncovered.Length}건 — 오너에게 축약어를 받아 표에 넣거나 Exempt 에 넣으세요: {string.Join(", ", uncovered)}");
    }

    /// <summary>축약어끼리 겹치면 두 버프가 같은 클립을 쓰고, 다른 버프의 원래 이름과 겹치면 "그 버프 온"과
    /// 구별이 안 된다(처음 제안한 폭풍의 주문 → '폭주' 가 권성 '폭주' 와 바로 이렇게 부딪쳤다).</summary>
    [Fact]
    public void Abbreviations_are_unique_and_never_another_buffs_full_name()
    {
        string[] duplicated = BuffAbbreviation.All.Values
            .GroupBy(v => v, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();
        Assert.True(duplicated.Length == 0, $"겹치는 축약어: {string.Join(", ", duplicated)}");

        var names = ShippedVoicePackTests.BuffNames().ToHashSet(StringComparer.Ordinal);
        string[] clashing = BuffAbbreviation.All.Values.Where(names.Contains).ToArray();
        Assert.True(clashing.Length == 0, $"다른 버프의 원래 이름과 같은 축약어: {string.Join(", ", clashing)}");
    }

    [Fact]
    public void Abbreviation_is_shorter_than_the_name()
    {
        Assert.All(BuffAbbreviation.All, kv =>
        {
            Assert.False(string.IsNullOrWhiteSpace(kv.Value));
            Assert.True(HangulCount(kv.Value) < HangulCount(kv.Key), $"{kv.Key} → {kv.Value}");
        });
    }
}

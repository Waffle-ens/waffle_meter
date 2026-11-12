using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>
/// 아이콘 키 규칙(<see cref="IconKey"/>). App.Wpf 의 <c>JoinIcons.Skill</c>이 이 규칙으로 SkillIcons PNG를 고른다.
/// 9자리 경로가 표시 base에서 멈추면, 같은 base에서 빈 번호로 떼어 낸 표시 코드(전용 PNG 없음)가 상세 탭의
/// 버프 가동률 행·DPS 그래프 레인에서 아이콘 없이 그려진다 — 그 회귀를 막는다.
/// </summary>
public sealed class IconKeyTests
{
    private static Func<int, bool> Shipped(params int[] codes) => new HashSet<int>(codes).Contains;

    [Fact]
    public void An_exact_hit_wins()
    {
        Assert.Equal(17400058, IconKey.Skill(17400058, Shipped(17400058, 17400000)));
        Assert.Equal(11010047, IconKey.Skill(11010047, Shipped(11010047, 11010000)));
    }

    [Fact]
    public void A_skill_code_folds_to_its_floor_base()
    {
        Assert.Equal(15210000, IconKey.Skill(15210240, Shipped(15210000)));
        Assert.Equal(19790000, IconKey.Skill(19790007, Shipped(19790000))); // 회계·회복 합성 코드
    }

    [Fact]
    public void A_buff_code_uses_the_display_base_first()
    {
        // 대지의 축복: 표시 base에 전용 PNG가 있으면 그게 이기고, 대지의 징벌 아이콘으로 접히지 않는다.
        Assert.Equal(17400058, IconKey.Skill(174000571, Shipped(17400058, 17400000)));
    }

    [Theory]
    [InlineData(122400502, 12240000)] // 심판의 권능 → 심판
    [InlineData(121100371, 12110000)] // 보호의 방패
    [InlineData(130800371, 13080000)] // 회피 자세
    public void A_split_display_code_without_its_own_png_falls_to_the_floor_base(int runtime, int floor)
    {
        Assert.Equal(floor, IconKey.Skill(runtime, Shipped(floor)));
    }

    [Fact]
    public void An_ordinary_buff_code_resolves_as_before()
    {
        Assert.Equal(11780000, IconKey.Skill(117800071, Shipped(11780000)));
        // 표시 base가 이미 바닥이라 바닥 단계는 같은 코드를 두 번 내지 않는다.
        Assert.Equal(new[] { 11780000 }, IconKey.Fallbacks(117800071));
    }

    [Fact]
    public void No_png_anywhere_or_a_code_outside_the_job_bands_is_null()
    {
        Assert.Null(IconKey.Skill(122400502, Shipped()));
        Assert.Null(IconKey.Skill(22101051, Shipped(22100000)));
        Assert.Null(IconKey.Skill(0, Shipped(0)));
        Assert.Null(IconKey.Skill(-5, Shipped()));
    }
}

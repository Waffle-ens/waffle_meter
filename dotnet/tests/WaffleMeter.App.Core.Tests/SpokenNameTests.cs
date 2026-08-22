using WaffleMeter.App.Core;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

/// <summary>
/// An override here silently changes which pre-rendered clip a line resolves to, because the packs are keyed
/// on the spoken string. Adding one without re-rendering that line drops it onto the online voice — audible
/// as a stutter before the alert, not as an error. These tests keep the table honest about what it is.
/// </summary>
public sealed class SpokenNameTests
{
    [Fact]
    public void A_name_with_no_override_is_read_as_written()
    {
        Assert.Equal("썩은 쿠타르", SpokenName.Of("썩은 쿠타르"));
        Assert.Equal("감시자 카이라", SpokenName.Of("감시자 카이라"));
    }

    [Theory]
    [InlineData("별동대장 링크스", "별똥대장 링크스")]
    [InlineData("세 개의 뿔 마이노", "세개의 뿔 마이노")]
    public void A_name_that_reads_wrong_is_respelled(string display, string spoken)
        => Assert.Equal(spoken, SpokenName.Of(display));

    /// <summary>An override that changes nothing is a rendered clip wasted and a reader misled.</summary>
    [Fact]
    public void No_override_is_a_no_op()
        => Assert.All(SpokenName.All, kv => Assert.NotEqual(kv.Key, kv.Value));

    /// <summary>The point is to change the sound, not to rename the boss on screen — the overlay still shows
    /// the catalogue spelling, so an override that rewrote the name outright would desync the two.</summary>
    [Fact]
    public void An_override_keeps_roughly_the_same_length()
        => Assert.All(SpokenName.All, kv => Assert.InRange(kv.Value.Length, kv.Key.Length - 2, kv.Key.Length + 2));

    [Fact]
    public void Overrides_are_not_chained()
        => Assert.All(SpokenName.All, kv => Assert.Equal(kv.Value, SpokenName.Of(kv.Value)));
}

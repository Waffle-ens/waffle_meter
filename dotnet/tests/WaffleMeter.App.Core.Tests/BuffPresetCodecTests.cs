using WaffleMeter.App.Core;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

public sealed class BuffPresetCodecTests
{
    private static BuffPresetSet Sample() => new()
    {
        Active = 2,
        Slots =
        [
            new BuffPreset { Name = "레이드", Hidden = "17400058,17300001", Voice = "17400000", IconSize = 34, Transparent = false },
            new BuffPreset { Name = "딜링", IconSize = 80, TtsOnStart = true, TtsOnEnd = true, GrayOnCooldown = true, ShowOther = false },
            new BuffPreset { Name = "프리셋 3", TextColor = "#00FF88" },
        ],
    };

    [Fact]
    public void Round_trips_korean_names_and_code_sets()
    {
        BuffPresetSet? decoded = BuffPresetCodec.Decode(BuffPresetCodec.Encode(Sample()));

        Assert.NotNull(decoded);
        Assert.Equal(2, decoded.Active);
        Assert.Equal(3, decoded.Slots.Count);
        Assert.Equal("레이드", decoded.Slots[0].Name);
        Assert.Equal("17400058,17300001", decoded.Slots[0].Hidden);
        Assert.Equal("17400000", decoded.Slots[0].Voice);
        Assert.Equal(34, decoded.Slots[0].IconSize);
        Assert.False(decoded.Slots[0].Transparent);
        Assert.Equal("딜링", decoded.Slots[1].Name);
        Assert.Equal(80, decoded.Slots[1].IconSize); // 배율 상한(200%)이 공유코드를 왕복해도 살아남는지
        Assert.True(decoded.Slots[1].TtsOnStart);
        Assert.True(decoded.Slots[1].GrayOnCooldown);
        Assert.False(decoded.Slots[1].ShowOther);
        Assert.Equal("#00FF88", decoded.Slots[2].TextColor);
    }

    // The settings store re-decodes every value through Latin-1 -> EUC-KR on read, which replaces any char
    // above 0xFF with '?'. Pure-ASCII output is what makes the blob survive that; guard it.
    [Fact]
    public void Encoded_blob_is_pure_ascii()
    {
        string encoded = BuffPresetCodec.Encode(Sample());

        Assert.NotEmpty(encoded);
        Assert.All(encoded, c => Assert.InRange(c, (char)0x20, (char)0x7E));
    }

    // '스킬명 축약 알림'(TtsAbbrev)보다 먼저 저장된 프리셋 blob 에는 그 필드가 없다. 그런 blob 은 꺼짐으로
    // 읽혀야 한다 — 업데이트했다고 사용자가 고르지도 않은 축약 음성이 켜지면 안 된다.
    [Fact]
    public void A_preset_saved_before_the_abbreviation_toggle_reads_it_as_off()
    {
        const string legacyJson =
            "{\"Active\":0,\"Slots\":[{\"Name\":\"a\",\"TtsOnStart\":true,\"EndWarning3s\":true},{\"Name\":\"b\"},{\"Name\":\"c\"}]}";
        string blob = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(legacyJson));

        BuffPresetSet? decoded = BuffPresetCodec.Decode(blob);

        Assert.NotNull(decoded);
        Assert.True(decoded.Slots[0].TtsOnStart);
        Assert.True(decoded.Slots[0].EndWarning3s);
        Assert.All(decoded.Slots, slot => Assert.False(slot.TtsAbbrev));
    }

    [Fact]
    public void The_abbreviation_toggle_round_trips()
    {
        var set = new BuffPresetSet { Slots = [new BuffPreset { TtsAbbrev = true }, new BuffPreset(), new BuffPreset()] };

        BuffPresetSet? decoded = BuffPresetCodec.Decode(BuffPresetCodec.Encode(set));

        Assert.NotNull(decoded);
        Assert.True(decoded.Slots[0].TtsAbbrev);
        Assert.False(decoded.Slots[1].TtsAbbrev);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-base64!!")]
    [InlineData("bm90IGpzb24=")] // valid base64 of "not json"
    public void Decode_returns_null_for_absent_or_corrupt(string? raw) => Assert.Null(BuffPresetCodec.Decode(raw));
}

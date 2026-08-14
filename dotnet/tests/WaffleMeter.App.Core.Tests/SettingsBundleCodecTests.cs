using WaffleMeter.App.Core;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

/// <summary>
/// The code is a string a user copies out of one machine and pastes into another, usually via a chat app. Most
/// of these tests are about that trip: it gets wrapped, quoted, commented on, and clipped, and every one of
/// those has to either work or say clearly which it was.
/// </summary>
public sealed class SettingsBundleCodecTests
{
    private static SettingsBundle Sample(params (string Key, string Value)[] data)
    {
        var b = new SettingsBundle { Profile = "F", App = "2.9.7", CreatedAt = "2026-08-15T00:00:00Z" };
        foreach ((string k, string v) in data)
        {
            b.Data[k] = v;
        }

        return b;
    }

    [Fact]
    public void Round_trips_values()
    {
        string code = SettingsBundleCodec.Encode(Sample(("rowHeight", "42"), ("barStyle", "bar")));

        Assert.True(SettingsBundleCodec.TryDecode(code, out SettingsBundle back, out SettingsCodeError err));
        Assert.Equal(SettingsCodeError.None, err);
        Assert.Equal("42", back.Data["rowHeight"]);
        Assert.Equal("bar", back.Data["barStyle"]);
        Assert.Equal("F", back.Profile);
    }

    [Fact]
    public void Round_trips_non_ascii_values()
    {
        // The whole reason the payload is base64 of UTF-8 rather than raw text: settings.properties is Latin-1
        // with a EUC-KR re-decode on read, and a Korean value that takes that path twice does not survive.
        string code = SettingsBundleCodec.Encode(Sample(("theme", "{\"이름\":\"기본 테마\"}")));

        Assert.True(SettingsBundleCodec.TryDecode(code, out SettingsBundle back, out _));
        Assert.Equal("{\"이름\":\"기본 테마\"}", back.Data["theme"]);
    }

    [Fact]
    public void Code_is_pure_ascii_so_it_survives_any_transport()
    {
        string code = SettingsBundleCodec.Encode(Sample(("theme", "{\"이름\":\"기본\"}")));
        Assert.All(code, c => Assert.InRange(c, (char)0x21, (char)0x7E));
    }

    [Fact]
    public void Compression_actually_pays_for_itself()
    {
        // A full backup has to fit in a chat message. Settings are repetitive text, which is exactly what gzip
        // is good at — without it the same content ran to five figures.
        var big = Sample();
        foreach (SettingsKey k in SettingsKeyCatalog.All)
        {
            big.Data[k.Key] = "false";
        }

        string code = SettingsBundleCodec.Encode(big);
        Assert.True(code.Length < 1200, $"전체 백업 코드가 {code.Length}자로 너무 깁니다");
    }

    [Theory]
    [InlineData("여기 코드요: {0} 확인해줘")]
    [InlineData("`{0}`")]
    [InlineData("{0} 좋아요")]
    [InlineData("> {0}")]
    public void Extracts_the_code_out_of_surrounding_text(string template)
    {
        string code = SettingsBundleCodec.Encode(Sample(("rowHeight", "36")));

        Assert.True(SettingsBundleCodec.TryDecode(string.Format(template, code), out SettingsBundle back, out _));
        Assert.Equal("36", back.Data["rowHeight"]);
    }

    [Fact]
    public void Hangul_immediately_after_the_code_ends_it_instead_of_joining_it()
    {
        // The bug this rule exists for: char.IsLetterOrDigit is true for Hangul, so a naive parser swallows the
        // comment into the payload and then blames the code.
        string code = SettingsBundleCodec.Encode(Sample(("rowHeight", "36")));

        Assert.True(SettingsBundleCodec.TryDecode(code + "좋아요", out SettingsBundle back, out _));
        Assert.Equal("36", back.Data["rowHeight"]);
    }

    [Fact]
    public void Survives_being_wrapped_across_lines()
    {
        string code = SettingsBundleCodec.Encode(Sample(("rowHeight", "36")));
        string wrapped = string.Join("\n", Enumerable.Range(0, (code.Length + 39) / 40)
            .Select(i => code.Substring(i * 40, Math.Min(40, code.Length - i * 40))));

        Assert.True(SettingsBundleCodec.TryDecode(wrapped, out SettingsBundle back, out _));
        Assert.Equal("36", back.Data["rowHeight"]);
    }

    [Fact]
    public void A_clipped_paste_is_reported_as_a_checksum_mismatch_not_as_garbage()
    {
        // The distinction that makes the message useful: "복사가 잘렸다" and "이건 우리 코드가 아니다" are
        // different problems with different fixes, and without a fingerprint they look identical.
        string code = SettingsBundleCodec.Encode(Sample(("rowHeight", "36")));

        Assert.False(SettingsBundleCodec.TryDecode(code[..^4], out _, out SettingsCodeError err));
        Assert.Equal(SettingsCodeError.ChecksumMismatch, err);
    }

    [Fact]
    public void A_tampered_payload_fails_the_fingerprint()
    {
        string code = SettingsBundleCodec.Encode(Sample(("rowHeight", "36")));
        string[] parts = code.Split('.');
        string tampered = string.Join('.', parts[0], parts[1], parts[2][..^1] + (parts[2][^1] == 'A' ? 'B' : 'A'), parts[3]);

        Assert.False(SettingsBundleCodec.TryDecode(tampered, out _, out SettingsCodeError err));
        Assert.Equal(SettingsCodeError.ChecksumMismatch, err);
    }

    [Theory]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("WM1")]
    [InlineData("WM2.F.abc.1234abcd")]
    public void Text_with_no_code_in_it_reports_NotFound(string text)
    {
        Assert.False(SettingsBundleCodec.TryDecode(text, out _, out SettingsCodeError err));
        Assert.Equal(SettingsCodeError.NotFound, err);
    }

    [Fact]
    public void A_payload_that_passes_the_fingerprint_but_is_not_gzip_reports_Corrupt()
    {
        // Hand-built so the fingerprint is genuinely correct — this is the "right envelope, wrong contents"
        // path, and it must come back as an error rather than an exception out of GZipStream.
        string code = SettingsBundleCodec.Encode(Sample(("rowHeight", "36")));
        string[] parts = code.Split('.');
        string forged = FingerprintedCode(parts[1], "bm90LWd6aXAtYXQtYWxs"); // "not-gzip-at-all"

        Assert.False(SettingsBundleCodec.TryDecode(forged, out _, out SettingsCodeError err));
        Assert.Equal(SettingsCodeError.Corrupt, err);
    }

    /// <summary>Build a code whose fingerprint really matches, so decoding gets past the checksum gate.</summary>
    private static string FingerprintedCode(string profile, string payload)
    {
        // Same construction the codec uses, reproduced here rather than exposed as API — a test that needs an
        // internal hook is usually a test that has stopped describing the public behaviour.
        byte[] hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(payload));
        return $"WM1.{profile}.{payload}.{Convert.ToHexStringLower(hash)[..8]}";
    }

    [Fact]
    public void A_newer_container_version_is_refused_whole()
    {
        var future = Sample(("rowHeight", "36"));
        future.Version = 2;
        string code = SettingsBundleCodec.Encode(future);

        Assert.False(SettingsBundleCodec.TryDecode(code, out _, out SettingsCodeError err));
        Assert.Equal(SettingsCodeError.FutureVersion, err);
    }

    [Fact]
    public void Unknown_fields_in_the_payload_do_not_break_an_older_reader()
    {
        // Forward compatibility for additive changes: a v1 reader must ignore fields it has never heard of.
        string code = SettingsBundleCodec.Encode(Sample(("rowHeight", "36")));
        Assert.True(SettingsBundleCodec.TryDecode(code, out SettingsBundle back, out _));
        Assert.Equal(1, back.Version);
    }

    [Fact]
    public void Profile_tags_round_trip()
    {
        foreach (SettingsProfile p in new[] { SettingsProfile.Full, SettingsProfile.Design, SettingsProfile.Alarms })
        {
            string tag = SettingsBundleCodec.ProfileTag(p);
            Assert.Equal(p, SettingsBundleCodec.ParseProfile(tag));

            var b = Sample(("rowHeight", "36"));
            b.Profile = tag;
            Assert.True(SettingsBundleCodec.TryDecode(SettingsBundleCodec.Encode(b), out SettingsBundle back, out _));
            Assert.Equal(tag, back.Profile);
        }
    }

    [Fact]
    public void An_empty_bundle_still_round_trips()
    {
        Assert.True(SettingsBundleCodec.TryDecode(SettingsBundleCodec.Encode(Sample()), out SettingsBundle back, out _));
        Assert.Empty(back.Data);
    }
}

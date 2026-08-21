using WaffleMeter.App.Core;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

/// <summary>
/// The pack is addressed by a hash of the spoken line, and the bake script computes that hash separately in
/// Python. Nothing at build time joins the two, so a drift in either direction — a different digest, a
/// different truncation, a stray trim — would silently resolve every lookup to nothing and quietly push all
/// 373 lines back onto the network. These tests pin the contract with values the bake script must reproduce.
/// </summary>
public sealed class BakedVoicePackTests
{
    /// <summary>
    /// Values produced by the Python bake script (<c>sha256(text.strip().encode("utf-8")).hexdigest()[:16]</c>).
    /// If C# and Python ever disagree, this is where it surfaces — not in a user's silent, voiceless alert.
    /// </summary>
    [Theory]
    [InlineData("슈고 페스타, 5분 뒤 시작합니다", "88fd2526b77a0c83")]
    [InlineData("지켈의 축복 온", "f3e594c08c49d1d4")]
    [InlineData("썩은 쿠타르, 10분 뒤 리젠", "665005c4366b2007")]
    public void The_hash_matches_the_bake_script(string text, string expected)
    {
        Assert.Equal(expected, BakedVoicePack.HashOf(text));
        Assert.Equal(expected + ".mp3", BakedVoicePack.FileNameFor(text));
    }

    [Fact]
    public void Hashing_is_stable_and_case_exact()
    {
        Assert.Equal(BakedVoicePack.HashOf("지켈의 축복 온"), BakedVoicePack.HashOf("지켈의 축복 온"));
        Assert.NotEqual(BakedVoicePack.HashOf("지켈의 축복 온"), BakedVoicePack.HashOf("지켈의 축복 오프"));
    }

    /// <summary>Trimming is part of the contract: the bake script hashes the stripped string.</summary>
    [Fact]
    public void Surrounding_whitespace_does_not_change_the_key()
    {
        Assert.Equal(BakedVoicePack.HashOf("파멸의 방패 오프"), BakedVoicePack.HashOf("  파멸의 방패 오프 "));
    }

    /// <summary>A comma is not cosmetic — it is inside the key. Punctuation drift orphans the whole pack.</summary>
    [Fact]
    public void Punctuation_is_part_of_the_key()
    {
        Assert.NotEqual(BakedVoicePack.HashOf("썩은 쿠타르, 10분 뒤 리젠"),
                        BakedVoicePack.HashOf("썩은 쿠타르. 10분 뒤 리젠"));
    }

    [Fact]
    public void An_unknown_pack_name_falls_back_to_the_default_rather_than_throwing()
    {
        Assert.Equal(BakedVoicePack.Wasuni, new BakedVoicePack(Path.GetTempPath(), "없는팩").Pack);
        Assert.Equal(BakedVoicePack.Wabungi, new BakedVoicePack(Path.GetTempPath(), BakedVoicePack.Wabungi).Pack);
    }

    [Fact]
    public void A_missing_pack_directory_reports_a_miss_instead_of_throwing()
    {
        var pack = new BakedVoicePack(Path.Combine(Path.GetTempPath(), "waffle_no_such_" + Guid.NewGuid().ToString("N")),
                                      BakedVoicePack.Wasuni);
        Assert.False(pack.Exists);
        Assert.Null(pack.TryGet("슈고 페스타, 5분 뒤 시작합니다"));
        Assert.Null(pack.TryGet(""));
    }

    [Fact]
    public void A_present_clip_round_trips()
    {
        string root = Path.Combine(Path.GetTempPath(), "waffle_pack_" + Guid.NewGuid().ToString("N"));
        string dir = Path.Combine(root, "voice", BakedVoicePack.Wasuni);
        Directory.CreateDirectory(dir);
        try
        {
            const string Line = "감시자 카이라, 10분 뒤 정각 출현 가능";
            byte[] payload = { 0xFF, 0xFB, 0x90, 0x44 }; // MP3 frame header shape
            File.WriteAllBytes(Path.Combine(dir, BakedVoicePack.FileNameFor(Line)), payload);

            var pack = new BakedVoicePack(root, BakedVoicePack.Wasuni);
            Assert.True(pack.Exists);
            Assert.Equal(payload, pack.TryGet(Line));
            Assert.Null(pack.TryGet("이 문구는 굽지 않았다"));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void Every_shipped_pack_name_is_known()
    {
        Assert.All(BakedVoicePack.All, p => Assert.True(BakedVoicePack.IsKnown(p)));
        Assert.False(BakedVoicePack.IsKnown(null));
        Assert.False(BakedVoicePack.IsKnown("와초딩"));
    }
}

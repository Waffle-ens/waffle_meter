using System.Text;
using WaffleMeter.App.Core;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

/// <summary>Pure protocol spec for <see cref="EdgeTtsProtocol"/> (token rounding, SSML, frame parsing).</summary>
public class EdgeTtsProtocolTests
{
    [Fact]
    public void Gec_token_is_uppercase_hex_and_stable_within_a_5_minute_window()
    {
        // two times inside the same 5-minute window hash identically; a later window differs. The window
        // boundary is at unix % 300 == 0 (the epoch offset is itself a multiple of 300), so anchor t0 there.
        long t0 = 1_700_000_100;      // divisible by 300 → window start
        long sameWindow = t0 + 120;   // +2 min, still in the window
        long nextWindow = t0 + 300;   // exactly the next window boundary

        string a = EdgeTtsProtocol.SecMsGecToken(t0);
        string b = EdgeTtsProtocol.SecMsGecToken(sameWindow);
        string c = EdgeTtsProtocol.SecMsGecToken(nextWindow);

        Assert.Equal(64, a.Length);                       // SHA-256 hex
        Assert.All(a, ch => Assert.Contains(ch, "0123456789ABCDEF"));
        Assert.Equal(a, b);                               // same window → same token
        Assert.NotEqual(a, c);                            // rotates across windows
    }

    [Fact]
    public void Ssml_escapes_the_text_and_carries_the_voice()
    {
        string ssml = EdgeTtsProtocol.BuildSsml("체력 <30% & 위험", "ko-KR-SunHiNeural");
        Assert.Contains("name='ko-KR-SunHiNeural'", ssml);
        Assert.Contains("&lt;30% &amp; 위험", ssml); // < and & escaped
        Assert.DoesNotContain("<30%", ssml);
    }

    [Fact]
    public void Endpoint_uri_includes_the_token_and_version()
    {
        string uri = EdgeTtsProtocol.BuildEndpointUri("abc123", "DEADBEEF");
        Assert.StartsWith("wss://", uri);
        Assert.Contains("ConnectionId=abc123", uri);
        Assert.Contains("Sec-MS-GEC=DEADBEEF", uri);
        Assert.Contains("Sec-MS-GEC-Version=" + EdgeTtsProtocol.GecVersion, uri);
    }

    [Fact]
    public void Extract_audio_payload_strips_the_length_prefixed_header()
    {
        // [u16 BE headerLen=3]["abc"][audio bytes 01 02 03]
        byte[] frame = { 0x00, 0x03, (byte)'a', (byte)'b', (byte)'c', 0x01, 0x02, 0x03 };
        byte[] audio = EdgeTtsProtocol.ExtractAudioPayload(frame).ToArray();
        Assert.Equal(new byte[] { 0x01, 0x02, 0x03 }, audio);
    }

    [Fact]
    public void Is_mp3_recognizes_id3_and_frame_sync()
    {
        Assert.True(EdgeTtsProtocol.IsMp3(Encoding.ASCII.GetBytes("ID3xxxx")));
        Assert.True(EdgeTtsProtocol.IsMp3(new byte[] { 0xFF, 0xFB, 0x00 }));
        Assert.False(EdgeTtsProtocol.IsMp3(new byte[] { 0x00, 0x01 }));
    }
}

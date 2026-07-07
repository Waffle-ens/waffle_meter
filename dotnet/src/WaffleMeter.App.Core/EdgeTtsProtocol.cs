using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace WaffleMeter.App.Core;

/// <summary>
/// Pure protocol helpers for the online neural read-aloud text-to-speech endpoint (the same one the Edge
/// browser's read-aloud uses). Kept free of any I/O so the token/SSML/frame logic is unit-testable; the
/// WebSocket transport + audio playback live in the WPF layer. All values here are well-known public
/// constants of that endpoint.
/// </summary>
public static class EdgeTtsProtocol
{
    /// <summary>Well-known public client token the read-aloud endpoint expects.</summary>
    public const string TrustedClientToken = "6A5AA1D4EAFF4E9FB37E23D68491D6F4";

    /// <summary>Version tag the endpoint pairs with the GEC token.</summary>
    public const string GecVersion = "1-143.0.3650.75";

    /// <summary>Default Korean female neural voice.</summary>
    public const string DefaultVoice = "ko-KR-SunHiNeural";

    // Offset between the Unix epoch (1970) and the Windows FILETIME epoch (1601), in seconds.
    private const long WindowsEpochOffsetSeconds = 11_644_473_600L;

    /// <summary>
    /// The rolling Sec-MS-GEC token: SHA-256 (upper-hex) of the current time — rounded DOWN to a 5-minute
    /// window, expressed in Windows FILETIME ticks — concatenated with the trusted client token. Rounding to
    /// a 5-minute window is what lets the token be regenerated locally without a handshake; it rotates every
    /// 5 minutes. Pure in <paramref name="unixSeconds"/> so it is testable.
    /// </summary>
    public static string SecMsGecToken(long unixSeconds)
    {
        long winSeconds = unixSeconds + WindowsEpochOffsetSeconds;
        long rounded = winSeconds - (winSeconds % 300); // floor to 5-minute boundary
        long ticks = rounded * 10_000_000L;             // seconds -> 100ns FILETIME ticks
        string material = ticks.ToString(CultureInfo.InvariantCulture) + TrustedClientToken;
        byte[] hash = SHA256.HashData(Encoding.ASCII.GetBytes(material));
        return Convert.ToHexString(hash); // already upper-case
    }

    /// <summary>The wss endpoint URI for a synthesis connection.</summary>
    public static string BuildEndpointUri(string connectionId, string gecToken) =>
        "wss://speech.platform.bing.com/consumer/speech/synthesize/readaloud/edge/v1"
        + "?TrustedClientToken=" + TrustedClientToken
        + "&ConnectionId=" + connectionId
        + "&Sec-MS-GEC=" + gecToken
        + "&Sec-MS-GEC-Version=" + GecVersion;

    /// <summary>The <c>speech.config</c> text frame that must be sent before the SSML (MP3 output).</summary>
    public static string BuildSpeechConfigMessage(string timestamp)
    {
        const string config = "{\"context\":{\"synthesis\":{\"audio\":{\"metadataoptions\":{"
            + "\"sentenceBoundaryEnabled\":false,\"wordBoundaryEnabled\":false},"
            + "\"outputFormat\":\"audio-24khz-48kbitrate-mono-mp3\"}}}}";
        return $"X-Timestamp:{timestamp}\r\nContent-Type:application/json; charset=utf-8\r\nPath:speech.config\r\n\r\n{config}";
    }

    /// <summary>The SSML text frame (with headers) that requests the synthesis.</summary>
    public static string BuildSsmlMessage(string requestId, string timestamp, string ssml) =>
        $"X-RequestId:{requestId}\r\nX-Timestamp:{timestamp}\r\nContent-Type:application/ssml+xml\r\nPath:ssml\r\n\r\n{ssml}";

    /// <summary>The SSML body. <paramref name="rate"/>/<paramref name="pitch"/> are small integers (-5..5),
    /// mapped to percentage prosody like the reference client.</summary>
    public static string BuildSsml(string text, string voice, int rate = 0, int pitch = 0)
    {
        int r = Math.Clamp(rate, -5, 5) * 8;   // ±40%
        int p = Math.Clamp(pitch, -5, 5) * 6;  // ±30%
        return $"<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='ko-KR'>"
            + $"<voice name='{voice}'>"
            + $"<prosody pitch='{Signed(p)}%' rate='{Signed(r)}%' volume='+0%'>{Escape(text)}</prosody>"
            + "</voice></speak>";
    }

    /// <summary>True if <paramref name="frame"/> begins with an MP3 signature (ID3 tag or a frame sync),
    /// so a non-audio binary control frame isn't written to the audio stream.</summary>
    public static bool IsMp3(ReadOnlySpan<byte> frame)
    {
        if (frame.Length >= 3 && frame[0] == 0x49 && frame[1] == 0x44 && frame[2] == 0x33)
        {
            return true; // "ID3"
        }

        return frame.Length >= 2 && frame[0] == 0xFF && (frame[1] & 0xE0) == 0xE0; // frame sync
    }

    /// <summary>Strip the <c>[u16 BE headerLen][header][audio]</c> wrapper of a binary audio message and
    /// return the audio bytes, or empty if the frame is too short / not the audio path.</summary>
    public static ReadOnlySpan<byte> ExtractAudioPayload(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 2)
        {
            return ReadOnlySpan<byte>.Empty;
        }

        int headerLen = (frame[0] << 8) | frame[1];
        int start = 2 + headerLen;
        return start <= frame.Length ? frame[start..] : ReadOnlySpan<byte>.Empty;
    }

    private static string Signed(int v) => v >= 0 ? "+" + v.ToString(CultureInfo.InvariantCulture) : v.ToString(CultureInfo.InvariantCulture);

    private static string Escape(string s) => s
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
        .Replace("\"", "&quot;").Replace("'", "&apos;");
}

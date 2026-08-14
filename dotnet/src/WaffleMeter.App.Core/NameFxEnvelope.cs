using System.Security.Cryptography;
using System.Text;

namespace WaffleMeter.App.Core;

/// <summary>
/// The wrapper the grant artifact arrives in.
/// <para>⚠ <b>This is friction, not privacy.</b> The key has to be present in every meter, so it is not a
/// secret. What it stops is the one-line <c>curl | gunzip</c> that lifts the whole list; anyone who reads this
/// source or unpacks the binary can still recover it. The plan notes an occasion where a competitor's local
/// cache was actually decrypted the same way — same structure, same outcome.</para>
/// <para>So nothing here justifies calling the list private. The exposure judgement stays exactly as recorded
/// on <c>supporters.consented_at</c> server-side.</para>
/// <para>Layout: <c>WMFX1</c> | nonce(12) | ciphertext | tag(16), AES-256-GCM. The manifest's
/// <c>sha256</c> covers the WHOLE envelope — i.e. the bytes on the wire — so the integrity contract is
/// unchanged and the reason the server refuses to declare a Content-Encoding still holds.</para>
/// </summary>
public static class NameFxEnvelope
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("WMFX1");
    private const int NonceBytes = 12;
    private const int TagBytes = 16;

    /// <summary>Must match <c>src/server/namefx/envelope.ts</c> byte for byte. Change one side only and every
    /// install fails to decrypt at the same moment — visible as "연출이 안 붙는다" and nothing else.</summary>
    private static readonly byte[] Key = Convert.FromHexString(
        "ea2c5653ddcbe8bec46619089d3924fb1fd7068889c975619fa7c69cda7b69ee");

    /// <summary>True when the payload carries the envelope magic. Used to keep reading plain gzip artifacts
    /// that predate this — the cached file on disk may well be one of them.</summary>
    public static bool IsSealed(byte[] payload) =>
        payload.Length >= Magic.Length && payload.AsSpan(0, Magic.Length).SequenceEqual(Magic);

    /// <summary>Unwrap, or throw. Callers treat any failure as "no roster", the same as a bad download.</summary>
    public static byte[] Open(byte[] sealedPayload)
    {
        if (sealedPayload.Length < Magic.Length + NonceBytes + TagBytes || !IsSealed(sealedPayload))
        {
            throw new InvalidDataException("namefx_envelope_malformed");
        }

        int bodyStart = Magic.Length + NonceBytes;
        int bodyLength = sealedPayload.Length - bodyStart - TagBytes;

        ReadOnlySpan<byte> nonce = sealedPayload.AsSpan(Magic.Length, NonceBytes);
        ReadOnlySpan<byte> body = sealedPayload.AsSpan(bodyStart, bodyLength);
        ReadOnlySpan<byte> tag = sealedPayload.AsSpan(sealedPayload.Length - TagBytes, TagBytes);

        byte[] plaintext = new byte[bodyLength];
        using var gcm = new AesGcm(Key, TagBytes);
        gcm.Decrypt(nonce, body, tag, plaintext);
        return plaintext;
    }
}

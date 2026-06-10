using System.Security.Cryptography;
using System.Text;

namespace WaffleMeter.Stats;

/// <summary>
/// Verbatim port of Kotlin <c>stats.StatsIdentity</c>: a stable, anonymous per-character identity
/// hash = SHA-256 of "<c>version|server|lowercased-trimmed-nickname</c>", lowercase hex. Used to
/// de-duplicate uploads and tie consent to a character without sending the raw nickname.
/// </summary>
public static class StatsIdentity
{
    public const string IdentityHashVersion = "sha256:aion2-character:v1";

    public static string? CharacterIdentityHash(int server, string? nickname)
    {
        if (server <= 0)
        {
            return null;
        }

        string normalizedName = nickname?.Trim().ToLowerInvariant() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return null;
        }

        return Sha256($"{IdentityHashVersion}|{server}|{normalizedName}");
    }

    public static string Sha256(string raw)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexStringLower(digest);
    }
}

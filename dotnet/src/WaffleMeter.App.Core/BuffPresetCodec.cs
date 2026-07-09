using System.Text;
using System.Text.Json;

namespace WaffleMeter.App.Core;

/// <summary>
/// Encodes the buff preset slots as one Base64(UTF-8(JSON)) settings value, exactly as
/// <see cref="CustomAlarmCodec"/> does — and for the same reason: slot names are user-typed Korean, and
/// <c>PropertyHandler.GetProperty</c> re-decodes every value through Latin-1 → EUC-KR, which replaces each
/// non-Latin-1 char with '?'. Base64 output is pure ASCII, so it survives that read untouched.
/// </summary>
public static class BuffPresetCodec
{
    public static string Encode(BuffPresetSet set)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(set)));

    /// <summary>Decode a stored blob, or null when it is absent, corrupt, or structurally unusable — the
    /// caller re-seeds rather than throwing out of the settings-load path.</summary>
    public static BuffPresetSet? Decode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            byte[] bytes = Convert.FromBase64String(raw);
            return JsonSerializer.Deserialize<BuffPresetSet>(Encoding.UTF8.GetString(bytes));
        }
        catch
        {
            return null; // corrupt / hand-edited value — re-seed rather than throw
        }
    }
}

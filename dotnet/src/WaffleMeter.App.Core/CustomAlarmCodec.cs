using System.Text;
using System.Text.Json;

namespace WaffleMeter.App.Core;

/// <summary>
/// (De)serializes the custom-alarm list to a single settings string. Encodes as Base64(UTF-8(JSON)) so the
/// stored value is pure ASCII — sidestepping the settings store's Java-.properties escaping and EUC-KR
/// re-decode (see PropertyHandler), which would otherwise corrupt Korean titles or the JSON's own
/// backslash / <c>\\uXXXX</c> escapes.
/// </summary>
public static class CustomAlarmCodec
{
    public static string Encode(IReadOnlyList<CustomAlarm> alarms)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(alarms)));

    public static IReadOnlyList<CustomAlarm> Decode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        try
        {
            byte[] bytes = Convert.FromBase64String(raw);
            return JsonSerializer.Deserialize<List<CustomAlarm>>(Encoding.UTF8.GetString(bytes)) ?? [];
        }
        catch
        {
            return []; // corrupt / legacy value — start empty rather than throw
        }
    }
}

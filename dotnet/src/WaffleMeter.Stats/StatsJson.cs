using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WaffleMeter.Stats;

/// <summary>
/// Shared serializer settings matching the Kotlin client's kotlinx.serialization Json
/// (<c>encodeDefaults = true, ignoreUnknownKeys = true, explicitNulls = false</c>): camelCase keys,
/// null fields omitted (but non-null defaults like <c>false</c>/<c>0</c> are written), and unknown
/// keys ignored on read. Field-name overrides (e.g. <c>public</c>) use <see cref="JsonPropertyNameAttribute"/>.
/// </summary>
public static class StatsJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // kotlinx writes raw UTF-8 (Korean nicknames, '+', '<' unescaped); match it instead of
        // System.Text.Json's default \uXXXX escaping so the wire bytes line up with the Kotlin client.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options)!;
}

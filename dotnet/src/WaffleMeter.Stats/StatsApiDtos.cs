using System.Text.Json.Serialization;

namespace WaffleMeter.Stats;

// Verbatim port of the request/response DTOs from Kotlin stats.StatsApiClient. The server's "public"
// key is mapped via JsonPropertyName (Kotlin @SerialName("public")).

public sealed record ConsentStatusResponse(
    bool Ok,
    string IdentityHash,
    bool Exists,
    string ConsentState,
    [property: JsonPropertyName("public")] bool PublicCharacter = false,
    string? ConsentVersion = null,
    string? UpdatedAt = null,
    string? LastSeenAt = null,
    string? CharacterId = null);

public sealed record ConsentEventRequest(
    string ConsentState,
    string ConsentVersion,
    string? IdentityHash = null,
    ConsentEventCharacter? Character = null);

public sealed record ConsentEventCharacter(
    string IdentityHash,
    string Nickname,
    int Server,
    [property: JsonPropertyName("public")] bool PublicCharacter,
    string? Job = null,
    int Power = 0);

public sealed record ReportUploadResponse(
    bool Ok,
    string? ReportId = null,
    bool Duplicate = false);

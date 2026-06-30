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
    string? CharacterId = null,
    // SHARED CONTRACT §2.2/§3.3: on a SIGNED consent event the server echoes whether the signing install
    // holds this character's grant. Defaulted false so a pre-rollout server that omits it is harmless
    // (forward-compatible). Unsigned reads can't carry a real grant, so this stays false there.
    [property: JsonPropertyName("granted")] bool Granted = false);

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
    bool Duplicate = false,
    // SHARED CONTRACT §2.2/§3.3: true once this signed upload's uploader character earned/holds a grant for
    // the signing install. Defaulted false → forward-compatible with a pre-rollout server.
    [property: JsonPropertyName("granted")] bool Granted = false);

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
    // Nullable: null OMITS the "public" key (StatsJson ignores nulls) so the server PRESERVES the
    // character's existing public flag. A non-owning install sends null to avoid ever downgrading a
    // character another install legitimately made public; only an owning (granted) install asserts
    // true/false. See StatsConsentManager.Accept.
    [property: JsonPropertyName("public")] bool? PublicCharacter,
    string? Job = null,
    int Power = 0);

public sealed record ReportUploadResponse(
    bool Ok,
    string? ReportId = null,
    bool Duplicate = false,
    // SHARED CONTRACT §2.2/§3.3: true once this signed upload's uploader character earned/holds a grant for
    // the signing install. Defaulted false → forward-compatible with a pre-rollout server.
    [property: JsonPropertyName("granted")] bool Granted = false,
    // 던전 티어: the uploader character's own career tier for this dungeon, piggybacked so the meter never
    // spends a request on it. The KEY IS ABSENT (not null) when the server has nothing — an unmapped
    // encounter, no snapshot, or a stale envelope — so this stays null on every older server too.
    TierSnapshotDto? Tier = null);

/// <summary>A character's career tier for one scope, as the server computed it.
/// <para><paramref name="Scope"/> is <c>overall</c> or <c>d:{dungeonOrd}:{variantOrd}:{partyMode}</c> — a
/// DIFFERENT coordinate system from the artifact's six-axis cohort key, so the two must not be mixed.</para>
/// <para>Hysteresis (promote fast, demote slow) is already applied server-side; the meter renders
/// <paramref name="TierRank"/> as given.</para></summary>
public sealed record TierSnapshotDto(
    int TierRank,
    string TierName,
    double TopPercent,
    string Scope,
    int Battles,
    int CohortSize,
    string? ComputedAt = null);

/// <summary>Manifest for the current distribution artifact. Polled rarely; the only field that decides whether
/// a download is needed is <paramref name="ArtifactId"/>.</summary>
public sealed record TierManifestResponse(
    bool Ok,
    string ArtifactId,
    int SchemaVersion,
    int WindowDays,
    string Url,
    long ByteSize,
    string Sha256,
    string? GeneratedAt = null,
    string? ExpiresAt = null);

/// <summary>Manifest for the current supporter/ranker grant artifact. Same content-addressed shape as the tier
/// manifest — <paramref name="ArtifactId"/> alone decides whether a download is needed — but a SEPARATE document
/// on a separate cadence. Sharing the tier one would tie a grant taking effect to the distribution rebuild.</summary>
public sealed record NameFxManifestResponse(
    bool Ok,
    string ArtifactId,
    int SchemaVersion,
    string Url,
    long ByteSize,
    string Sha256,
    string? GeneratedAt = null);

/// <summary>"이 캐릭터의 연출을 이걸로 바꿔 줘". 서명 필수 — 서버가 <c>character_grants</c> 로 이 설치본이
/// 그 캐릭터로 전투를 올린 적이 있는지 확인한다.
/// <para>⚠ <paramref name="IdentityHash"/> 는 반드시 <b>본문</b>에 있다. 정규 서명 문자열의 PATH 는
/// 쿼리스트링을 포함하지 않으므로, 쿼리로 보내면 서명이 덮지 않는 값이 되어 서명을 재사용해 대상
/// 캐릭터만 바꿔치기할 수 있다.</para>
/// <para><paramref name="GaugeId"/> 를 보내지 않으면 서버가 기존 값을 그대로 둔다.</para></summary>
public sealed record NameFxChoiceRequest(
    string IdentityHash,
    string EffectId,
    string? GaugeId = null);

public sealed record NameFxChoiceResponse(
    bool Ok,
    string? Kind = null,
    string? EffectId = null,
    string? GaugeId = null,
    bool Published = false);

/// <summary>Batch tier lookup for party applicants. Hard server caps: 12 hashes, 4,096-byte body, 120/hour.</summary>
public sealed record TierLookupRequest(
    IReadOnlyList<string> IdentityHashes,
    string? Scope = null);

public sealed record TierLookupResponse(
    bool Ok,
    string? GeneratedAt = null,
    IReadOnlyList<TierLookupResult>? Results = null);

/// <summary><paramref name="Status"/> is exactly one of <c>ok</c> | <c>insufficient</c> | <c>none</c>.
/// <para><c>none</c> deliberately collapses "did not consent", "revoked" and "never existed" into one byte-identical
/// answer, so the endpoint cannot be used to probe whether a character exists. Everything except
/// <paramref name="IdentityHash"/>/<paramref name="Status"/> is absent for the non-ok cases, and no nickname,
/// server or power is ever returned.</para></summary>
public sealed record TierLookupResult(
    string IdentityHash,
    string Status,
    int TierRank = 0,
    string? TierName = null,
    double TopPercent = 0,
    string? Job = null,
    int Battles = 0,
    int CohortSize = 0,
    string? Scope = null);

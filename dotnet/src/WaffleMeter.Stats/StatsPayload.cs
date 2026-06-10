using System.Text.Json.Serialization;

namespace WaffleMeter.Stats;

// Verbatim port of Kotlin stats.StatsPayload DTOs. Serialized with StatsJson (camelCase, nulls
// omitted). The literal "public" key keeps its name via JsonPropertyName.

public sealed record StatsOwnCharacter(
    bool Detected,
    int Id = 0,
    string? Nickname = null,
    int Server = -1,
    string? Job = null,
    int Power = 0);

public sealed record StatsUploadPayload(
    int SchemaVersion,
    string ClientVersion,
    string BattleHash,
    string IdentityHashVersion,
    string ConsentVersion,
    long UploadedAt,
    StatsCharacterPayload Character,
    StatsEncounterPayload Encounter,
    StatsBattlePayload Battle,
    StatsPartyCompositionPayload PartyComposition,
    IReadOnlyList<StatsParticipantPayload> Participants,
    StatsResultPayload Result,
    IReadOnlyList<StatsSkillPayload> Skills,
    IReadOnlyList<StatsBuffPayload> Buffs,
    IReadOnlyList<StatsBuffPayload> BossDebuffs);

public sealed record StatsCharacterPayload(
    string IdentityHash,
    string Nickname,
    int Server,
    string? Job,
    int Power,
    [property: JsonPropertyName("public")] bool Public);

public sealed record StatsEncounterPayload(
    int MobCode,
    string BossName,
    string? DungeonName = null,
    string? Category = null,
    string? Difficulty = null,
    int? Stage = null,
    int? BossIndex = null);

public sealed record StatsBattlePayload(
    long StartedAt,
    long EndedAt,
    long DurationMs,
    int PartySize);

public sealed record StatsPartyCompositionPayload(
    IReadOnlyDictionary<string, int> Jobs,
    StatsSynergyPayload Synergy);

public sealed record StatsSynergyPayload(
    bool HasGuardian,
    bool HasGladiator,
    bool HasChanter,
    bool HasCleric,
    int SynergyCount);

public sealed record StatsParticipantPayload(
    string? IdentityHash,
    bool IsUploader,
    string? Job,
    int Power,
    StatsResultPayload Result,
    IReadOnlyList<StatsSkillPayload> Skills,
    IReadOnlyList<StatsBuffPayload> Buffs);

public sealed record StatsResultPayload(
    long TotalDamage,
    long Dps,
    double PartyContribution,
    double BossHpContribution,
    int HitCount,
    double CritRate,
    double StrongRate,
    double PerfectRate,
    double BackRate,
    double ParryRate,
    double BossBlockRate);

public sealed record StatsSkillPayload(
    int SkillCode,
    string SkillName,
    string DamageType,
    long Damage,
    int HitCount,
    double CritRate,
    double StrongRate,
    double PerfectRate,
    double Share);

public sealed record StatsBuffPayload(
    int BuffCode,
    string BuffName,
    double OperatingRate,
    string Scope,
    string Category,
    string? Source = null,
    string? ActorIdentityHash = null,
    int? OwnerParticipantIndex = null,
    int? ActorParticipantIndex = null);

public sealed record StatsUploadStatus(
    bool Enabled,
    int Pending,
    int Uploaded,
    int Skipped,
    int Failed,
    string? LastPath = null,
    string? LastReason = null,
    long LastUpdatedAt = 0L);

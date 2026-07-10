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
    IReadOnlyList<StatsBuffPayload> Buffs,
    // 10-인 공대(5+5) sub-party: PartyNumber 1 = uploader's party (slots 1-5), 2 = the other party (slots 6-10);
    // PartySlot is the raw 1-10 roster slot. Both null for a non-raid (5-인 이하) or an unmatched participant.
    int? PartyNumber = null,
    int? PartySlot = null);

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

/// <param name="Category">
/// Target-derived: "buff" for a player target, "debuff" for the boss. That IS the correct taxonomy — a player
/// skill's debuff always lands on its target, never on the caster — so there is nothing better to send. The
/// datamined per-code type is not shipped because it is wrong often enough to be dangerous.
/// </param>
/// <param name="BaseCode">The 8-digit base skill code this row's rank/aspect variants collapsed to.</param>
public sealed record StatsBuffPayload(
    int BuffCode,
    string BuffName,
    double OperatingRate,
    string Scope,
    string Category,
    string? Source = null,
    string? ActorIdentityHash = null,
    int? OwnerParticipantIndex = null,
    int? ActorParticipantIndex = null,
    int? BaseCode = null);

public sealed record StatsUploadStatus(
    bool Enabled,
    int Pending,
    int Uploaded,
    int Skipped,
    int Failed,
    string? LastPath = null,
    string? LastReason = null,
    long LastUpdatedAt = 0L);

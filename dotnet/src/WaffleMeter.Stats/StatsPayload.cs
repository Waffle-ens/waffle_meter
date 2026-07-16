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
    IReadOnlyList<StatsBuffPayload> BossDebuffs,
    // The uploader's (self) combat-detail DPS graph sources. Null (omitted) when the frozen snapshot is absent
    // (e.g. a pre-save/live report) — the web then hides the chart. Uploader-only, not per participant.
    StatsDpsSeriesPayload? DpsSeries = null,
    IReadOnlyList<StatsSelfBuffIntervalPayload>? SelfBuffIntervals = null);

/// <summary>The uploader's per-second damage series. <see cref="Damage"/>[i] = damage dealt in the i-th second
/// from battle start; <see cref="Step"/> = seconds per sample (1). The web aggregates/smooths it for display.</summary>
public sealed record StatsDpsSeriesPayload(int Step, IReadOnlyList<long> Damage);

/// <summary>One of the uploader's own class(딜) buffs and when it was up. <see cref="Spans"/> is a flat
/// <c>[start, end, start, end, ...]</c> list of whole-second offsets from battle start (merged/clamped upstream).
/// Only the uploader's own-class buffs are sent — consumables (scrolls/food/drinks) and other players' buffs are
/// excluded (mirrors the meter's 내 버프 filter / <c>BuffSource == "self"</c>).</summary>
public sealed record StatsSelfBuffIntervalPayload(int BaseCode, string Name, IReadOnlyList<int> Spans);

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
    // Front/back are the two mutually-exclusive facing judgments; both divide by the flag-bearing hit count
    // (FlaggedTimes), matching the meter's 후방/전방 detail tiles. Optional on the wire (older schema versions
    // omitted it); the web treats an absent frontRate as "no data" (renders "-", never 0%).
    double FrontRate,
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

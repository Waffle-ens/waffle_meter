package com.tbread.stats

import kotlinx.serialization.Serializable

@Serializable
data class StatsOwnCharacter(
    val detected: Boolean,
    val id: Int = 0,
    val nickname: String? = null,
    val server: Int = -1,
    val job: String? = null,
    val power: Int = 0
)

@Serializable
data class StatsUploadPayload(
    val schemaVersion: Int,
    val clientVersion: String,
    val battleHash: String,
    val identityHashVersion: String,
    val consentVersion: String,
    val uploadedAt: Long,
    val character: StatsCharacterPayload,
    val encounter: StatsEncounterPayload,
    val battle: StatsBattlePayload,
    val partyComposition: StatsPartyCompositionPayload,
    val participants: List<StatsParticipantPayload>,
    val result: StatsResultPayload,
    val skills: List<StatsSkillPayload>,
    val buffs: List<StatsBuffPayload>,
    val bossDebuffs: List<StatsBuffPayload>
)

@Serializable
data class StatsCharacterPayload(
    val identityHash: String,
    val nickname: String,
    val server: Int,
    val job: String?,
    val power: Int,
    val public: Boolean
)

@Serializable
data class StatsEncounterPayload(
    val mobCode: Int,
    val bossName: String,
    val dungeonName: String? = null,
    val category: String? = null,
    val difficulty: String? = null,
    val stage: Int? = null,
    val bossIndex: Int? = null
)

@Serializable
data class StatsBattlePayload(
    val startedAt: Long,
    val endedAt: Long,
    val durationMs: Long,
    val partySize: Int
)

@Serializable
data class StatsPartyCompositionPayload(
    val jobs: Map<String, Int>,
    val synergy: StatsSynergyPayload
)

@Serializable
data class StatsSynergyPayload(
    val hasGuardian: Boolean,
    val hasGladiator: Boolean,
    val hasChanter: Boolean,
    val hasCleric: Boolean,
    val synergyCount: Int
)

@Serializable
data class StatsParticipantPayload(
    val identityHash: String?,
    val isUploader: Boolean,
    val job: String?,
    val power: Int,
    val result: StatsResultPayload,
    val skills: List<StatsSkillPayload>,
    val buffs: List<StatsBuffPayload>
)

@Serializable
data class StatsResultPayload(
    val totalDamage: Long,
    val dps: Long,
    val partyContribution: Double,
    val bossHpContribution: Double,
    val hitCount: Int,
    val critRate: Double,
    val strongRate: Double,
    val perfectRate: Double,
    val backRate: Double,
    val parryRate: Double,
    val bossBlockRate: Double
)

@Serializable
data class StatsSkillPayload(
    val skillCode: Int,
    val skillName: String,
    val damage: Long,
    val hitCount: Int,
    val critRate: Double,
    val strongRate: Double,
    val perfectRate: Double,
    val share: Double
)

@Serializable
data class StatsBuffPayload(
    val buffCode: Int,
    val buffName: String,
    val operatingRate: Double
)

@Serializable
data class StatsUploadStatus(
    val enabled: Boolean,
    val pending: Int,
    val uploaded: Int,
    val skipped: Int,
    val failed: Int,
    val lastPath: String? = null,
    val lastReason: String? = null,
    val lastUpdatedAt: Long = 0L
)

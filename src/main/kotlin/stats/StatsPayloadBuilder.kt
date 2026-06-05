package com.tbread.stats

import com.tbread.data.DataManager
import com.tbread.entity.AnalyzedSkill
import com.tbread.entity.DpsLog
import com.tbread.entity.OperatingData
import com.tbread.entity.User
import com.tbread.entity.enums.JobClass
import java.security.MessageDigest
import kotlin.math.roundToLong

object StatsPayloadBuilder {
    private const val IDENTITY_HASH_VERSION = "sha256:aion2-character:v1"

    private val synergyJobs = setOf(
        JobClass.TEMPLAR,
        JobClass.GLADIATOR,
        JobClass.CHANTER,
        JobClass.CLERIC
    )

    fun ownCharacter(): StatsOwnCharacter {
        val id = DataManager.executorId()
        val user = DataManager.user(id) ?: return StatsOwnCharacter(false)
        return StatsOwnCharacter(
            detected = !user.nickname.isNullOrBlank(),
            id = user.id,
            nickname = user.nickname,
            server = user.server,
            job = user.job?.className,
            power = user.power
        )
    }

    fun build(log: DpsLog, clientVersion: String, killConfirmed: Boolean): BuildResult {
        val report = log.report
        val target = report.target ?: return BuildResult.Skip("target_missing")
        val mob = target.mob
        if (!mob.boss || mob.isDummy) return BuildResult.Skip("not_uploadable_boss")
        if (!killConfirmed) return BuildResult.Skip("not_kill")

        val ownId = DataManager.executorId()
        if (ownId == 0) return BuildResult.Skip("executor_missing")
        val own = report.contributors.firstOrNull { it.id == ownId }
            ?: DataManager.user(ownId)
            ?: return BuildResult.Skip("own_character_missing")
        val ownNickname = own.nickname?.takeIf { it.isNotBlank() }
            ?: return BuildResult.Skip("own_nickname_missing")
        val ownInfo = report.information[own.id] ?: return BuildResult.Skip("own_result_missing")
        if (ownInfo.amount <= 0.0) return BuildResult.Skip("own_damage_empty")

        val duration = (report.battleEnd - report.battleStart).takeIf { it > 0 }
            ?: return BuildResult.Skip("invalid_duration")
        val totalDamage = ownInfo.amount.roundToLong()
        val ownSkills = log.skillDetails[own.id].orEmpty()
        val skillPayloads = buildSkillPayloads(ownSkills, totalDamage)
        val resultRates = summarizeRates(ownSkills.values)
        val participantPayloads = buildParticipantPayloads(log, own.id)
        val ownIdentityHash = characterIdentityHash(own.server, ownNickname)
            ?: return BuildResult.Skip("own_identity_missing")
        val jobCounts = report.contributors
            .mapNotNull { it.job?.className }
            .groupingBy { it }
            .eachCount()
        val synergy = buildSynergy(report.contributors)
        val battleHash = battleHash(
            own.server,
            ownNickname,
            mob.code,
            report.battleStart,
            report.battleEnd,
            totalDamage,
            duration
        )

        return BuildResult.Payload(
            StatsUploadPayload(
                schemaVersion = 3,
                clientVersion = clientVersion,
                battleHash = battleHash,
                identityHashVersion = IDENTITY_HASH_VERSION,
                consentVersion = StatsConsentManager.CONSENT_VERSION,
                uploadedAt = System.currentTimeMillis(),
                character = StatsCharacterPayload(
                    identityHash = ownIdentityHash,
                    nickname = ownNickname,
                    server = own.server,
                    job = own.job?.className,
                    power = own.power,
                    public = StatsConsentManager.info().publicCharacter
                ),
                encounter = StatsEncounterPayload(
                    mobCode = mob.code,
                    bossName = mob.name
                ),
                battle = StatsBattlePayload(
                    startedAt = report.battleStart,
                    endedAt = report.battleEnd,
                    durationMs = duration,
                    partySize = report.contributors.size
                ),
                partyComposition = StatsPartyCompositionPayload(
                    jobs = jobCounts,
                    synergy = synergy
                ),
                participants = participantPayloads,
                result = buildResultPayload(ownInfo, resultRates),
                skills = skillPayloads,
                buffs = log.buffRates[own.id].orEmpty().map(::toBuffPayload),
                bossDebuffs = log.bossBuffRates.map(::toBuffPayload)
            )
        )
    }

    private fun buildParticipantPayloads(log: DpsLog, ownId: Int): List<StatsParticipantPayload> {
        return log.report.contributors.mapNotNull { user ->
            val info = log.report.information[user.id] ?: return@mapNotNull null
            if (info.amount <= 0.0) return@mapNotNull null

            val totalDamage = info.amount.roundToLong()
            val skills = log.skillDetails[user.id].orEmpty()
            val rates = summarizeRates(skills.values)
            StatsParticipantPayload(
                identityHash = user.nickname
                    ?.takeIf { it.isNotBlank() }
                    ?.let { characterIdentityHash(user.server, it) },
                isUploader = user.id == ownId,
                job = user.job?.className,
                power = user.power,
                result = buildResultPayload(info, rates),
                skills = buildSkillPayloads(skills, totalDamage),
                buffs = log.buffRates[user.id].orEmpty().map(::toBuffPayload)
            )
        }.sortedByDescending { it.result.totalDamage }
    }

    private fun buildSynergy(contributors: Collection<User>): StatsSynergyPayload {
        val jobs = contributors.mapNotNull { it.job }.toSet()
        val count = jobs.count { it in synergyJobs }.coerceAtMost(3)
        return StatsSynergyPayload(
            hasGuardian = JobClass.TEMPLAR in jobs,
            hasGladiator = JobClass.GLADIATOR in jobs,
            hasChanter = JobClass.CHANTER in jobs,
            hasCleric = JobClass.CLERIC in jobs,
            synergyCount = count
        )
    }

    private fun buildSkillPayloads(
        skills: Map<String, AnalyzedSkill>,
        totalDamage: Long
    ): List<StatsSkillPayload> {
        return skills.mapNotNull { (key, skill) ->
            val code = key.toIntOrNull() ?: return@mapNotNull null
            val damage = (skill.damageAmount.toLong() + skill.dotDamageAmount.toLong()).coerceAtLeast(0)
            if (damage <= 0L) return@mapNotNull null
            val rate = summarizeRates(listOf(skill))
            StatsSkillPayload(
                skillCode = code,
                skillName = skill.name?.takeIf { it.isNotBlank() } ?: code.toString(),
                damage = damage,
                hitCount = rate.hitCount,
                critRate = rate.critRate,
                strongRate = rate.strongRate,
                perfectRate = rate.perfectRate,
                share = if (totalDamage > 0) oneDecimal(damage.toDouble() / totalDamage * 100.0) else 0.0
            )
        }.sortedByDescending { it.damage }
    }

    private fun buildResultPayload(info: com.tbread.entity.DpsInformation, rates: RateSummary): StatsResultPayload {
        return StatsResultPayload(
            totalDamage = info.amount.roundToLong(),
            dps = info.dps.roundToLong(),
            partyContribution = oneDecimal(info.contribution),
            bossHpContribution = oneDecimal(info.entireContribution),
            hitCount = rates.hitCount,
            critRate = rates.critRate,
            strongRate = rates.strongRate,
            perfectRate = rates.perfectRate,
            backRate = rates.backRate,
            parryRate = rates.parryRate,
            bossBlockRate = 0.0
        )
    }

    private fun toBuffPayload(value: OperatingData): StatsBuffPayload {
        return StatsBuffPayload(
            buffCode = value.code,
            buffName = value.name,
            operatingRate = oneDecimal(value.operatingRate)
        )
    }

    private data class RateSummary(
        val hitCount: Int,
        val critRate: Double,
        val strongRate: Double,
        val perfectRate: Double,
        val backRate: Double,
        val parryRate: Double
    )

    private fun summarizeRates(skills: Collection<AnalyzedSkill>): RateSummary {
        val directHits = skills.sumOf { it.times }.coerceAtLeast(0)
        val allHits = directHits + skills.sumOf { it.dotTimes }.coerceAtLeast(0)
        fun rate(count: Int): Double =
            if (directHits > 0) oneDecimal(count.toDouble() / directHits * 100.0) else 0.0

        return RateSummary(
            hitCount = allHits,
            critRate = rate(skills.sumOf { it.critTimes }),
            strongRate = rate(skills.sumOf { it.doubleTimes }),
            perfectRate = rate(skills.sumOf { it.perfectTimes }),
            backRate = rate(skills.sumOf { it.backTimes }),
            parryRate = rate(skills.sumOf { it.parryTimes })
        )
    }

    private fun battleHash(
        server: Int,
        nickname: String,
        mobCode: Int,
        startedAt: Long,
        endedAt: Long,
        totalDamage: Long,
        durationMs: Long
    ): String {
        val roundedStart = startedAt / 10_000L * 10_000L
        val roundedEnd = endedAt / 10_000L * 10_000L
        val raw = listOf(server, nickname, mobCode, roundedStart, roundedEnd, totalDamage, durationMs)
            .joinToString("|")
        return sha256(raw)
    }

    private fun characterIdentityHash(server: Int, nickname: String): String? {
        if (server <= 0) return null
        val normalizedName = nickname.trim().lowercase()
        if (normalizedName.isBlank()) return null
        return sha256("$IDENTITY_HASH_VERSION|$server|$normalizedName")
    }

    private fun sha256(raw: String): String {
        val digest = MessageDigest.getInstance("SHA-256").digest(raw.toByteArray(Charsets.UTF_8))
        return digest.joinToString("") { "%02x".format(it) }
    }

    private fun oneDecimal(value: Double): Double = kotlin.math.round(value * 10.0) / 10.0

    sealed class BuildResult {
        data class Payload(val payload: StatsUploadPayload) : BuildResult()
        data class Skip(val reason: String) : BuildResult()
    }
}

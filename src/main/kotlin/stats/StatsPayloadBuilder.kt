package com.tbread.stats

import com.tbread.data.DataManager
import com.tbread.entity.AnalyzedSkill
import com.tbread.entity.DpsLog
import com.tbread.entity.OperatingData
import com.tbread.entity.User
import com.tbread.entity.enums.JobClass
import org.slf4j.LoggerFactory
import kotlin.math.roundToLong

object StatsPayloadBuilder {
    private val logger = LoggerFactory.getLogger(StatsPayloadBuilder::class.java)

    private val synergyJobs = setOf(
        JobClass.TEMPLAR,
        JobClass.GLADIATOR,
        JobClass.CHANTER,
        JobClass.CLERIC
    )

    fun ownCharacter(): StatsOwnCharacter {
        val id = DataManager.executorId()
        val user = DataManager.user(id) ?: return StatsOwnCharacter(false)
        val resolved = resolveUserSnapshot(user)
        return StatsOwnCharacter(
            detected = !resolved.nickname.isNullOrBlank(),
            id = resolved.id,
            nickname = resolved.nickname,
            server = resolved.server,
            job = resolved.job?.className,
            power = resolved.power
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
        val contributors = resolveContributors(report.contributors)
        val resolvedOwn = contributors.firstOrNull { it.id == own.id } ?: own
        val totalDamage = ownInfo.amount.roundToLong()
        val ownSkills = log.skillDetails[own.id].orEmpty()
        val skillPayloads = buildSkillPayloads(ownSkills, totalDamage)
        val resultRates = summarizeRates(ownSkills.values)
        val participantUsers = sortedParticipantUsers(log, contributors)
        val participantIndexById = participantUsers
            .mapIndexed { index, user -> user.id to index }
            .toMap()
        val participantPayloads = buildParticipantPayloads(log, own.id, participantUsers, participantIndexById)
        if (resolvedOwn.power <= 0) return BuildResult.Skip("own_power_unresolved")
        if (participantPayloads.any { it.power <= 0 }) return BuildResult.Skip("participant_power_unresolved")
        val ownIdentityHash = StatsIdentity.characterIdentityHash(own.server, ownNickname)
            ?: return BuildResult.Skip("own_identity_missing")
        val jobCounts = contributors
            .mapNotNull { it.job?.className }
            .groupingBy { it }
            .eachCount()
        val synergy = buildSynergy(contributors)
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
                identityHashVersion = StatsIdentity.IDENTITY_HASH_VERSION,
                consentVersion = StatsConsentManager.CONSENT_VERSION,
                uploadedAt = System.currentTimeMillis(),
                character = StatsCharacterPayload(
                    identityHash = ownIdentityHash,
                    nickname = ownNickname,
                    server = own.server,
                    job = resolvedOwn.job?.className,
                    power = resolvedOwn.power,
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
                buffs = log.buffRates[own.id].orEmpty().map {
                    toBuffPayload(
                        value = it,
                        scope = "participant",
                        category = "buff",
                        ownerParticipantIndex = participantIndexById[own.id],
                        actorParticipantIndex = participantIndexById[it.actorId]
                    )
                },
                bossDebuffs = log.bossBuffRates.map {
                    toBuffPayload(
                        value = it,
                        scope = "boss",
                        category = "debuff",
                        ownerParticipantIndex = null,
                        actorParticipantIndex = participantIndexById[it.actorId]
                    )
                }
            )
        )
    }

    private fun sortedParticipantUsers(log: DpsLog, contributors: Collection<User>): List<User> {
        return contributors
            .filter { user -> (log.report.information[user.id]?.amount ?: 0.0) > 0.0 }
            .sortedByDescending { user -> log.report.information[user.id]?.amount ?: 0.0 }
    }

    private fun buildParticipantPayloads(
        log: DpsLog,
        ownId: Int,
        participants: List<User>,
        participantIndexById: Map<Int, Int>
    ): List<StatsParticipantPayload> {
        return participants.mapNotNull { user ->
            val info = log.report.information[user.id] ?: return@mapNotNull null

            val totalDamage = info.amount.roundToLong()
            val skills = log.skillDetails[user.id].orEmpty()
            val rates = summarizeRates(skills.values)
            StatsParticipantPayload(
                identityHash = user.nickname
                    ?.takeIf { it.isNotBlank() }
                    ?.let { StatsIdentity.characterIdentityHash(user.server, it) },
                isUploader = user.id == ownId,
                job = user.job?.className,
                power = user.power,
                result = buildResultPayload(info, rates),
                skills = buildSkillPayloads(skills, totalDamage),
                buffs = log.buffRates[user.id].orEmpty().map {
                    toBuffPayload(
                        value = it,
                        scope = "participant",
                        category = "buff",
                        ownerParticipantIndex = participantIndexById[user.id],
                        actorParticipantIndex = participantIndexById[it.actorId]
                    )
                }
            )
        }
    }

    private fun resolveContributors(contributors: Collection<User>): List<User> {
        return contributors.map(::resolveUserSnapshot)
    }

    private fun resolveUserSnapshot(user: User): User {
        val resolved = user.copy()
        mergeUserInfo(resolved, DataManager.user(user.id))
        val nickname = resolved.nickname?.takeIf { it.isNotBlank() }
        val server = resolved.server

        if (resolved.power <= 0 && nickname != null && server > 0) {
            mergeUserInfo(resolved, DataManager.findUserByNicknameAndServer(nickname, server))
        }

        if (resolved.power <= 0 && nickname != null && server > 0) {
            DataManager.resolveOfficialCharacterInfo(resolved.id, nickname, server, resolved.job)?.let { info ->
                if (resolved.nickname.isNullOrBlank()) resolved.nickname = info.nickname
                if (resolved.server <= 0) resolved.server = info.server
                if (resolved.job == null && info.job != null) resolved.job = info.job
                if (info.power > 0) resolved.power = info.power
            }
        }

        if (resolved.power <= 0) {
            logger.warn(
                "통계 payload 전투력 미해결: uid={}, nickname={}, server={}, job={}",
                resolved.id,
                resolved.nickname,
                resolved.server,
                resolved.job?.className
            )
        }

        return resolved
    }

    private fun mergeUserInfo(target: User, source: User?) {
        if (source == null) return
        if (target.nickname.isNullOrBlank() && !source.nickname.isNullOrBlank()) {
            target.nickname = source.nickname
        }
        if (target.server <= 0 && source.server > 0) {
            target.server = source.server
        }
        if (target.job == null && source.job != null) {
            target.job = source.job
        }
        if (target.power <= 0 && source.power > 0) {
            target.power = source.power
        }
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

    private fun toBuffPayload(
        value: OperatingData,
        scope: String,
        category: String,
        ownerParticipantIndex: Int?,
        actorParticipantIndex: Int?
    ): StatsBuffPayload {
        return StatsBuffPayload(
            buffCode = value.code,
            buffName = value.name,
            operatingRate = oneDecimal(value.operatingRate),
            scope = scope,
            category = category,
            ownerParticipantIndex = ownerParticipantIndex,
            actorParticipantIndex = actorParticipantIndex
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
        return StatsIdentity.sha256(raw)
    }

    private fun oneDecimal(value: Double): Double = kotlin.math.round(value * 10.0) / 10.0

    sealed class BuildResult {
        data class Payload(val payload: StatsUploadPayload) : BuildResult()
        data class Skip(val reason: String) : BuildResult()
    }
}

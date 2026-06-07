package com.tbread

import com.tbread.data.DataManager
import com.tbread.entity.*
import com.tbread.entity.enums.JobClass
import com.tbread.entity.enums.SpecialDamage
import com.tbread.stats.StatsUploadQueue
import org.slf4j.LoggerFactory

class DpsCalculator(private val streamResetCallback: (() -> Unit)? = null) {
    private val logger = LoggerFactory.getLogger(DpsCalculator::class.java)

    companion object {
        private const val PREEMPTIVE_PACKET_WINDOW_MS = 1000L
    }

    private var currentTarget: Int = 0
    private var currentBattleRevision: Long = 0
    private var recentTargetWasDummy: Boolean = false

    private var recentData = DpsReport()
    private var recentDataSaved = false
    private var recentSkillDetails: Map<Int, HashMap<String, AnalyzedSkill>> = emptyMap()
    private var recentBuffRates: Map<Int, List<OperatingData>> = emptyMap()
    private var recentBossBuffRates: List<OperatingData> = emptyList()

    private var lastProcessedSequence = 0L
    private val cachedInfo = HashMap<Int, DpsInformation>()
    private val cachedContributors = mutableSetOf<User>()
    private var cachedBattleEnd = 0L
    private var cachedBattleStart = 0L
    private var isCachedBattleStartFake = false
    private val cachedSkillDetails = hashMapOf<Int, HashMap<String, AnalyzedSkill>>()

    private val summonDamageSkillPrefixes = listOf(
        "불의 정령:",
        "물의 정령:",
        "바람의 정령:",
        "땅의 정령:",
        "고대의 정령:"
    )

    private fun resetCache() {
        lastProcessedSequence = 0L
        cachedInfo.clear()
        cachedContributors.clear()
        cachedSkillDetails.clear()
        cachedBattleEnd = 0L
        cachedBattleStart = 0L
        isCachedBattleStartFake = false
    }

    fun getRecentData(): DpsReport {
        return recentData
    }

    private fun isSummonDamageSkill(skillCode: Int): Boolean {
        val skillName = DataManager.skill(skillCode.toLong())?.name ?: return false
        return summonDamageSkillPrefixes.any { skillName.startsWith(it) }
    }

    private fun resolveActor(packet: ParsedDamagePacket, candidates: Collection<User>): Int? {
        DataManager.summonerId(packet.getActorId())?.let { return it }

        if (isSummonDamageSkill(packet.getSkillCode1())) {
            val elementalists = candidates
                .filter { it.job == JobClass.ELEMENTALIST }
                .map { it.id }
                .distinct()
            return elementalists.singleOrNull()
        }

        if (DataManager.isMobInstance(packet.getActorId())) return null
        return packet.getActorId()
    }

    private fun cloneSkillDetails(
        source: Map<Int, HashMap<String, AnalyzedSkill>>
    ): Map<Int, HashMap<String, AnalyzedSkill>> {
        return source.mapValues { (_, skills) ->
            HashMap(skills.mapValues { (_, skill) -> skill.copy() })
        }
    }

    private fun accumulateSkillDetail(
        target: MutableMap<Int, HashMap<String, AnalyzedSkill>>,
        packet: ParsedDamagePacket,
        actor: Int
    ) {
        val skillCode = packet.getSkillCode1().toString()
        val actorSkills = target.getOrPut(actor) { hashMapOf() }
        if (!actorSkills.containsKey(skillCode)) {
            val skill = DataManager.skill(packet.getSkillCode1().toLong())
            val analyzedSkill = AnalyzedSkill(packet)
            analyzedSkill.name = skill?.name ?: skillCode
            actorSkills[skillCode] = analyzedSkill
        }

        val analyzedSkill = actorSkills[skillCode]!!
        if (packet.isDoT()) {
            analyzedSkill.dotTimes++
            analyzedSkill.dotDamageAmount += packet.getDamage()
        } else {
            analyzedSkill.times++
            analyzedSkill.damageAmount += packet.getDamage()
            if (packet.isCrit()) analyzedSkill.critTimes++
            if (packet.getSpecials().contains(SpecialDamage.BACK)) analyzedSkill.backTimes++
            if (packet.getSpecials().contains(SpecialDamage.PARRY)) analyzedSkill.parryTimes++
            if (packet.getSpecials().contains(SpecialDamage.DOUBLE)) analyzedSkill.doubleTimes++
            if (packet.getSpecials().contains(SpecialDamage.PERFECT)) analyzedSkill.perfectTimes++
            if (packet.getSpecials().contains(SpecialDamage.POWER_SHARD)) analyzedSkill.shardTimes++
            if (packet.getLoop() != 0) analyzedSkill.multiHitTimes++
        }
    }

    private fun accumulatePacket(packet: ParsedDamagePacket) {
        val actor = resolveActor(packet, cachedContributors) ?: return
        val user = DataManager.user(actor) ?: return
        cachedContributors.remove(user)
        cachedContributors.add(user)
        if (user.job == null) {
            user.job = JobClass.convertFromSkill(packet.getSkillCode1())
        }
        if (!user.nickname.isNullOrBlank() && user.server > 0 && user.power <= 0) {
            DataManager.requestOfficialCharacterLookup(user.id)
        }
        cachedInfo.getOrPut(user.id) { DpsInformation() }.addDamage(packet.getDamage().toDouble())
        accumulateSkillDetail(cachedSkillDetails, packet, user.id)
        val ts = packet.getTimeStamp()
        if (cachedBattleStart == 0L) {
            cachedBattleStart = ts; isCachedBattleStartFake = true
        }
        if (isCachedBattleStartFake && cachedBattleStart > ts) cachedBattleStart = ts
        if (ts > cachedBattleEnd) cachedBattleEnd = ts
    }

    private fun activePacketCutoff(): Long {
        val start = DataManager.currentBattleStart()
        return if (start > PREEMPTIVE_PACKET_WINDOW_MS) start - PREEMPTIVE_PACKET_WINDOW_MS else 0L
    }

    private fun processPendingPacketsBefore(targetId: Int, before: Long) {
        if (targetId <= 0) return
        val window = DataManager.battleDataSince(targetId, lastProcessedSequence)
        if (window.droppedBeforeStart) {
            logger.warn(
                "패킷 ring buffer가 이전 처리 지점보다 앞서 덮어써졌습니다. target={}, retained={}",
                targetId,
                window.totalSize
            )
            resetCache()
        }
        window.packets
            .asSequence()
            .filter { before <= 0L || it.getTimeStamp() < before }
            .forEach(::accumulatePacket)
        lastProcessedSequence = window.nextSequence
    }

    private fun refreshRecentReportFromCache(targetId: Int, fixedTarget: MobInfo? = recentData.target) {
        val battleStart = cachedBattleStart.takeIf { it > 0L } ?: recentData.battleStart
        val battleEnd = (cachedBattleEnd.takeIf { it > 0L } ?: recentData.battleEnd)
            .coerceAtLeast(battleStart)
        val targetInfo = fixedTarget?.copy(mob = fixedTarget.mob.copy()) ?: run {
            val mobCode = DataManager.mobId(targetId) ?: return@run null
            val mob = DataManager.mob(mobCode) ?: return@run null
            MobInfo(
                targetId,
                mob,
                DataManager.mobHp(targetId) ?: 0,
                DataManager.mobMaxHp(targetId) ?: 0
            )
        }
        val report = DpsReport(
            contributors = cachedContributors.toMutableSet(),
            battleStart = battleStart,
            battleEnd = battleEnd,
            packets = null
        )
        report.target = targetInfo

        val totalDamage = cachedInfo.values.sumOf { it.amount }
        val duration = report.battleEnd - report.battleStart
        val mobMaxHp = targetInfo?.maxHp?.takeIf { it > 0 }?.toDouble()
            ?: DataManager.mobMaxHp(targetId)?.toDouble()
            ?: 0.0
        cachedInfo.forEach { (uid, cached) ->
            report.information[uid] = DpsInformation(
                amount = cached.amount,
                dps = if (duration > 0) cached.amount / duration * 1000 else 0.0,
                contribution = if (totalDamage > 0) cached.amount / totalDamage * 100 else 0.0,
                entireContribution = if (mobMaxHp > 0) cached.amount / mobMaxHp * 100 else 0.0
            )
        }

        recentData = report
    }

    fun getLiveReport(): DpsReport {
        val storageTarget = DataManager.currentTarget()
        if (storageTarget == -1) return recentData
        if (!recentData.isEmpty() && recentData.target?.id == storageTarget) return recentData
        return DpsReport(
            battleStart = DataManager.currentBattleStart(),
            battleEnd = DataManager.currentBattleEnd(),
            packets = DataManager.battleData(storageTarget)
        )
    }

    fun getDps(): DpsReport {
        val storageTarget = DataManager.currentTarget()
        val storageBattleRevision = DataManager.currentBattleRevision()
        val previousTarget = currentTarget
        val prevTargetDummy = DataManager.isCurrentTargetDummy()
        val targetChanged = storageTarget != previousTarget
        val battleRestartedWithSameTarget =
            storageTarget > 0 && previousTarget > 0 && storageBattleRevision != currentBattleRevision
        val isNewBattleEnd = storageTarget == -1 && storageTarget != previousTarget
        if ((targetChanged || battleRestartedWithSameTarget) && !prevTargetDummy
            && storageTarget != -1 && previousTarget > 0 && !recentData.isEmpty()
        ) {
            processPendingPacketsBefore(previousTarget, activePacketCutoff())
            refreshRecentReportFromCache(previousTarget, recentData.target)
            saveRecentBattleLog()
            recentDataSaved = true
        }
        if (targetChanged || battleRestartedWithSameTarget) {
            resetCache()
        }
        currentTarget = storageTarget
        currentBattleRevision = storageBattleRevision
        recentTargetWasDummy = prevTargetDummy
        if (currentTarget == -1) {
            val battleEnd = DataManager.currentBattleEnd()
            if (isNewBattleEnd) {
                recentData.battleEnd = battleEnd
                if (previousTarget > 0) {
                    processPendingPacketsBefore(previousTarget, 0L)
                    refreshRecentReportFromCache(previousTarget, recentData.target)
                    recentData.target?.remainHp = DataManager.mobHp(previousTarget) ?: recentData.target?.remainHp ?: 0
                    recentData.target?.maxHp = DataManager.mobMaxHp(previousTarget) ?: recentData.target?.maxHp ?: 0
                }
            }
            DataManager.flushPacket()
            if (isNewBattleEnd && !recentData.isEmpty() && !recentTargetWasDummy) {
                saveRecentBattleLog()
                recentDataSaved = true
            }
            return recentData
        }

        if (currentTarget == 0) {
            return DpsReport()
        }

        val reportPackets = if (currentTarget > 0) {
            val window = DataManager.battleDataSince(currentTarget, lastProcessedSequence)
            if (window.droppedBeforeStart) {
                logger.warn(
                    "패킷 ring buffer가 이전 처리 지점보다 앞서 덮어써졌습니다. target={}, retained={}",
                    currentTarget,
                    window.totalSize
                )
                resetCache()
            }
            val packetCutoff = activePacketCutoff()
            window.packets
                .asSequence()
                .filter { it.getTimeStamp() >= packetCutoff }
                .forEach(::accumulatePacket)
            lastProcessedSequence = window.nextSequence
            null
        } else {
            val data = DataManager.battleData(currentTarget)
            val currentCount = data?.size ?: 0
            if (currentCount.toLong() < lastProcessedSequence) {
                resetCache()
            }
            val fromIndex = lastProcessedSequence.toInt().coerceIn(0, currentCount)
            if (data != null && currentCount > fromIndex) {
                for (i in fromIndex until currentCount) {
                    accumulatePacket(data[i])
                }
                lastProcessedSequence = currentCount.toLong()
            }
            data
        }

        val dmStart = DataManager.currentBattleStart()
        val dmEnd = DataManager.currentBattleEnd()
        val report = DpsReport(
            contributors = cachedContributors.toMutableSet(),
            battleStart = when {
                dmStart != 0L && cachedBattleStart != 0L -> minOf(dmStart, cachedBattleStart)
                dmStart != 0L -> dmStart
                else -> cachedBattleStart
            },
            battleEnd = maxOf(dmEnd, cachedBattleEnd),
            packets = reportPackets
        )

        if (currentTarget > 0) {
            val mobCode = DataManager.mobId(currentTarget)
            val mob = DataManager.mob(mobCode!!)
            report.target = MobInfo(currentTarget, mob!!)
            report.target!!.remainHp = DataManager.mobHp(currentTarget) ?: 0
            report.target!!.maxHp = DataManager.mobMaxHp(currentTarget) ?: 0

        }

        val totalDamage = cachedInfo.values.sumOf { it.amount }
        val duration = report.battleEnd - report.battleStart
        val mobMaxHp = DataManager.mobMaxHp(currentTarget)?.toDouble() ?: 0.0
        cachedInfo.forEach { (uid, cached) ->
            report.information[uid] = DpsInformation(
                amount = cached.amount,
                dps = if (duration > 0) cached.amount / duration * 1000 else 0.0,
                contribution = if (totalDamage > 0) cached.amount / totalDamage * 100 else 0.0,
                entireContribution = if (mobMaxHp > 0) cached.amount / mobMaxHp * 100 else 0.0
            )
        }

        if (DataManager.isCurrentTargetDummy()) {
            val executorId = DataManager.executorId()
            if (executorId != 0 && !report.contributors.any { it.isExecutor }) {
                return recentData
            }
        }

        recentData = report
        recentSkillDetails = emptyMap()
        recentBuffRates = emptyMap()
        recentBossBuffRates = emptyList()
        recentDataSaved = false
        return report
    }

    private fun buildSkillDetails(data: DpsReport): Map<Int, HashMap<String, AnalyzedSkill>> {
        val analyzedByActor = hashMapOf<Int, HashMap<String, AnalyzedSkill>>()
        val contributorIds = data.contributors.mapTo(hashSetOf()) { it.id }
        data.packets?.forEach {
            val realActor = resolveActor(it, data.contributors) ?: return@forEach
            if (realActor !in contributorIds) return@forEach
            accumulateSkillDetail(analyzedByActor, it, realActor)
        }
        return analyzedByActor
    }

    fun battleDetails(data: DpsReport?, uid: Int): HashMap<String, AnalyzedSkill> {
        if (data == null) return hashMapOf()
        if (data === recentData && data.packets == null) {
            val details = cachedSkillDetails[uid] ?: recentSkillDetails[uid] ?: emptyMap()
            return HashMap(details.mapValues { it.value.copy() })
        }
        return HashMap(buildSkillDetails(data)[uid] ?: emptyMap())
    }

    private data class BuffDisplay(
        val code: Int,
        val name: String,
        val summary: String?,
        val effect: String?
    )

    private fun isPlaceholderBuff(buff: Buff): Boolean {
        return buff.name.isBlank() || buff.name.equals("None", ignoreCase = true)
    }

    private fun normalizeBuffSkillCode(code: Int): Int? {
        val candidates = linkedSetOf<Int>()

        fun addCandidate(value: Int) {
            if (value <= 0) return
            candidates.add(value)
            candidates.add((value / 10) * 10)
        }

        addCandidate(code)
        if (code in 110_000_000..190_999_999) {
            addCandidate((code / 100_000) * 10_000)
            addCandidate((code / 10_000) * 1_000)
        }
        addCandidate(code / 10)
        addCandidate(code / 100)

        return candidates.firstOrNull { DataManager.skill(it.toLong()) != null }
    }

    private fun resolveBuffDisplay(code: Int): BuffDisplay? {
        val buff = DataManager.buff(code)
        if (buff != null) {
            if (isPlaceholderBuff(buff)) return null
            return BuffDisplay(code, buff.name, buff.summary, buff.effect)
        }

        val skillCode = normalizeBuffSkillCode(code) ?: return null
        val skill = DataManager.skill(skillCode.toLong()) ?: return null
        val name = skill.name?.takeIf { it.isNotBlank() && !it.equals("None", ignoreCase = true) }
            ?: return null
        return BuffDisplay(skillCode, name, null, null)
    }

    fun getBuffOperatingRate(uid: Int, start: Long, end: Long): List<OperatingData> {
        val totalDuration = end - start
        if (totalDuration <= 0) return emptyList()

        return DataManager.battleBuff(uid, start, end)
            .filter { !DataManager.isBuffBlacklisted(it.skillCode) }
            .mapNotNull { useBuff ->
                resolveBuffDisplay(useBuff.skillCode)?.let { display -> display to useBuff }
            }
            .groupBy { (display, useBuff) -> display.code to useBuff.actorId }
            .map { (key, entries) ->
                val (_, actorId) = key
                val display = entries.first().first
                val buffs = entries.map { it.second }
                val clamped = buffs
                    .map { maxOf(it.buffStart, start) to minOf(it.buffEnd, end) }
                    .sortedBy { it.first }

                val merged = mutableListOf<Pair<Long, Long>>()
                for (interval in clamped) {
                    if (merged.isEmpty() || interval.first > merged.last().second) {
                        merged.add(interval)
                    } else {
                        val last = merged.removeLast()
                        merged.add(last.first to maxOf(last.second, interval.second))
                    }
                }

                val rate = merged.sumOf { it.second - it.first }.toDouble() / totalDuration * 100.0
                OperatingData(display.code, display.name, display.summary, display.effect, rate, actorId)
            }
    }

    private fun buildBuffRates(data: DpsReport): Map<Int, List<OperatingData>> {
        if (data.battleEnd <= data.battleStart) return emptyMap()
        return data.contributors.associate { user ->
            user.id to getBuffOperatingRate(user.id, data.battleStart, data.battleEnd)
        }
    }

    private fun buildBossBuffRates(data: DpsReport): List<OperatingData> {
        val targetId = data.target?.id ?: return emptyList()
        if (data.battleEnd <= data.battleStart) return emptyList()
        return getBuffOperatingRate(targetId, data.battleStart, data.battleEnd)
    }

    private fun saveRecentBattleLog() {
        val skillDetails = if (cachedSkillDetails.isNotEmpty()) {
            cloneSkillDetails(cachedSkillDetails)
        } else {
            buildSkillDetails(recentData)
        }
        val buffRates = buildBuffRates(recentData)
        val bossBuffRates = buildBossBuffRates(recentData)

        recentSkillDetails = skillDetails
        recentBuffRates = buffRates
        recentBossBuffRates = bossBuffRates

        val log = DataManager.saveBattleLog(recentData, skillDetails, buffRates, bossBuffRates)
        StatsUploadQueue.offerIfEligible(log)
        recentData.packets = null
    }

    fun getLiveBuffOperatingRate(uid: Int): List<OperatingData> {
        val report = getLiveReport()
        if (report === recentData && report.packets == null && recentDataSaved) {
            return recentBuffRates[uid] ?: emptyList()
        }
        val end = if (report.battleEnd == 0L) System.currentTimeMillis() else report.battleEnd
        return getBuffOperatingRate(uid, report.battleStart, end)
    }

    fun getLiveBossBuffOperatingRate(): List<OperatingData> {
        val report = getLiveReport()
        if (report === recentData && report.packets == null && recentDataSaved) {
            return recentBossBuffRates
        }
        val targetId = report.target?.id ?: return emptyList()
        val end = if (report.battleEnd == 0L) System.currentTimeMillis() else report.battleEnd
        return getBuffOperatingRate(targetId, report.battleStart, end)
    }

    fun resetDataStorage() {
        if (!recentData.isEmpty() && !recentDataSaved && !DataManager.isCurrentTargetDummy()) {
            saveRecentBattleLog()
            recentDataSaved = true
        }
        DataManager.flushPacket()
        currentTarget = -1
        currentBattleRevision = DataManager.currentBattleRevision()
        recentData = DpsReport()
        recentSkillDetails = emptyMap()
        recentBuffRates = emptyMap()
        recentBossBuffRates = emptyList()
        recentDataSaved = false
        resetCache()
        logger.info("대상 데미지 누적 데이터 초기화 완료")
    }

    fun hardReset() {
        DataManager.hardReset()
        streamResetCallback?.invoke()
        currentTarget = -1
        currentBattleRevision = 0
        recentData = DpsReport()
        recentSkillDetails = emptyMap()
        recentBuffRates = emptyMap()
        recentBossBuffRates = emptyList()
        recentDataSaved = false
        resetCache()
        logger.info("전체 강제 초기화 완료")
    }
}

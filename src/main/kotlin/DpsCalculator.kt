package com.tbread

import com.tbread.data.DataManager
import com.tbread.entity.*
import com.tbread.entity.enums.JobClass
import com.tbread.entity.enums.SpecialDamage
import org.slf4j.LoggerFactory

class DpsCalculator(private val streamResetCallback: (() -> Unit)? = null) {
    private val logger = LoggerFactory.getLogger(DpsCalculator::class.java)

    private var currentTarget: Int = 0
    private var recentTargetWasDummy: Boolean = false

    private var recentData = DpsReport()
    private var recentDataSaved = false

    private var lastProcessedSequence = 0L
    private val cachedInfo = HashMap<Int, DpsInformation>()
    private val cachedContributors = mutableSetOf<User>()
    private var cachedBattleEnd = 0L
    private var cachedBattleStart = 0L
    private var isCachedBattleStartFake = false

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

    private fun accumulatePacket(packet: ParsedDamagePacket) {
        val actor = resolveActor(packet, cachedContributors) ?: return
        var user = DataManager.user(actor)
        if (user == null) {
            user = User(actor, nickname = actor.toString())
            DataManager.saveUser(user.id, user)
        }
        cachedContributors.remove(user)
        cachedContributors.add(user)
        if (user.job == null) {
            user.job = JobClass.convertFromSkill(packet.getSkillCode1())
        }
        cachedInfo.getOrPut(user.id) { DpsInformation() }.addDamage(packet.getDamage().toDouble())
        val ts = packet.getTimeStamp()
        if (cachedBattleStart == 0L) {
            cachedBattleStart = ts; isCachedBattleStartFake = true
        }
        if (isCachedBattleStartFake && cachedBattleStart > ts) cachedBattleStart = ts
        if (ts > cachedBattleEnd) cachedBattleEnd = ts
    }

    fun getLiveReport(): DpsReport {
        val storageTarget = DataManager.currentTarget()
        if (storageTarget == -1) return recentData
        return DpsReport(
            battleStart = DataManager.currentBattleStart(),
            battleEnd = DataManager.currentBattleEnd(),
            packets = DataManager.battleData(storageTarget)
        )
    }

    fun getDps(): DpsReport {
        val storageTarget = DataManager.currentTarget()
        val previousTarget = currentTarget
        val prevTargetDummy = DataManager.isCurrentTargetDummy()
        val isNewBattleEnd = storageTarget == -1 && storageTarget != previousTarget
        if (storageTarget != previousTarget && !prevTargetDummy
            && storageTarget != -1 && previousTarget > 0 && !recentData.isEmpty()
        ) {
            DataManager.battleData(previousTarget)?.let {
                recentData.packets = it
            }
            DataManager.saveBattleLog(recentData)
            recentDataSaved = true
        }
        if (storageTarget != previousTarget) {
            resetCache()
        }
        currentTarget = storageTarget
        recentTargetWasDummy = prevTargetDummy
        if (currentTarget == -1) {
            val battleEnd = DataManager.currentBattleEnd()
            if (isNewBattleEnd) {
                recentData.battleEnd = battleEnd
                if (previousTarget > 0) {
                    DataManager.battleData(previousTarget)?.let {
                        recentData.packets = it
                    }
                }
            }
            DataManager.flushPacket()
            if (isNewBattleEnd && !recentData.isEmpty() && !recentTargetWasDummy) {
                DataManager.saveBattleLog(recentData)
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
            window.packets.forEach(::accumulatePacket)
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
        recentDataSaved = false
        return report
    }

    fun battleDetails(data: DpsReport?, uid: Int): HashMap<String, AnalyzedSkill> {
        val analyzedData: HashMap<String, AnalyzedSkill> = hashMapOf()
        if (data == null) {
            return analyzedData
        }
        data.packets?.forEach {
            val skill = DataManager.skill(it.getSkillCode1().toLong())
            val skillName = it.getSkillCode1().toString()
            val realActor = resolveActor(it, data.contributors) ?: return@forEach
            if (realActor == uid) {
                if (!analyzedData.containsKey(skillName)) {
                    val analyzedSkill = AnalyzedSkill(it)
                    analyzedSkill.name = skill?.name ?: it.getSkillCode1().toString()
                    analyzedData[skillName] = analyzedSkill
                }
                val analyzedSkill = analyzedData[skillName]!!
                if (it.isDoT()) {
                    analyzedSkill.dotTimes++
                    analyzedSkill.dotDamageAmount += it.getDamage()
                } else {
                    analyzedSkill.times++
                    analyzedSkill.damageAmount += it.getDamage()
                    if (it.isCrit()) analyzedSkill.critTimes++
                    if (it.getSpecials().contains(SpecialDamage.BACK)) analyzedSkill.backTimes++
                    if (it.getSpecials().contains(SpecialDamage.PARRY)) analyzedSkill.parryTimes++
                    if (it.getSpecials().contains(SpecialDamage.DOUBLE)) analyzedSkill.doubleTimes++
                    if (it.getSpecials().contains(SpecialDamage.PERFECT)) analyzedSkill.perfectTimes++
                    if (it.getSpecials().contains(SpecialDamage.POWER_SHARD)) analyzedSkill.shardTimes++
                    if (it.getLoop() != 0) analyzedSkill.multiHitTimes++
                }
            }
        }
        return analyzedData
    }

    fun getBuffOperatingRate(uid: Int, start: Long, end: Long): List<OperatingData> {
        val totalDuration = end - start
        if (totalDuration <= 0) return emptyList()

        return DataManager.battleBuff(uid, start, end)
            .filter { !DataManager.isBuffBlacklisted(it.skillCode) }
            .groupBy { it.skillCode to it.actorId }
            .map { (key, buffs) ->
                val (skillCode, actorId) = key
                val buff = DataManager.buff(skillCode)
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
                OperatingData(skillCode, buff, rate, actorId)
            }
    }

    fun resetDataStorage() {
        if (!recentData.isEmpty() && !recentDataSaved && !DataManager.isCurrentTargetDummy()) {
            DataManager.saveBattleLog(recentData)
            recentDataSaved = true
        }
        DataManager.flushPacket()
        currentTarget = -1
        recentData = DpsReport()
        recentDataSaved = false
        resetCache()
        logger.info("대상 데미지 누적 데이터 초기화 완료")
    }

    fun hardReset() {
        DataManager.hardReset()
        streamResetCallback?.invoke()
        currentTarget = -1
        recentData = DpsReport()
        recentDataSaved = false
        resetCache()
        logger.info("전체 강제 초기화 완료")
    }
}

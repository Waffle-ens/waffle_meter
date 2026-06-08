package com.tbread.data

import com.tbread.config.PropertyHandler
import com.tbread.data.repository.*
import com.tbread.entity.*
import com.tbread.entity.enums.JobClass
import com.tbread.official.OfficialCharacterInfo
import com.tbread.official.OfficialCharacterLookup
import kotlinx.serialization.json.*
import org.slf4j.LoggerFactory
import java.io.File
import java.time.Instant
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.ConcurrentLinkedDeque
import java.util.concurrent.atomic.AtomicInteger
import java.util.concurrent.atomic.AtomicLong

object DataManager {
    private val logger = LoggerFactory.getLogger(DataManager::class.java)

    private data class EndedBattle(val mobCode: Int?, val endedAt: Long)

    private const val ENDED_BATTLE_START_IGNORE_MS = 30 * 60 * 1000L

    private val resetEpoch = AtomicLong(0)
    private val battleRevision = AtomicLong(0)
    private val recentlyEndedBattles = ConcurrentHashMap<Int, EndedBattle>()
    @Volatile
    private var activeBattleMobCode: Int? = null

    fun currentEpoch(): Long = resetEpoch.get()
    fun currentBattleRevision(): Long = battleRevision.get()

    /*
    rawPacket 버퍼 영역
     */
    private const val RAW_PACKET_LIMIT = 200_000
    private const val RAW_PACKET_RETENTION_MS = 15 * 60 * 1000L

    private val rawPacketBuffer = ConcurrentLinkedDeque<RawPacket>()
    private val rawPacketCount = AtomicInteger(0)

    // 무손실 연속 패킷 캡처(분석용): 롤링 버퍼 eviction과 무관하게 세션 전체를 파일에 append 한다.
    @Volatile
    private var continuousPacketWriter: java.io.BufferedWriter? = null
    private val continuousLogLock = Any()
    private var continuousLogLines = 0L
    private val packetLogFileFormatter = DateTimeFormatter
        .ofPattern("yyyyMMdd-HHmmss")
        .withZone(ZoneId.systemDefault())

    @Volatile
    private var packetLoggingEnabled =
        PropertyHandler.getProperty("packetLoggingMode")?.toBooleanStrictOrNull() ?: false

    fun isPacketLoggingEnabled(): Boolean {
        return packetLoggingEnabled
    }

    fun setPacketLoggingEnabled(enabled: Boolean) {
        packetLoggingEnabled = enabled
        PropertyHandler.setProperty("packetLoggingMode", enabled.toString())
        if (!enabled) {
            closeContinuousPacketLog()
            clearRawPackets()
        }
    }

    fun saveRawPacket(data: ByteArray, timestamp: Long) {
        if (!packetLoggingEnabled) return
        rawPacketBuffer.add(RawPacket(data.copyOf(), timestamp))
        rawPacketCount.incrementAndGet()
        trimRawPackets(timestamp)
        appendContinuousPacketLog(timestamp, data)
    }

    fun rawPacketsInRange(from: Long, to: Long): List<RawPacket> {
        return rawPacketBuffer.filter { it.timestamp in from..to }
    }

    fun exportRawPacketLog(label: String = "manual"): String {
        val now = System.currentTimeMillis()
        val first = rawPacketBuffer.peekFirst()
        val packets = rawPacketsInRange(first?.timestamp ?: now, now)
        return writeRawPacketLog(label, first?.timestamp ?: now, now, packets)
    }

    private fun trimRawPackets(now: Long) {
        val cutoff = now - RAW_PACKET_RETENTION_MS
        while (true) {
            val first = rawPacketBuffer.peekFirst() ?: break
            if (first.timestamp >= cutoff) break
            pollRawPacket()
        }
        while (rawPacketCount.get() > RAW_PACKET_LIMIT) {
            if (pollRawPacket() == null) break
        }
    }

    private fun dropRawPacketsUntil(timestamp: Long) {
        while (true) {
            val first = rawPacketBuffer.peekFirst() ?: break
            if (first.timestamp > timestamp) break
            pollRawPacket()
        }
    }

    private fun pollRawPacket(): RawPacket? {
        val packet = rawPacketBuffer.pollFirst()
        if (packet != null) rawPacketCount.decrementAndGet()
        return packet
    }

    private fun clearRawPackets() {
        rawPacketBuffer.clear()
        rawPacketCount.set(0)
    }

    private fun writeRawPacketLog(label: String, from: Long, to: Long, packets: List<RawPacket>): String {
        val dir = File(PropertyHandler.appDirectory(), "packet-logs").also { it.mkdirs() }
        val createdAt = System.currentTimeMillis()
        val fileName = "${packetLogFileFormatter.format(Instant.ofEpochMilli(createdAt))}-${safeFileToken(label)}-${packets.size}.log"
        val file = File(dir, fileName)

        file.bufferedWriter(Charsets.UTF_8).use { writer ->
            writer.appendLine("# waffle_meter packet log")
            writer.appendLine("# createdAt=$createdAt")
            writer.appendLine("# from=$from")
            writer.appendLine("# to=$to")
            writer.appendLine("# packetCount=${packets.size}")
            writer.appendLine("# format=timestamp\\tpacketSize\\thex")
            packets.forEach { packet ->
                writer.append(packet.timestamp.toString())
                writer.append('\t')
                writer.append(packet.data.size.toString())
                writer.append('\t')
                writer.appendLine(packet.data.toHexString())
            }
        }

        logger.info("패킷 로그 저장 완료: {}", file.absolutePath)
        return file.absolutePath
    }

    private fun safeFileToken(value: String): String {
        return value
            .replace(Regex("[^A-Za-z0-9가-힣._-]+"), "_")
            .trim('_')
            .take(60)
            .ifBlank { "unknown" }
    }

    private fun ByteArray.toHexString(): String {
        return joinToString(separator = "") { "%02X".format(it.toInt() and 0xff) }
    }

    private fun appendContinuousPacketLog(timestamp: Long, data: ByteArray) {
        synchronized(continuousLogLock) {
            if (continuousPacketWriter == null) openContinuousPacketLogLocked()
            val writer = continuousPacketWriter ?: return
            runCatching {
                writer.append(timestamp.toString())
                writer.append('\t')
                writer.append(data.size.toString())
                writer.append('\t')
                writer.appendLine(data.toHexString())
                if (++continuousLogLines % 512L == 0L) writer.flush()
            }.onFailure {
                logger.warn("연속 패킷 로그 기록 실패", it)
                runCatching { writer.close() }
                continuousPacketWriter = null
            }
        }
    }

    private fun openContinuousPacketLogLocked() {
        runCatching {
            val dir = File(PropertyHandler.appDirectory(), "packet-logs").also { it.mkdirs() }
            val createdAt = System.currentTimeMillis()
            val file = File(dir, "${packetLogFileFormatter.format(Instant.ofEpochMilli(createdAt))}-session.log")
            val writer = file.bufferedWriter(Charsets.UTF_8, 1 shl 20)
            writer.appendLine("# waffle_meter packet log (continuous session)")
            writer.appendLine("# createdAt=$createdAt")
            writer.appendLine("# format=timestamp\\tpacketSize\\thex")
            continuousPacketWriter = writer
            continuousLogLines = 0L
            logger.info("연속 패킷 로그 시작: {}", file.absolutePath)
        }.onFailure { logger.warn("연속 패킷 로그 시작 실패", it) }
    }

    private fun closeContinuousPacketLog() {
        synchronized(continuousLogLock) {
            continuousPacketWriter?.let { w -> runCatching { w.flush(); w.close() } }
            continuousPacketWriter = null
        }
    }

    private val mobIdRepository = MobIdRepository()
    private val mobRepository = MobRepository()
    private val userRepository = UserRepository()
    private val packetRepository = PacketRepository()
    private val summonRepository = SummonRepository()
    private val battleLogRepository = BattleLogRepository()
    private val skillRepository = SkillRepository()
    private val mobHpRepository = MobHpRepository()
    private val useBuffRepository = UseBuffRepository()
    private val buffRepository = BuffRepository()
    private val officialLookupAttempts = ConcurrentHashMap<Int, Long>()
    // 본인 전투력 강제 재검증(캐시 우회) 전용 throttle. 장비 교체 반영용, 호출량 억제.
    private val forceVerifyAttempts = ConcurrentHashMap<Int, Long>()
    private const val SELF_VERIFY_THROTTLE_MS = 60_000L

    private val buffBlacklist = mutableSetOf<Int>()

    fun isBuffBlacklisted(code: Int): Boolean = code in buffBlacklist

    fun load() {
        loadMobJson()
        loadSkillJson()
        loadBuffJson()
        loadCustomBuffJson()
        loadBuffBlacklistJson()
    }

    private fun requiredResourceText(path: String): String =
        resourceText(path) ?: error("Missing required resource: $path")

    private fun resourceText(path: String): String? =
        DataManager::class.java.getResourceAsStream(path)
            ?.bufferedReader()
            ?.use { it.readText() }

    private fun loadMobJson() {
        val mobJson = requiredResourceText("/json/mobs.json")
        Json.decodeFromString<List<Mob>>(mobJson).forEach { saveMob(it) }
    }

    private fun loadSkillJson() {
        val skillJson = requiredResourceText("/json/skills.json")
        Json.decodeFromString<List<Skill>>(skillJson).forEach {
            saveSkill(it)
        }
    }

    private fun loadBuffJson() {
        try {
            val buffJson = resourceText("/json/buff.json") ?: return

            val json = Json { ignoreUnknownKeys = true }

            json.decodeFromString<JsonObject>(buffJson).forEach { (code, element) ->
                val obj = element.jsonObject
                val buff = obj["effect"]?.jsonPrimitive?.contentOrNull?.let {
                    obj["summary"]?.jsonPrimitive?.contentOrNull?.let { it1 ->
                        Buff(
                            code = code.toInt(),
                            name = obj["name"]?.jsonPrimitive?.content ?: "",
                            summary = it1,
                            effect = it
                        )
                    }
                }
                buff?.let { saveBuff(it) }
            }
        } catch (e: Exception) {
            e.printStackTrace()
        }
    }

    private fun loadBuffBlacklistJson() {
        try {
            val json = resourceText("/json/buff_blacklist.json") ?: return
            Json.decodeFromString<JsonObject>(json)["blacklist"]
                ?.jsonArray
                ?.forEach { buffBlacklist.add(it.jsonPrimitive.int) }
        } catch (e: Exception) {
            e.printStackTrace()
        }
    }

    private fun loadCustomBuffJson() {
        try {
            val buffJson = resourceText("/json/buff_custom.json") ?: return

            val json = Json { ignoreUnknownKeys = true }

            json.decodeFromString<JsonObject>(buffJson).forEach { (code, element) ->
                val obj = element.jsonObject
                val buff = obj["effect"]?.jsonPrimitive?.contentOrNull?.let {
                    obj["summary"]?.jsonPrimitive?.contentOrNull?.let { it1 ->
                        Buff(
                            code = code.toInt(),
                            name = obj["name"]?.jsonPrimitive?.content ?: "",
                            summary = it1,
                            effect = it
                        )
                    }
                }
                buff?.let { saveBuff(it) }
            }
        } catch (e: Exception) {
            e.printStackTrace()
        }
    }

    @Synchronized
    fun hardReset() {
        resetEpoch.incrementAndGet()
        battleRevision.set(0)
        battleLogRepository.flush()
        mobHpRepository.flush()
        mobIdRepository.flush()
        packetRepository.flush()
        summonRepository.flush()
        userRepository.flush()
        useBuffRepository.flush()
        clearRawPackets()
        recentlyEndedBattles.clear()
        activeBattleMobCode = null
        lastDummyHitTime = 0
    }

    /*
    mobHp 영역
     */

    fun mobHp(mobId: Int): Int? {
        return mobHpRepository.get(mobId)
    }

    fun mobHp(mobId: Int, mobHp: Int) {
        mobHpRepository.set(mobId, mobHp)
        if (mobHp > 0) {
            recentlyEndedBattles.remove(mobId)
            saveMobMaxHp(mobId, mobHp)
        }
    }

    /*
    skill 영역
     */
    private fun saveSkill(skill: Skill) {
        return skillRepository.save(skill.code, skill)
    }

    fun skill(skillId: Long): Skill? {
        return skillRepository.get(skillId)
    }


    /*
    packet 영역
     */
    fun battleData(targetId: Int): MutableList<ParsedDamagePacket>? {
        if (targetId <= 0) return null
        return packetRepository.get(targetId)
    }

    fun battleDataSince(targetId: Int, sequence: Long): PacketRepository.PacketWindow {
        if (targetId <= 0) return PacketRepository.PacketWindow(
            packets = emptyList(),
            nextSequence = sequence,
            droppedBeforeStart = false,
            totalSize = 0
        )
        return packetRepository.getWindow(targetId, sequence)
    }

    fun currentTarget(): Int {
        return packetRepository.currentTarget()
    }

    fun isCurrentTargetDummy(): Boolean {
        val current = currentTarget()
        if (current <= 0) return false
        return mobId(current)?.let { mob(it) }?.isDummy == true
    }

    fun executorId(): Int = userRepository.executor()

    @Volatile
    private var lastDummyHitTime: Long = 0
    private val DUMMY_TIMEOUT_MS = 5000L

    fun touchDummyBattle(mobId: Int, epoch: Long) {
        if (resetEpoch.get() != epoch) return
        lastDummyHitTime = System.currentTimeMillis()
        if (currentTarget() <= 0) {
            saveCurrentBattleStart()
            saveCurrentTarget(mobId)
        }
    }

    fun checkDummyTimeout() {
        val current = currentTarget()
        if (current <= 0) return
        if (!isCurrentTargetDummy()) return
        if (System.currentTimeMillis() - lastDummyHitTime > DUMMY_TIMEOUT_MS) {
            saveCurrentBattleEnd(lastDummyHitTime)
            saveCurrentTarget(-1)
            lastDummyHitTime = 0
        }
    }

    @Synchronized
    fun startBattle(mobId: Int) {
        val mobCode = DataManager.mobId(mobId)
        val now = System.currentTimeMillis()
        val endedBattle = recentlyEndedBattles[mobId]
        if (
            currentTarget() <= 0 &&
            endedBattle != null &&
            endedBattle.mobCode == mobCode &&
            mobHp(mobId) == 0 &&
            now - endedBattle.endedAt <= ENDED_BATTLE_START_IGNORE_MS
        ) {
            return
        }
        if (
            currentTarget() == mobId &&
            currentBattleStart() > 0L &&
            currentBattleEnd() == 0L &&
            activeBattleMobCode == mobCode
        ) {
            return
        }
        recentlyEndedBattles.remove(mobId)
        battleRevision.incrementAndGet()
        saveCurrentBattleStart()
        saveCurrentTarget(mobId)
        activeBattleMobCode = mobCode
        // 새 전투 시작마다 본인 전투력을 공식 API로 재검증(캐시 우회 + 덮어쓰기). 세션 중 장비 교체 반영용.
        // 타인은 스냅샷/조인 패킷의 saveUserPower 로 이미 변동 반영되므로 본인만 트리거한다.
        val exec = executorId()
        if (exec > 0) requestOfficialCharacterLookup(exec, forceRefresh = true)
    }

    fun endBattle(mobId: Int) {
        if (currentTarget() != mobId) return
        val mobCode = activeBattleMobCode ?: DataManager.mobId(mobId)
        saveCurrentBattleEnd()
        saveCurrentTarget(-1)
        recentlyEndedBattles[mobId] = EndedBattle(mobCode, System.currentTimeMillis())
        activeBattleMobCode = null
    }

    fun currentBattleStart(): Long {
        return packetRepository.currentBattleStart()
    }

    fun currentBattleEnd(): Long {
        return packetRepository.currentBattleEnd()
    }

    private fun saveCurrentBattleStart() {
        packetRepository.saveCurrentBattleStart()
    }

    private fun saveCurrentBattleEnd(time: Long = System.currentTimeMillis()) {
        packetRepository.saveCurrentBattleEnd(time)
    }

    private fun saveCurrentTarget(targetId: Int) {
        packetRepository.currentTarget(targetId)
    }

    @Synchronized
    fun flushPacket() {
        packetRepository.flush()
        packetRepository.currentTarget(-1)
        packetRepository.flushBattleTime()
        activeBattleMobCode = null
        lastDummyHitTime = 0
    }

    @Synchronized
    fun saveDamage(pdp: ParsedDamagePacket, epoch: Long) {
        if (resetEpoch.get() != epoch) return
        packetRepository.save(pdp)
    }


    /*
    battleLog 영역
     */
    fun saveBattleLog(
        data: DpsReport,
        skillDetails: Map<Int, HashMap<String, AnalyzedSkill>> = emptyMap(),
        buffRates: Map<Int, List<OperatingData>> = emptyMap(),
        bossBuffRates: List<OperatingData> = emptyList()
    ): DpsLog {
        val snapshot = DpsReport(
            contributors = data.contributors.mapTo(mutableSetOf()) { it.copy() },
            battleStart = data.battleStart,
            battleEnd = data.battleEnd,
            information = HashMap(data.information.mapValues { it.value.copy() }),
            target = data.target?.copy(mob = data.target!!.mob.copy()),
            packets = null
        )
        val packets = rawPacketsInRange(data.battleStart - 5000L, data.battleEnd)
        val log = DpsLog(
            snapshot,
            HashMap(summonRepository.getAll()),
            emptyList(),
            skillDetails,
            buffRates,
            bossBuffRates
        )
        battleLogRepository.save(log)
        if (packetLoggingEnabled && packets.isNotEmpty()) {
            val targetName = data.target?.mob?.name ?: data.target?.id?.toString() ?: "battle"
            writeRawPacketLog("battle-$targetName", data.battleStart - 5000L, data.battleEnd, packets)
        }
        dropRawPacketsUntil(data.battleEnd)
        useBuffRepository.pruneBefore(data.battleEnd + 1)
        return log
    }

    fun recentBattleList(): List<Pair<Int, DpsReport>> {
        val battleList: MutableList<Pair<Int, DpsReport>> = mutableListOf()
        val battleLogs = battleLogRepository.getAll()
        battleLogs.forEachIndexed { idx, it ->
            battleList.add(Pair(idx, it.report))
        }
        return battleList
    }

    fun battleLog(idx: Int): DpsLog? {
        return battleLogRepository.get(idx)
    }


    /*
    summon 영역
     */
    fun summonerId(summonId: Int): Int? {
        return summonRepository.get(summonId)
    }

    fun summonMap(): Map<Int, Int> = summonRepository.getAll()

    fun saveSummon(summonId: Int, summonerId: Int) {
        summonRepository.save(summonId, summonerId)
    }


    /*
    mobId 영역
     */
    fun mobId(mobId: Int): Int? {
        return mobIdRepository.get(mobId)?.code
    }

    fun mobMaxHp(mobId: Int): Int? {
        return mobIdRepository.get(mobId)?.maxHp?.takeIf { it > 0 }
    }

    fun saveMobId(mid: Int, code: Int) {
        val previous = DataManager.mobId(mid)
        if (previous != null && previous != code) {
            recentlyEndedBattles.remove(mid)
        }
        mobIdRepository.save(mid, code)
    }

    fun saveMobMaxHp(mid: Int, maxHp: Int) {
        mobIdRepository.saveMaxHp(mid, maxHp)
    }

    private fun existMobId(mobId: Int): Boolean {
        return mobIdRepository.exist(mobId)
    }

    fun isMobInstance(id: Int): Boolean {
        return existMobId(id)
    }


    /*
    mob 영역
     */
    fun mob(mobCode: Int): Mob? {
        return mobRepository.get(mobCode)
    }

    private fun saveMob(mob: Mob) {
        mobRepository.save(mob.code, mob)
    }


    /*
    user 영역
     */
    fun user(uid: Int): User? {
        return userRepository.get(uid)
    }

    fun saveUser(uid: Int, user: User) {
        userRepository.save(uid, user)
    }

    fun saveUser(user: User) {
        userRepository.savePending(user)
    }

    fun rememberUserPower(uid: Int, nickname: String?, server: Int, job: JobClass?, power: Int) {
        userRepository.rememberPower(uid, nickname, server, job, power)
    }

    /**
     * 캐릭터 스냅샷 패킷에서 파싱한 전투력을 해당 uid 의 User 에 직접 반영한다.
     * 닉네임 패킷에서 이미 uid 로 User 가 저장된 직후에 호출되므로 항상 존재한다.
     */
    fun saveUserPower(uid: Int, power: Int) {
        if (power <= 0) return
        val user = userRepository.get(uid) ?: return
        if (user.power != power) {
            user.power = power
            userRepository.save(uid, user)
        }
    }

    fun requestOfficialCharacterLookup(uid: Int, forceRefresh: Boolean = false) {
        val user = user(uid) ?: return
        requestOfficialCharacterLookup(uid, user.nickname, user.server, user.job, forceRefresh = forceRefresh)
    }

    fun requestOfficialCharacterLookup(
        uid: Int,
        nickname: String?,
        server: Int,
        job: JobClass?,
        forceRefresh: Boolean = false,
        onResult: ((OfficialCharacterInfo) -> Unit)? = null
    ) {
        if (nickname.isNullOrBlank() || server <= 0) return
        val now = System.currentTimeMillis()
        // 강제 재검증은 캐시 우회로 호출량이 늘 수 있어 짧은 전용 throttle(본인 전용 경로)로 묶는다.
        val attempts = if (forceRefresh) forceVerifyAttempts else officialLookupAttempts
        val throttleMs = if (forceRefresh) SELF_VERIFY_THROTTLE_MS else 10 * 60 * 1000L
        val previous = attempts[uid]
        if (previous != null && now - previous < throttleMs) return
        if (uid > 0) attempts[uid] = now

        OfficialCharacterLookup.lookupAsync(nickname, server, job, forceRefresh = forceRefresh) { info ->
            applyOfficialCharacterInfo(uid, info, overwrite = forceRefresh)
            onResult?.invoke(info)
        }
    }

    fun resolveOfficialCharacterInfo(
        uid: Int,
        nickname: String?,
        server: Int,
        job: JobClass?
    ): OfficialCharacterInfo? {
        val info = OfficialCharacterLookup.lookupBlocking(nickname, server, job) ?: return null
        applyOfficialCharacterInfo(uid, info)
        return info
    }

    private fun applyOfficialCharacterInfo(uid: Int, info: OfficialCharacterInfo, overwrite: Boolean = false) {
        val existing = if (uid > 0) userRepository.get(uid) else null
        if (existing != null) {
            if (existing.nickname.isNullOrBlank()) existing.nickname = info.nickname
            if (existing.server <= 0) existing.server = info.server
            if (existing.job == null && info.job != null) existing.job = info.job
            if (info.power > 0 && (overwrite || existing.power <= 0)) existing.power = info.power
            userRepository.save(uid, existing)
            return
        }

        userRepository.savePending(
            User(
                id = uid,
                nickname = info.nickname,
                server = info.server,
                job = info.job,
                power = info.power
            )
        )
    }

    fun findUserByNicknameAndServer(nickname: String, server: Int): User? {
        return userRepository.findByNicknameAndServer(nickname, server)
    }

    fun saveNickname(uid: Int, nickname: String, isExecutor: Boolean = false,server:Int, job: JobClass? = null) {
        val user = userRepository.get(uid) ?: User(uid, nickname, server, null, isExecutor).also {
            userRepository.save(uid, it)
        }
        user.nickname = nickname
        if (server > 0) {
            user.server = server
        }
        if (user.job == null && job != null) {
            user.job = job
        }
        userRepository.save(uid, user)
        if (isExecutor) {
            saveExecutorId(uid)
        }
    }

    private fun saveExecutorId(uid: Int) {
        val executor = userRepository.executor()
        if (executor != uid) {
            if (executor != 0) {
                userRepository.get(executor)!!.isExecutor = false
            }
            userRepository.executor(uid)
            userRepository.get(uid)!!.isExecutor = true
        }
    }

    /*
    buff 영역
     */
    fun saveUseBuff(uid: Int, useBuff: UseBuff) {
        useBuffRepository.save(uid, useBuff)
    }

    fun battleBuff(uid:Int,start:Long,end:Long): List<UseBuff> {
        return useBuffRepository.findOverlapping(uid,start,end)
    }

    fun buff(buffCode:Int):Buff?{
        return buffRepository.get(buffCode)
    }

    private fun saveBuff(buff: Buff){
        buffRepository.save(buff)
    }
}

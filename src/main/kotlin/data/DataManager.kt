package com.tbread.data

import com.tbread.data.repository.*
import com.tbread.entity.*
import kotlinx.serialization.json.*
import org.slf4j.LoggerFactory
import java.util.concurrent.ConcurrentHashMap
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
        val log = DpsLog(
            snapshot,
            HashMap(summonRepository.getAll()),
            skillDetails,
            buffRates,
            bossBuffRates
        )
        battleLogRepository.save(log)
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

    fun findUserByNicknameAndServer(nickname: String, server: Int): User? {
        return userRepository.findByNicknameAndServer(nickname, server)
    }

    fun saveNickname(uid: Int, nickname: String, isExecutor: Boolean = false,server:Int) {
        val user = userRepository.get(uid) ?: User(uid, nickname, server, null, isExecutor).also {
            userRepository.save(uid, it)
        }
        user.nickname = nickname
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

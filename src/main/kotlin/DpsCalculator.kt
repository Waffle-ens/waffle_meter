package com.tbread

import com.tbread.entity.DpsData
import com.tbread.entity.JobClass
import com.tbread.entity.PersonalData
import com.tbread.entity.TargetInfo
import kotlinx.coroutines.Job
import org.slf4j.LoggerFactory
import kotlin.math.log
import kotlin.math.roundToInt

class DpsCalculator(private val dataStorage: DataStorage) {
    private val logger = LoggerFactory.getLogger(DpsCalculator::class.java)

    enum class Mode {
        ALL, BOSS_ONLY
    }

    private val POSSIBLE_OFFSETS: IntArray =
        intArrayOf(
            0, 10, 20, 30, 40, 50,
            120, 130, 140, 150,
            230, 240, 250,
            340, 350,
            450,
            1230, 1240, 1250,
            1340, 1350,
            1450,
            2340, 2350,
            2450,
            3450
        )

    private val SKILL_MAP = mapOf(
        39063 to "파괴 충동",
        3528 to "살기파열",
        9952 to "예리한일격",
        19952 to "파열의일격",
        29952 to "분노의일격",
        38880 to "무모한일격",
        65488 to "절단의 맹타",
        16736 to "격파의 맹타",
        48880 to "도약 찍기",
        17808 to "유린의검",
        17809 to "유린의검(행동불가면역대상)",
        28880 to "내려찍기",
        36736 to "올려치기",
        58880 to "발목베기",
        39952 to "분쇄파동",
        49952 to "광폭파동",
        22272 to "돌진일격",
        24418 to "파멸의맹타",
        27808 to "공중결박"
    )

    private val SKILL_CODES: IntArray =
        intArrayOf(
            39063,
            3528,
            9952,
            19952,
            29952,
            38880,
            65488,
            16736,
            48880,
            17808,
            17809,
            26960,
            28880,
            36736,
            58880,
            39952,
            49952,
            22272,
            24418,
            27808,
            60832,
            33872,
            19216,
            53136,
            36176,
            5648,
            16912
        ).apply { sort() }

    private val targetInfoMap = hashMapOf<Int, TargetInfo>()

    private var mode: Mode = Mode.BOSS_ONLY
    private var currentTarget: Int = 0

    fun setMode(mode: Mode) {
        this.mode = mode
        //모드 변경시 이전기록 초기화?
    }

    fun getDps(): DpsData {
        val pdpMap = dataStorage.getBossModeData()

        pdpMap.forEach { (target, data) ->
            var flag = false
            var targetInfo = targetInfoMap[target]
            if (!targetInfoMap.containsKey(target)) {
                flag = true
            }
            data.forEach { pdp ->
                if (flag) {
                    flag = false
                    targetInfo = TargetInfo(target, 0, pdp.getTimeStamp(), pdp.getTimeStamp())
                    targetInfoMap[target] = targetInfo!!
                }
                targetInfo!!.processPdp(pdp)
                //그냥 아래에서 재계산하는거 여기서 해놓고 아래에선 그냥 골라서 주는게 맞는거같은데 나중에 고민할필요있을듯
            }
        }
        val dpsData = DpsData()
        val targetData = decideTarget()
        dpsData.targetName = targetData.second
        val battleTime = targetInfoMap[currentTarget]?.parseBattleTime() ?: 0
        val nicknameData = dataStorage.getNickname()
        var totalDamage = 0.0
        if (battleTime == 0L) {
            return dpsData
        }
        pdpMap[currentTarget]!!.forEach lastPdpLoop@{ pdp ->
            totalDamage += pdp.getDamage()
            val uid = pdp.getActorId()
            val nickname = nicknameData[pdp.getActorId()] ?: nicknameData[dataStorage.getSummonData()[pdp.getActorId()]
                ?: return@lastPdpLoop] ?: return@lastPdpLoop
            if (!dpsData.map.containsKey(uid)) {
                dpsData.map[uid] = PersonalData(nickname = nickname)
            }
            pdp.setSkillCode(inferOriginalSkillCode(pdp.getSkillCode1()) ?: pdp.getSkillCode1())
            dpsData.map[uid]!!.processPdp(pdp)
            if (dpsData.map[uid]!!.job == "") {
                val origSkillCode = inferOriginalSkillCode(pdp.getSkillCode1()) ?: -1
                val job = JobClass.convertFromSkill(origSkillCode)
                if (job != null) {
                    dpsData.map[uid]!!.job = job.className
                }
            }
        }
        dpsData.map.forEach { (_, data) ->
            data.dps = data.amount / battleTime * 1000
            data.damageContribution = data.amount / totalDamage * 100
        }
        return dpsData
    }

    private fun decideTarget(): Pair<Int, String> {
        val target: Int = targetInfoMap.maxByOrNull { it.value.damagedAmount() }?.key ?: 0
        var targetName = ""
        currentTarget = target
        //데미지 누계말고도 건수누적방식도 추가하는게 좋을지도? 지금방식은 정복같은데선 타겟변경에 너무 오랜시간이듬
        if (dataStorage.getMobData().containsKey(target)) {
            val mobCode = dataStorage.getMobData()[target]
            if (dataStorage.getMobCodeData().containsKey(mobCode)) {
                targetName = dataStorage.getMobCodeData()[mobCode]!!
            }
        }

        return Pair(target, targetName)
    }

    private fun inferOriginalSkillCode(skillCode: Int): Int? {
        for (offset in POSSIBLE_OFFSETS) {
            val possibleOrigin = skillCode - offset
            if (SKILL_CODES.binarySearch(possibleOrigin) >= 0) {
                logger.debug("추론 성공한 원본 스킬코드 :{}", possibleOrigin)
                return possibleOrigin
            }
        }
        logger.debug("스킬코드 추론 실패")
        return null
    }

    fun resetDataStorage() {
        val dpsData = getDps()
        dpsData.map.forEach { (_, pData) ->
            logger.info("-----------------------------------------")
            logger.info(
                "닉네임: {} 직업: {} 총 딜량: {} 기여도: {}",
                pData.nickname,
                pData.job,
                pData.amount,
                pData.damageContribution
            )
            pData.analyzedData.forEach { (key, data) ->
                logger.info("스킬(코드): {} 스킬 총 피해량: {}", SKILL_MAP[key] ?: key, data.damageAmount)
                logger.info(
                    "사용 횟수: {} 치명타 횟수: {} 치명타 비율:{}",
                    data.times,
                    data.critTimes,
                    data.critTimes / data.times * 100
                )
                logger.info("스킬의 딜 지분: {}%", (data.damageAmount / pData.amount * 100).roundToInt())
            }
            logger.info("-----------------------------------------")
        }
        dataStorage.flushDamageStorage()
        targetInfoMap.clear()
        logger.info("대상 데미지 누적 데이터 초기화 완료")
    }

}
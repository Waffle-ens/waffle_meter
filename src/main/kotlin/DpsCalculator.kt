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
        27808 to "공중결박",
        //검성

        60832 to "저격",
        5296 to "연사",
        15296 to "나선 화살",
        53152 to "속사",
        //질풍화살 추후 추가
        39760 to "올가미 화살",
        //올가미 2타 추후 추가
        65296 to "표적 화살",
        25296 to "송곳 화살",
        54407 to "사냥꾼의 혼",
        19761 to "광풍 화살 - 1단계",
        19762 to "광풍 화살 - 2단계",
        19763 to "광풍 화살 - Max",
        14225 to "폭발의 덫",
        50832 to "조준 화살",
        50833 to "조준 화살 - 1단계",
        50834 to "조준 화살 - 2단계",
        50835 to "조준 화살 - Max",
        55296 to "파열 화살",
        24407 to "속박의 눈",
        45296 to "제압 화살",
        39943 to "집중 포화",
        34408 to "근접 사격",
        //궁성

        37447 to "각오",
        33872 to "빠른 베기",
        53872 to "절혼 베기",
        63872 to "쾌속 베기",
        22985 to "빈틈 노리기",
        58336 to "맹수의 포효",
        2800 to "맹수의 뒷발차기",
        12800 to "맹수의 후려치기",
        28336 to "암습",
        18336 to "기습",
        8336 to "섬광 베기",
        22800 to "문양 폭발",
        46192 to "심장 찌르기",
        //회오리베기 추후 추가
        56192 to "침투",
        //침투2타 추후추가
        47264 to "그림자 낙하", //추정
        47265 to "그림자 낙하(행동불가면역대상)"
        //기습자세 추후 추가
        //살성

    )

    private val SKILL_CODES: IntArray =
        intArrayOf(
            37447,
            53872,
            63872,
            22985,
            58336,
            2800,
            12800,
            28336,
            18336,
            8336,
            22800,
            46192,
            56192,
            47264,
            47265,
            15296,
            53152,
            39760,
            65296,
            25296,
            54407,
            19761,
            19762,
            19763,
            14225,
            50832,
            50833,
            50834,
            50835,
            55296,
            24407,
            45296,
            39943,
            34408,
            5296,
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
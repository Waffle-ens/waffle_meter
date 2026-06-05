package com.tbread.entity

import kotlinx.serialization.Serializable

@Serializable
data class DpsLog(
    val report: DpsReport,
    val summonMap: Map<Int, Int>,
    val skillDetails: Map<Int, HashMap<String, AnalyzedSkill>> = emptyMap(),
    val buffRates: Map<Int, List<OperatingData>> = emptyMap(),
    val bossBuffRates: List<OperatingData> = emptyList()
)

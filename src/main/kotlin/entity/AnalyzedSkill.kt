package com.tbread.entity

import kotlinx.serialization.Serializable

@Serializable
data class AnalyzedSkill(
    val skillCode: Int,
    var damageAmount: Int = 0,
    var critTimes: Int = 0,
    var times: Int = 0
) {
}
package com.tbread.entity

data class AnalyzedSkill(
    val skillCode: Int,
    var damageAmount: Int = 0,
    var critTimes: Int = 0,
    var times: Int = 0
) {
}
package com.tbread.entity

import kotlinx.serialization.Serializable

@Serializable
data class OperatingData(val buff:Buff?,val operatingRate:Double) {
}
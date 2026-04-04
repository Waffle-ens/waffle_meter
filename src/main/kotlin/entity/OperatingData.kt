package com.tbread.entity

import kotlinx.serialization.Serializable

@Serializable
data class OperatingData(val code:Int,val name:String,val summary:String?,val effect:String?,val operatingRate:Double) {
    constructor(code:Int,buff:Buff?,rate:Double):this(
        code,buff?.name?:code.toString(),buff?.summary,buff?.effect,rate
    )
}
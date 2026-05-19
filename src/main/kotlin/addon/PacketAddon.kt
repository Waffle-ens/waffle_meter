package com.tbread.addon

import com.tbread.entity.JoinRequestUser

interface PacketAddon {
    fun parse(packet: ByteArray, arrivedAt: Long)
    fun loggingServerTime(arrivedAt: Long, duration: Long, serverTime: Long)
    fun processingUser(joinRequestUser: JoinRequestUser): JoinRequestUser
    fun parsingMobSpawnAddon(packet:ByteArray,offset:Int,mid:Int,mobCode:Int)
}

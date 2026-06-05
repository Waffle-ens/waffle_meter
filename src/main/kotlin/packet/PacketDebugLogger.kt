package com.tbread.packet

import com.tbread.entity.ParsedDamagePacket

object PacketDebugLogger {
    fun capture(ip: String, seq: Long, data: ByteArray, arrivedAt: Long) = Unit

    fun assembled(packet: ByteArray, arrivedAt: Long) = Unit

    fun dispatch(opcodeKey: Int, opcodeName: String?, extraFlag: Boolean, size: Int, arrivedAt: Long) = Unit

    fun unknownOpcode(opcodeKey: Int, extraFlag: Boolean, size: Int, arrivedAt: Long, head: ByteArray) = Unit

    fun parserError(type: String, reason: String, packet: ByteArray? = null, offset: Int? = null) = Unit

    fun damage(kind: String, packet: ParsedDamagePacket, saved: Boolean, reason: String? = null, mobCode: Int? = null) = Unit

    fun battle(target: Int, toggle: Int, mobCode: Int?, mobName: String?, accepted: Boolean, reason: String?) = Unit

    fun meta(type: String, fields: Map<String, Any?>) = Unit
}

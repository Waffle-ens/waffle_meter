package com.tbread.data.repository

import com.tbread.entity.ParsedDamagePacket
import java.util.concurrent.CopyOnWriteArrayList

class BattleLogRepository {
    private val storage = mutableListOf<CopyOnWriteArrayList<ParsedDamagePacket>>()

    fun save(data: CopyOnWriteArrayList<ParsedDamagePacket>) {
        storage.add(data)
    }

    fun get(idx: Int): CopyOnWriteArrayList<ParsedDamagePacket>? {
        return storage[idx]
    }
}
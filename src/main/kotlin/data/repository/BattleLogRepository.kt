package com.tbread.data.repository

import com.tbread.entity.DpsReport

class BattleLogRepository {
    private val maxSize = 20
    private val storage = ArrayDeque<DpsReport>()

    fun save(data: DpsReport) {
        if (storage.size >= maxSize) {
            storage.removeFirst()
        }
        storage.addLast(data)
    }

    fun get(idx: Int): DpsReport? {
        return storage.elementAtOrNull(idx)
    }

    fun getAll(): List<DpsReport> {
        return storage.toList()
    }

    fun flush(){
        storage.clear()
    }
}
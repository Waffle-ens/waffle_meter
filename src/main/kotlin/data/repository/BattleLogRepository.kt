package com.tbread.data.repository

import com.tbread.entity.DpsLog

class BattleLogRepository {
    companion object {
        private const val SAME_BATTLE_MERGE_WINDOW_MS = 120_000L
        private const val IDLE_EXTENSION_GRACE_MS = 30_000L
    }

    private val maxSize = 12
    private val storage = ArrayDeque<DpsLog>()

    fun save(data: DpsLog) {
        val existingIndex = storage.indexOfLast { isSameBattle(it, data) }
        if (existingIndex >= 0) {
            replaceAt(existingIndex, selectPreferred(storage.elementAt(existingIndex), data))
            return
        }

        if (storage.size >= maxSize) {
            storage.removeFirst()
        }
        storage.addLast(data)
    }

    fun get(idx: Int): DpsLog? {
        return storage.elementAtOrNull(idx)
    }

    fun getAll(): List<DpsLog> {
        return storage.toList()
    }

    fun flush(){
        storage.clear()
    }

    private fun isSameBattle(a: DpsLog, b: DpsLog): Boolean {
        val aTarget = a.report.target ?: return false
        val bTarget = b.report.target ?: return false
        if (aTarget.id != bTarget.id) return false
        if (aTarget.mob.code != bTarget.mob.code) return false
        if (a.report.battleStart <= 0L || b.report.battleStart <= 0L) return false

        val aEnd = a.report.battleEnd.takeIf { it >= a.report.battleStart } ?: a.report.battleStart
        val bEnd = b.report.battleEnd.takeIf { it >= b.report.battleStart } ?: b.report.battleStart
        val gap = when {
            aEnd < b.report.battleStart -> b.report.battleStart - aEnd
            bEnd < a.report.battleStart -> a.report.battleStart - bEnd
            else -> 0L
        }

        return gap <= SAME_BATTLE_MERGE_WINDOW_MS
    }

    private fun selectPreferred(existing: DpsLog, next: DpsLog): DpsLog {
        val existingDamage = totalDamage(existing)
        val nextDamage = totalDamage(next)
        val existingDuration = duration(existing)
        val nextDuration = duration(next)

        if (
            nextDamage <= existingDamage * 1.01 &&
            nextDuration > existingDuration + IDLE_EXTENSION_GRACE_MS
        ) {
            return existing
        }

        return if (nextDamage + 0.001 >= existingDamage) next else existing
    }

    private fun totalDamage(log: DpsLog): Double {
        return log.report.information.values.sumOf { it.amount }
    }

    private fun duration(log: DpsLog): Long {
        return (log.report.battleEnd - log.report.battleStart).coerceAtLeast(0L)
    }

    private fun replaceAt(index: Int, data: DpsLog) {
        val items = storage.toMutableList()
        items[index] = data
        storage.clear()
        items.forEach(storage::addLast)
    }
}

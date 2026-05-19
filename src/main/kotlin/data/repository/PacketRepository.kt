package com.tbread.data.repository

import com.tbread.entity.ParsedDamagePacket
import java.util.concurrent.ConcurrentHashMap

class PacketRepository(private val maxPacketsPerTarget: Int = MAX_PACKETS_PER_TARGET) {
    companion object {
        const val MAX_PACKETS_PER_TARGET = 150_000
        private const val INITIAL_BUFFER_CAPACITY = 1_024
    }

    data class PacketWindow(
        val packets: List<ParsedDamagePacket>,
        val nextSequence: Long,
        val droppedBeforeStart: Boolean,
        val totalSize: Int
    )

    private class PacketRingBuffer(private val maxCapacity: Int) {
        private var buffer = arrayOfNulls<ParsedDamagePacket>(minOf(INITIAL_BUFFER_CAPACITY, maxCapacity))
        private var start = 0
        private var size = 0
        private var totalAdded = 0L

        @Synchronized
        fun add(packet: ParsedDamagePacket) {
            ensureCapacityForAppend()

            if (size < buffer.size) {
                buffer[(start + size) % buffer.size] = packet
                size++
            } else {
                buffer[start] = packet
                start = (start + 1) % buffer.size
            }

            totalAdded++
        }

        @Synchronized
        fun snapshot(): MutableList<ParsedDamagePacket> {
            val result = ArrayList<ParsedDamagePacket>(size)
            for (i in 0 until size) {
                result.add(elementAtOffset(i))
            }
            return result
        }

        @Synchronized
        fun windowFrom(sequence: Long): PacketWindow {
            val firstSequence = totalAdded - size
            val safeSequence = maxOf(sequence, firstSequence).coerceAtMost(totalAdded)
            val count = (totalAdded - safeSequence).toInt()
            val result = ArrayList<ParsedDamagePacket>(count)
            val firstOffset = (safeSequence - firstSequence).toInt()

            for (i in 0 until count) {
                result.add(elementAtOffset(firstOffset + i))
            }

            return PacketWindow(
                packets = result,
                nextSequence = totalAdded,
                droppedBeforeStart = sequence < firstSequence,
                totalSize = size
            )
        }

        private fun ensureCapacityForAppend() {
            if (size < buffer.size || buffer.size >= maxCapacity) return

            val newCapacity = minOf(buffer.size * 2, maxCapacity)
            val newBuffer = arrayOfNulls<ParsedDamagePacket>(newCapacity)
            for (i in 0 until size) {
                newBuffer[i] = elementAtOffset(i)
            }
            buffer = newBuffer
            start = 0
        }

        private fun elementAtOffset(offset: Int): ParsedDamagePacket {
            return buffer[(start + offset) % buffer.size]!!
        }
    }

    private val storage = ConcurrentHashMap<Int, PacketRingBuffer>()
    @Volatile
    private var currentTarget = 0
    @Volatile
    private var currentBattleStart = 0L
    @Volatile
    private var currentBattleEnd = 0L

    fun save(pdp: ParsedDamagePacket) {
        storage.computeIfAbsent(pdp.getTargetId()) { PacketRingBuffer(maxPacketsPerTarget) }
            .add(pdp)
    }

    fun get(id: Int): MutableList<ParsedDamagePacket>? {
        return storage[id]?.snapshot()
    }

    fun getWindow(id: Int, sequence: Long): PacketWindow {
        return storage[id]?.windowFrom(sequence)
            ?: PacketWindow(emptyList(), sequence, droppedBeforeStart = false, totalSize = 0)
    }

    fun getAll(): Map<Int, MutableList<ParsedDamagePacket>> {
        return storage.mapValues { it.value.snapshot() }
    }

    fun exist(id: Int): Boolean {
        return storage.containsKey(id)
    }

    fun flush() {
        currentTarget = 0
        currentBattleStart = 0
        currentBattleEnd = 0
        storage.clear()
    }

    fun currentTarget(): Int {
        return currentTarget
    }

    fun currentTarget(targetId: Int): Int {
        val pastTarget = currentTarget
        currentTarget = targetId
        return pastTarget
    }

    fun flushBattleTime() {
        currentBattleStart = 0
        currentBattleEnd = 0
    }

    fun currentBattleStart(): Long {
        return currentBattleStart
    }

    fun currentBattleEnd(): Long {
        return currentBattleEnd
    }

    fun saveCurrentBattleStart() {
        currentBattleStart = System.currentTimeMillis()
    }

    fun saveCurrentBattleEnd(time: Long = System.currentTimeMillis()) {
        currentBattleEnd = time
    }
}

package com.tbread.repository

class MobIdRepository {
    private val storage = HashMap<Int, Int>()

    fun save(key: Int, value: Int): Int? {
        return storage.put(key, value)
    }

    fun get(id: Int): Int? {
        return storage[id]
    }

    fun exist(id: Int): Boolean {
        return storage.containsKey(id)
    }

    fun flush() {
        storage.clear()
    }
}
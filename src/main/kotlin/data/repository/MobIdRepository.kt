package com.tbread.data.repository

data class MobInstance(val code: Int, var maxHp: Int = 0)

class MobIdRepository {
    private val storage = HashMap<Int, MobInstance>()

    fun save(key: Int, code: Int): MobInstance? {
        val existing = storage[key]
        val maxHp = if (existing?.code == code) existing.maxHp else 0
        return storage.put(key, MobInstance(code, maxHp))
    }

    fun saveMaxHp(key: Int, maxHp: Int): Boolean {
        val instance = storage[key] ?: return false
        if (maxHp > instance.maxHp) {
            instance.maxHp = maxHp
        }
        return true
    }

    fun get(id: Int): MobInstance? {
        return storage[id]
    }

    fun exist(id: Int): Boolean {
        return storage.containsKey(id)
    }

    fun delete(id: Int) {
        storage.remove(id)
    }

    fun flush() {
        storage.clear()
    }
}

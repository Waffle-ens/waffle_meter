package com.tbread.repository

import com.tbread.entity.Mob

class MobRepository {
    private val storage = HashMap<Int, Mob>()

    fun save(key: Int, value: Mob): Mob? {
        return storage.put(key, value)
    }

    fun get(id: Int): Mob? {
        return storage[id]
    }

    fun exist(id: Int): Boolean {
        return storage.containsKey(id)
    }
}
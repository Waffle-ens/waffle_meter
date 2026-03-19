package com.tbread.repository

import com.tbread.entity.User
import java.util.concurrent.ConcurrentHashMap

class UserRepository {
    private val storage = ConcurrentHashMap<Int, User>()
    private var executor: Int = 0

    fun save(key: Int, value: User): User? {
        return storage.put(key, value)
    }

    fun get(id: Int): User? {
        return storage[id]
    }

    fun exist(id: Int): Boolean {
        return storage.containsKey(id)
    }

    fun flush() {
        storage.clear()
    }

    fun executor(): Int {
        return executor
    }

    fun executor(id: Int): Int {
        val pastExecutor = executor
        executor = id
        return pastExecutor
    }
}
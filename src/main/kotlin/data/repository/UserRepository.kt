package com.tbread.data.repository

import com.tbread.entity.User
import java.util.concurrent.ConcurrentHashMap

class UserRepository {
    private val storage = ConcurrentHashMap<Int, User>()
    private var executor: Int = 0
    private val subStorage = mutableSetOf<User>()

    fun save(key: Int, value: User): User? {
        val pendingUser = subStorage.find { it.nickname.equals(value.nickname) && it.server == value.server }
        if (pendingUser != null) {
            value.power = pendingUser.power
        }
        subStorage.remove(pendingUser)
        return storage.put(key, value)
    }

    fun savePending(user:User){
        subStorage.add(user)
    }

    fun removePending(user:User){
        subStorage.remove(user)
    }

    fun get(id: Int): User? {
        return storage[id]
    }

    fun exist(id: Int): Boolean {
        return storage.containsKey(id)
    }

    fun findByNicknameAndServer(nickname: String, server: Int): User? {
        return storage.values.find { it.nickname.equals(nickname) && it.server == server }
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
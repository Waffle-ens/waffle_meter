package com.tbread.data.repository

import com.tbread.entity.User
import java.util.concurrent.ConcurrentHashMap

class UserRepository {
    private val storage = ConcurrentHashMap<Int, User>()

    // nickname:server 를 키로 사용 — find+remove 를 ConcurrentHashMap.remove() 한 번으로 원자적 처리
    private val subStorage = ConcurrentHashMap<String, User>()

    @Volatile
    private var executor: Int = 0

    fun save(key: Int, value: User): User? {
        val pendingUser = subStorage.remove("${value.nickname}:${value.server}")
        if (pendingUser != null) {
            value.power = pendingUser.power
        }
        return storage.put(key, value)
    }

    fun savePending(user: User) {
        subStorage["${user.nickname}:${user.server}"] = user
    }

    fun removePending(user: User) {
        subStorage.remove("${user.nickname}:${user.server}")
    }

    fun get(id: Int): User? {
        return storage[id]
    }

    fun exist(id: Int): Boolean {
        return storage.containsKey(id)
    }

    fun findByNicknameAndServer(nickname: String, server: Int): User? {
        return storage.values.find { it.nickname == nickname && it.server == server }
    }

    fun flush() {
        storage.clear()
        subStorage.clear()
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
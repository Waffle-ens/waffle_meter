package com.tbread.data.repository

import com.tbread.entity.User
import com.tbread.entity.enums.JobClass
import java.util.concurrent.ConcurrentHashMap

class UserRepository {
    private val storage = ConcurrentHashMap<Int, User>()

    // character identity can arrive in a different packet from combat power.
    private val pendingByNameServer = ConcurrentHashMap<String, User>()
    private val pendingById = ConcurrentHashMap<Int, User>()
    private val pendingByNickname = ConcurrentHashMap<String, User>()

    @Volatile
    private var executor: Int = 0

    fun save(key: Int, value: User): User? {
        val previous = storage[key]
        val target = previous ?: value

        if (previous != null && previous !== value) {
            mergeInto(previous, value)
        }

        removePendingById(key)?.let { mergeInto(target, it) }
        removePendingByName(target.nickname, target.server)?.let { mergeInto(target, it) }

        storage[key] = target
        return previous
    }

    fun savePending(user: User) {
        if (user.id > 0) {
            storage[user.id]?.let {
                mergeInto(it, user)
                return
            }
            pendingById[user.id] = user
        }

        val nickname = normalizedNickname(user.nickname) ?: return
        if (user.server > 0) {
            pendingByNameServer[nameServerKey(nickname, user.server)] = user
        }
        pendingByNickname[nickname] = user
    }

    fun removePending(user: User) {
        if (user.id > 0) {
            pendingById.remove(user.id)
        }
        val nickname = normalizedNickname(user.nickname) ?: return
        if (user.server > 0) {
            pendingByNameServer.remove(nameServerKey(nickname, user.server))
        }
        pendingByNickname.remove(nickname)
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

    fun rememberPower(id: Int, nickname: String?, server: Int, job: JobClass?, power: Int) {
        if (power <= 0) return
        val user = User(id, nickname, server, job, power = power)
        savePending(user)
    }

    fun flush() {
        storage.clear()
        pendingByNameServer.clear()
        pendingById.clear()
        pendingByNickname.clear()
    }

    fun executor(): Int {
        return executor
    }

    fun executor(id: Int): Int {
        val pastExecutor = executor
        executor = id
        return pastExecutor
    }

    private fun mergeInto(target: User, source: User) {
        if (target.nickname.isNullOrBlank() && !source.nickname.isNullOrBlank()) {
            target.nickname = source.nickname
        }
        if (target.server <= 0 && source.server > 0) {
            target.server = source.server
        }
        if (target.job == null && source.job != null) {
            target.job = source.job
        }
        if (!target.isExecutor && source.isExecutor) {
            target.isExecutor = true
        }
        if (source.power > 0) {
            target.power = source.power
        }
    }

    private fun removePendingById(id: Int): User? {
        if (id <= 0) return null
        return pendingById.remove(id)
    }

    private fun removePendingByName(nickname: String?, server: Int): User? {
        val normalized = normalizedNickname(nickname) ?: return null

        if (server > 0) {
            pendingByNameServer.remove(nameServerKey(normalized, server))?.let {
                removeNicknameReference(normalized, it)
                return it
            }
        }

        val nicknameMatch = pendingByNickname[normalized]
        val sameNameEntries = pendingByNameServer.entries
            .filter { normalizedNickname(it.value.nickname) == normalized }

        val candidates = buildList {
            nicknameMatch?.let(::add)
            sameNameEntries.forEach { add(it.value) }
        }.distinctBy { "${it.id}:${it.server}:${it.power}" }

        if (candidates.size != 1) return null

        val selected = candidates.single()
        pendingByNickname.remove(normalized, selected)
        sameNameEntries
            .filter { it.value === selected || it.value.power == selected.power && it.value.id == selected.id }
            .forEach { pendingByNameServer.remove(it.key, it.value) }
        return selected
    }

    private fun removeNicknameReference(nickname: String, user: User) {
        pendingByNickname.remove(nickname, user)
    }

    private fun normalizedNickname(nickname: String?): String? {
        return nickname?.trim()?.takeIf { it.isNotEmpty() }
    }

    private fun nameServerKey(nickname: String, server: Int): String {
        return "$nickname:$server"
    }
}

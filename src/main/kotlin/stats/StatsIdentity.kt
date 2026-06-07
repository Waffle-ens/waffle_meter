package com.tbread.stats

import java.security.MessageDigest

object StatsIdentity {
    const val IDENTITY_HASH_VERSION = "sha256:aion2-character:v1"

    fun characterIdentityHash(server: Int, nickname: String?): String? {
        if (server <= 0) return null
        val normalizedName = nickname?.trim()?.lowercase().orEmpty()
        if (normalizedName.isBlank()) return null
        return sha256("$IDENTITY_HASH_VERSION|$server|$normalizedName")
    }

    fun sha256(raw: String): String {
        val digest = MessageDigest.getInstance("SHA-256").digest(raw.toByteArray(Charsets.UTF_8))
        return digest.joinToString("") { "%02x".format(it) }
    }
}

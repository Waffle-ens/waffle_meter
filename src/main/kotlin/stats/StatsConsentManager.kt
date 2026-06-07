package com.tbread.stats

import com.tbread.config.PropertyHandler
import com.tbread.data.DataManager
import kotlinx.serialization.Serializable
import org.slf4j.LoggerFactory
import java.time.Instant

object StatsConsentManager {
    const val CONSENT_VERSION = "2026-06-04"

    private const val KEY_STATE = "statsConsentState"
    private const val KEY_UPLOAD_ENABLED = "statsUploadEnabled"
    private const val KEY_PUBLIC_CHARACTER = "statsPublicCharacter"
    private const val KEY_CONSENT_VERSION = "statsConsentVersion"
    private const val KEY_UPDATED_AT = "statsConsentUpdatedAt"
    private const val KEY_IDENTITY_HASH = "statsConsentIdentityHash"
    private const val KEY_REMOTE_EXISTS = "statsConsentRemoteExists"
    private const val KEY_SYNC_STATUS = "statsConsentSyncStatus"
    private const val KEY_SYNC_ERROR = "statsConsentSyncError"
    private const val KEY_SERVER_UPDATED_AT = "statsConsentServerUpdatedAt"
    private const val KEY_LAST_SEEN_AT = "statsConsentLastSeenAt"

    private val logger = LoggerFactory.getLogger(StatsConsentManager::class.java)

    enum class State {
        unknown,
        accepted,
        declined,
        revoked
    }

    @Serializable
    data class Info(
        val state: String,
        val uploadEnabled: Boolean,
        val publicCharacter: Boolean,
        val consentVersion: String,
        val updatedAt: Long,
        val identityHash: String? = null,
        val remoteExists: Boolean = false,
        val syncStatus: String = "local",
        val syncError: String? = null,
        val serverUpdatedAt: String? = null,
        val lastSeenAt: String? = null
    )

    fun info(syncRemote: Boolean = false, clientVersion: String = "dev"): Info {
        return if (syncRemote) refreshFromServer(clientVersion) else localInfo()
    }

    fun set(
        state: String,
        uploadEnabled: Boolean,
        publicCharacter: Boolean,
        clientVersion: String = "dev"
    ): Info {
        val nextState = runCatching { State.valueOf(state) }.getOrDefault(State.unknown)
        return when (nextState) {
            State.accepted -> accept(uploadEnabled, publicCharacter, clientVersion)
            State.revoked -> revoke(clientVersion)
            State.declined -> {
                saveLocal(
                    state = State.declined,
                    uploadEnabled = false,
                    publicCharacter = false,
                    syncStatus = "local_declined"
                )
                localInfo()
            }
            State.unknown -> {
                saveLocal(
                    state = State.unknown,
                    uploadEnabled = false,
                    publicCharacter = false,
                    syncStatus = "local_unknown"
                )
                localInfo()
            }
        }
    }

    fun isUploadAllowed(): Boolean {
        val current = localInfo()
        return current.state == State.accepted.name && current.uploadEnabled
    }

    private fun refreshFromServer(clientVersion: String): Info {
        val identityHash = currentIdentityHash()
        if (identityHash == null) {
            rememberSync("identity_missing", null)
            return localInfo()
        }

        return try {
            val response = StatsApiClient.getConsentStatus(identityHash)
            val current = localInfo()
            val requestedUpload = if (current.state == State.accepted.name) current.uploadEnabled else true
            applyRemote(response, requestedUpload = requestedUpload)
        } catch (e: Exception) {
            val reason = e.message?.take(160)?.ifBlank { null } ?: e.javaClass.simpleName
            logger.warn("통계 동의 상태 조회 실패: identityHash={}", identityHash, e)
            rememberSync("sync_failed", reason)
            localInfo()
        }
    }

    private fun accept(uploadEnabled: Boolean, publicCharacter: Boolean, clientVersion: String): Info {
        val character = currentConsentCharacter(publicCharacter)
        if (character == null) {
            saveLocal(
                state = State.accepted,
                uploadEnabled = false,
                publicCharacter = publicCharacter,
                syncStatus = "identity_missing"
            )
            return localInfo()
        }

        return try {
            val response = StatsApiClient.postConsentEvent(
                request = ConsentEventRequest(
                    consentState = State.accepted.name,
                    consentVersion = CONSENT_VERSION,
                    character = character
                ),
                clientVersion = clientVersion
            )
            applyRemote(response, requestedUpload = uploadEnabled)
        } catch (e: Exception) {
            val reason = e.message?.take(160)?.ifBlank { null } ?: e.javaClass.simpleName
            logger.warn("통계 동의 이벤트 저장 실패: identityHash={}", character.identityHash, e)
            saveLocal(
                state = State.accepted,
                uploadEnabled = false,
                publicCharacter = publicCharacter,
                identityHash = character.identityHash,
                syncStatus = "sync_failed",
                syncError = reason
            )
            localInfo()
        }
    }

    private fun revoke(clientVersion: String): Info {
        val identityHash = currentIdentityHash()
            ?: PropertyHandler.getProperty(KEY_IDENTITY_HASH)?.takeIf { it.isNotBlank() }
        if (identityHash == null) {
            saveLocal(
                state = State.revoked,
                uploadEnabled = false,
                publicCharacter = false,
                syncStatus = "identity_missing"
            )
            return localInfo()
        }

        return try {
            val response = StatsApiClient.postConsentEvent(
                request = ConsentEventRequest(
                    identityHash = identityHash,
                    consentState = State.revoked.name,
                    consentVersion = CONSENT_VERSION
                ),
                clientVersion = clientVersion
            )
            applyRemote(response, requestedUpload = false)
        } catch (e: Exception) {
            val reason = e.message?.take(160)?.ifBlank { null } ?: e.javaClass.simpleName
            logger.warn("통계 동의 철회 이벤트 저장 실패: identityHash={}", identityHash, e)
            saveLocal(
                state = State.revoked,
                uploadEnabled = false,
                publicCharacter = false,
                identityHash = identityHash,
                syncStatus = "sync_failed",
                syncError = reason
            )
            localInfo()
        }
    }

    private fun applyRemote(response: ConsentStatusResponse, requestedUpload: Boolean): Info {
        val remoteState = runCatching { State.valueOf(response.consentState) }.getOrDefault(State.unknown)
        val accepted = remoteState == State.accepted && response.exists
        val publicCharacter = accepted && response.publicCharacter
        saveLocal(
            state = remoteState,
            uploadEnabled = accepted && requestedUpload,
            publicCharacter = publicCharacter,
            consentVersion = response.consentVersion ?: CONSENT_VERSION,
            updatedAt = parseRemoteTime(response.updatedAt) ?: System.currentTimeMillis(),
            identityHash = response.identityHash,
            remoteExists = response.exists,
            syncStatus = "synced",
            syncError = null,
            serverUpdatedAt = response.updatedAt,
            lastSeenAt = response.lastSeenAt
        )
        return localInfo()
    }

    private fun currentConsentCharacter(publicCharacter: Boolean): ConsentEventCharacter? {
        val user = DataManager.user(DataManager.executorId()) ?: return null
        val nickname = user.nickname?.takeIf { it.isNotBlank() } ?: return null
        val identityHash = StatsIdentity.characterIdentityHash(user.server, nickname) ?: return null
        val resolved = StatsPayloadBuilder.ownCharacter()
        return ConsentEventCharacter(
            identityHash = identityHash,
            nickname = nickname,
            server = user.server,
            job = resolved.job ?: user.job?.className,
            power = resolved.power,
            publicCharacter = publicCharacter
        )
    }

    private fun currentIdentityHash(): String? {
        val user = DataManager.user(DataManager.executorId()) ?: return null
        return StatsIdentity.characterIdentityHash(user.server, user.nickname)
    }

    private fun localInfo(): Info {
        val state = runCatching {
            State.valueOf(PropertyHandler.getProperty(KEY_STATE) ?: State.unknown.name)
        }.getOrDefault(State.unknown)
        val uploadEnabled = PropertyHandler.getProperty(KEY_UPLOAD_ENABLED)
            ?.toBooleanStrictOrNull()
            ?: false
        val publicCharacter = PropertyHandler.getProperty(KEY_PUBLIC_CHARACTER)
            ?.toBooleanStrictOrNull()
            ?: false
        val consentVersion = PropertyHandler.getProperty(KEY_CONSENT_VERSION) ?: CONSENT_VERSION
        val updatedAt = PropertyHandler.getProperty(KEY_UPDATED_AT)?.toLongOrNull() ?: 0L
        val identityHash = PropertyHandler.getProperty(KEY_IDENTITY_HASH)?.takeIf { it.isNotBlank() }
        val remoteExists = PropertyHandler.getProperty(KEY_REMOTE_EXISTS)
            ?.toBooleanStrictOrNull()
            ?: false
        val syncStatus = PropertyHandler.getProperty(KEY_SYNC_STATUS) ?: "local"
        val syncError = PropertyHandler.getProperty(KEY_SYNC_ERROR)?.takeIf { it.isNotBlank() }
        val serverUpdatedAt = PropertyHandler.getProperty(KEY_SERVER_UPDATED_AT)?.takeIf { it.isNotBlank() }
        val lastSeenAt = PropertyHandler.getProperty(KEY_LAST_SEEN_AT)?.takeIf { it.isNotBlank() }
        return Info(
            state = state.name,
            uploadEnabled = state == State.accepted && uploadEnabled,
            publicCharacter = state == State.accepted && publicCharacter,
            consentVersion = consentVersion,
            updatedAt = updatedAt,
            identityHash = identityHash,
            remoteExists = remoteExists,
            syncStatus = syncStatus,
            syncError = syncError,
            serverUpdatedAt = serverUpdatedAt,
            lastSeenAt = lastSeenAt
        )
    }

    private fun saveLocal(
        state: State,
        uploadEnabled: Boolean,
        publicCharacter: Boolean,
        consentVersion: String = CONSENT_VERSION,
        updatedAt: Long = System.currentTimeMillis(),
        identityHash: String? = currentIdentityHash(),
        remoteExists: Boolean = false,
        syncStatus: String,
        syncError: String? = null,
        serverUpdatedAt: String? = null,
        lastSeenAt: String? = null
    ) {
        PropertyHandler.setProperty(KEY_STATE, state.name)
        PropertyHandler.setProperty(KEY_UPLOAD_ENABLED, (state == State.accepted && uploadEnabled).toString())
        PropertyHandler.setProperty(KEY_PUBLIC_CHARACTER, (state == State.accepted && publicCharacter).toString())
        PropertyHandler.setProperty(KEY_CONSENT_VERSION, consentVersion)
        PropertyHandler.setProperty(KEY_UPDATED_AT, updatedAt.toString())
        PropertyHandler.setProperty(KEY_IDENTITY_HASH, identityHash.orEmpty())
        PropertyHandler.setProperty(KEY_REMOTE_EXISTS, remoteExists.toString())
        PropertyHandler.setProperty(KEY_SYNC_STATUS, syncStatus)
        PropertyHandler.setProperty(KEY_SYNC_ERROR, syncError.orEmpty())
        PropertyHandler.setProperty(KEY_SERVER_UPDATED_AT, serverUpdatedAt.orEmpty())
        PropertyHandler.setProperty(KEY_LAST_SEEN_AT, lastSeenAt.orEmpty())
    }

    private fun rememberSync(status: String, error: String?) {
        PropertyHandler.setProperty(KEY_SYNC_STATUS, status)
        PropertyHandler.setProperty(KEY_SYNC_ERROR, error.orEmpty())
    }

    private fun parseRemoteTime(value: String?): Long? {
        return value?.let { runCatching { Instant.parse(it).toEpochMilli() }.getOrNull() }
    }
}

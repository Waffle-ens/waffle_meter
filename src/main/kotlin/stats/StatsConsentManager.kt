package com.tbread.stats

import com.tbread.config.PropertyHandler
import kotlinx.serialization.Serializable

object StatsConsentManager {
    const val CONSENT_VERSION = "2026-06-04"

    private const val KEY_STATE = "statsConsentState"
    private const val KEY_UPLOAD_ENABLED = "statsUploadEnabled"
    private const val KEY_PUBLIC_CHARACTER = "statsPublicCharacter"
    private const val KEY_CONSENT_VERSION = "statsConsentVersion"
    private const val KEY_UPDATED_AT = "statsConsentUpdatedAt"

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
        val updatedAt: Long
    )

    fun info(): Info {
        val state = runCatching {
            State.valueOf(PropertyHandler.getProperty(KEY_STATE) ?: State.unknown.name)
        }.getOrDefault(State.unknown)
        val uploadEnabled = PropertyHandler.getProperty(KEY_UPLOAD_ENABLED)
            ?.toBooleanStrictOrNull()
            ?: false
        val publicCharacter = PropertyHandler.getProperty(KEY_PUBLIC_CHARACTER)
            ?.toBooleanStrictOrNull()
            ?: true
        val consentVersion = PropertyHandler.getProperty(KEY_CONSENT_VERSION) ?: CONSENT_VERSION
        val updatedAt = PropertyHandler.getProperty(KEY_UPDATED_AT)?.toLongOrNull() ?: 0L
        return Info(state.name, uploadEnabled, publicCharacter, consentVersion, updatedAt)
    }

    fun set(
        state: String,
        uploadEnabled: Boolean,
        publicCharacter: Boolean
    ): Info {
        val nextState = runCatching { State.valueOf(state) }.getOrDefault(State.unknown)
        val effectiveUpload = nextState == State.accepted && uploadEnabled
        PropertyHandler.setProperty(KEY_STATE, nextState.name)
        PropertyHandler.setProperty(KEY_UPLOAD_ENABLED, effectiveUpload.toString())
        PropertyHandler.setProperty(KEY_PUBLIC_CHARACTER, publicCharacter.toString())
        PropertyHandler.setProperty(KEY_CONSENT_VERSION, CONSENT_VERSION)
        PropertyHandler.setProperty(KEY_UPDATED_AT, System.currentTimeMillis().toString())
        return info()
    }

    fun isUploadAllowed(): Boolean {
        val current = info()
        return current.state == State.accepted.name && current.uploadEnabled
    }
}

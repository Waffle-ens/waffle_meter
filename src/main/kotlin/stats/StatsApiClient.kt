package com.tbread.stats

import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import java.net.HttpURLConnection
import java.net.URI
import java.net.URLEncoder

object StatsApiClient {
    // 와터기.kr (퓨니코드). 백엔드는 HTTPS 전용(http→https 301) + 도메인 인증서라 http://IP 로는
    // Java HttpURLConnection 이 cross-protocol 리다이렉트를 안 따라가 sync_failed → 업로드 전체가 막힘.
    private const val BASE_URL = "https://xn--ok0b896b9wh.kr"
    private const val REPORT_ENDPOINT = "$BASE_URL/api/v1/reports"
    private const val CONSENT_STATUS_ENDPOINT = "$BASE_URL/api/v1/consent/status"
    private const val CONSENT_EVENTS_ENDPOINT = "$BASE_URL/api/v1/consent/events"
    private const val CONNECT_TIMEOUT_MS = 8_000
    private const val READ_TIMEOUT_MS = 15_000

    private val json = Json {
        encodeDefaults = true
        ignoreUnknownKeys = true
        explicitNulls = false
    }

    fun reportEndpoint(): String = REPORT_ENDPOINT

    fun getConsentStatus(identityHash: String): ConsentStatusResponse {
        val encoded = URLEncoder.encode(identityHash, Charsets.UTF_8)
        val response = request(
            method = "GET",
            url = "$CONSENT_STATUS_ENDPOINT?identityHash=$encoded"
        )
        return json.decodeFromString<ConsentStatusResponse>(response.body)
            .also { require(it.ok) { "consent_status_not_ok" } }
    }

    fun postConsentEvent(
        request: ConsentEventRequest,
        clientVersion: String,
        installId: String = StatsInstall.installId(),
        consentVersion: String = StatsConsentManager.CONSENT_VERSION
    ): ConsentStatusResponse {
        val response = request(
            method = "POST",
            url = CONSENT_EVENTS_ENDPOINT,
            body = json.encodeToString(request),
            clientVersion = clientVersion,
            installId = installId,
            consentVersion = consentVersion
        )
        return json.decodeFromString<ConsentStatusResponse>(response.body)
            .also { require(it.ok) { "consent_event_not_ok" } }
    }

    fun postReport(
        payload: StatsUploadPayload,
        clientVersion: String,
        installId: String = StatsInstall.installId()
    ): ReportUploadResponse {
        val response = request(
            method = "POST",
            url = REPORT_ENDPOINT,
            body = json.encodeToString(payload),
            clientVersion = clientVersion,
            installId = installId,
            consentVersion = payload.consentVersion
        )
        return json.decodeFromString<ReportUploadResponse>(response.body)
            .also { require(it.ok) { "report_upload_not_ok" } }
    }

    private fun request(
        method: String,
        url: String,
        body: String? = null,
        clientVersion: String? = null,
        installId: String? = null,
        consentVersion: String? = null
    ): HttpResponse {
        val connection = (URI(url).toURL().openConnection() as HttpURLConnection).apply {
            requestMethod = method
            connectTimeout = CONNECT_TIMEOUT_MS
            readTimeout = READ_TIMEOUT_MS
            setRequestProperty("Accept", "application/json")
            clientVersion?.let {
                setRequestProperty("User-Agent", "waffle_meter/$it")
                setRequestProperty("x-client-version", it)
            }
            installId?.let { setRequestProperty("x-install-id", it) }
            consentVersion?.let { setRequestProperty("x-consent-version", it) }
            if (body != null) {
                doOutput = true
                setRequestProperty("Content-Type", "application/json")
            }
        }

        try {
            if (body != null) {
                connection.outputStream.use { output ->
                    output.write(body.toByteArray(Charsets.UTF_8))
                }
            }

            val status = connection.responseCode
            val responseText = runCatching {
                val stream = if (status in 200..299) connection.inputStream else connection.errorStream
                stream?.bufferedReader(Charsets.UTF_8)?.use { it.readText() }
            }.getOrNull().orEmpty()

            if (status !in 200..299) {
                val summary = responseText.take(300).ifBlank { "empty_response" }
                throw IllegalStateException("HTTP $status: $summary")
            }

            return HttpResponse(status, responseText)
        } finally {
            connection.disconnect()
        }
    }

    private data class HttpResponse(val statusCode: Int, val body: String)
}

@Serializable
data class ConsentStatusResponse(
    val ok: Boolean,
    val identityHash: String,
    val exists: Boolean,
    val consentState: String,
    @SerialName("public")
    val publicCharacter: Boolean = false,
    val consentVersion: String? = null,
    val updatedAt: String? = null,
    val lastSeenAt: String? = null,
    val characterId: String? = null
)

@Serializable
data class ConsentEventRequest(
    val identityHash: String? = null,
    val consentState: String,
    val consentVersion: String,
    val character: ConsentEventCharacter? = null
)

@Serializable
data class ConsentEventCharacter(
    val identityHash: String,
    val nickname: String,
    val server: Int,
    val job: String? = null,
    val power: Int = 0,
    @SerialName("public")
    val publicCharacter: Boolean
)

@Serializable
data class ReportUploadResponse(
    val ok: Boolean,
    val reportId: String? = null,
    val duplicate: Boolean = false
)

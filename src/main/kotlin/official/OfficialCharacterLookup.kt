package com.tbread.official

import com.tbread.entity.enums.JobClass
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.contentOrNull
import kotlinx.serialization.json.intOrNull
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import org.slf4j.LoggerFactory
import java.net.HttpURLConnection
import java.net.URI
import java.net.URLEncoder
import java.util.concurrent.ConcurrentHashMap

data class OfficialCharacterInfo(
    val nickname: String,
    val server: Int,
    val job: JobClass?,
    val power: Int,
    val skills: Map<Int, Int>
)

object OfficialCharacterLookup {
    private const val BASE_URL = "https://aion2.plaync.com"
    private const val SUCCESS_TTL_MS = 6 * 60 * 60 * 1000L
    private const val MISS_TTL_MS = 10 * 60 * 1000L
    private const val CONNECT_TIMEOUT_MS = 3_000
    private const val READ_TIMEOUT_MS = 5_000

    private val logger = LoggerFactory.getLogger(OfficialCharacterLookup::class.java)
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private val json = Json { ignoreUnknownKeys = true }
    private val cache = ConcurrentHashMap<String, CacheEntry>()
    private val inFlight = ConcurrentHashMap.newKeySet<String>()

    fun lookupAsync(
        nickname: String?,
        server: Int,
        fallbackJob: JobClass?,
        callback: (OfficialCharacterInfo) -> Unit
    ) {
        val normalized = normalizeNickname(nickname) ?: return
        if (server <= 0) return

        val key = cacheKey(normalized, server)
        val now = System.currentTimeMillis()
        cache[key]?.let { cached ->
            if (cached.expiresAt > now) {
                cached.info?.let(callback)
                return
            }
            cache.remove(key, cached)
        }

        if (!inFlight.add(key)) return
        scope.launch {
            try {
                val info = lookup(normalized, server, fallbackJob)
                cache[key] = CacheEntry(info, now + if (info == null) MISS_TTL_MS else SUCCESS_TTL_MS)
                if (info != null) callback(info)
            } catch (e: Exception) {
                logger.debug("공식 캐릭터 조회 실패: nickname={}, server={}, reason={}", normalized, server, e.message)
                cache[key] = CacheEntry(null, now + MISS_TTL_MS)
            } finally {
                inFlight.remove(key)
            }
        }
    }

    fun lookupBlocking(
        nickname: String?,
        server: Int,
        fallbackJob: JobClass?
    ): OfficialCharacterInfo? {
        val normalized = normalizeNickname(nickname) ?: return null
        if (server <= 0) return null

        val key = cacheKey(normalized, server)
        val now = System.currentTimeMillis()
        cache[key]?.let { cached ->
            if (cached.expiresAt > now) {
                return cached.info
            }
            cache.remove(key, cached)
        }

        return try {
            val info = lookup(normalized, server, fallbackJob)
            cache[key] = CacheEntry(info, now + if (info == null) MISS_TTL_MS else SUCCESS_TTL_MS)
            info
        } catch (e: Exception) {
            logger.debug("공식 캐릭터 동기 조회 실패: nickname={}, server={}, reason={}", normalized, server, e.message)
            cache[key] = CacheEntry(null, now + MISS_TTL_MS)
            null
        }
    }

    private fun lookup(nickname: String, server: Int, fallbackJob: JobClass?): OfficialCharacterInfo? {
        val character = findCharacter(nickname, server) ?: return null
        val skills = fetchEquippedSkills(character.characterId, character.serverId)
        val power = fetchCombatPower(character.characterId, character.serverId)
        return OfficialCharacterInfo(
            nickname = nickname,
            server = character.serverId,
            job = character.job ?: fallbackJob,
            power = power,
            skills = skills
        )
    }

    private fun findCharacter(nickname: String, server: Int): CharacterSearchResult? {
        val params = mapOf(
            "keyword" to nickname,
            "pcId" to "",
            "race" to "",
            "serverId" to server.toString(),
            "sort" to "desc",
            "page" to "1",
            "size" to "20"
        )
        val root = requestJson("$BASE_URL/api/search/character?${query(params)}")
        val list = root["list"]?.jsonArray ?: JsonArray(emptyList())
        return list
            .mapNotNull { element ->
                val obj = element.jsonObject
                val name = stripHtml(obj["name"]?.jsonPrimitive?.contentOrNull.orEmpty())
                val serverId = obj["serverId"]?.jsonPrimitive?.intOrNull ?: return@mapNotNull null
                if (name != nickname || serverId != server) return@mapNotNull null
                CharacterSearchResult(
                    characterId = obj["characterId"]?.jsonPrimitive?.contentOrNull ?: return@mapNotNull null,
                    serverId = serverId,
                    level = obj["level"]?.jsonPrimitive?.intOrNull ?: 0,
                    job = obj["pcId"]?.jsonPrimitive?.intOrNull?.let(JobClass::convertFromCode)
                )
            }
            .maxByOrNull { it.level }
    }

    private fun fetchEquippedSkills(characterId: String, server: Int): Map<Int, Int> {
        val params = mapOf(
            "lang" to "ko",
            "characterId" to characterId,
            "serverId" to server.toString()
        )
        val root = requestJson("$BASE_URL/api/character/equipment?${query(params)}")
        val skills = root["skill"]
            ?.jsonObject
            ?.get("skillList")
            ?.jsonArray
            ?: return emptyMap()

        return skills.mapNotNull { element ->
            val obj = element.jsonObject
            val acquired = obj["acquired"]?.jsonPrimitive?.intOrNull ?: 0
            val equipped = obj["equip"]?.jsonPrimitive?.intOrNull ?: 0
            if (acquired <= 0 || equipped != 1) return@mapNotNull null
            val code = obj["id"]?.jsonPrimitive?.intOrNull ?: return@mapNotNull null
            val level = obj["skillLevel"]?.jsonPrimitive?.intOrNull ?: 0
            code to level
        }.toMap()
    }

    private fun fetchCombatPower(characterId: String, server: Int): Int {
        val params = mapOf(
            "lang" to "ko",
            "characterId" to characterId,
            "serverId" to server.toString()
        )
        return runCatching {
            val root = requestJson("$BASE_URL/api/character/info?${query(params)}")
            root["profile"]
                ?.jsonObject
                ?.get("combatPower")
                ?.jsonPrimitive
                ?.intOrNull
                ?: 0
        }.getOrDefault(0)
    }

    private fun requestJson(url: String): JsonObject {
        val connection = (URI(url).toURL().openConnection() as HttpURLConnection).apply {
            requestMethod = "GET"
            connectTimeout = CONNECT_TIMEOUT_MS
            readTimeout = READ_TIMEOUT_MS
            setRequestProperty("Accept", "application/json, text/plain, */*")
            setRequestProperty("User-Agent", "waffle_meter")
            setRequestProperty("Referer", "$BASE_URL/ko-kr/characters/index")
        }
        return try {
            val status = connection.responseCode
            val text = (if (status in 200..299) connection.inputStream else connection.errorStream)
                ?.bufferedReader(Charsets.UTF_8)
                ?.use { it.readText() }
                .orEmpty()
            if (status !in 200..299) {
                throw IllegalStateException("HTTP $status: ${text.take(160)}")
            }
            json.parseToJsonElement(text).jsonObject
        } finally {
            connection.disconnect()
        }
    }

    private fun query(params: Map<String, String>): String =
        params.entries.joinToString("&") { (key, value) ->
            "${URLEncoder.encode(key, Charsets.UTF_8)}=${URLEncoder.encode(value, Charsets.UTF_8)}"
        }

    private fun normalizeNickname(nickname: String?): String? =
        nickname?.trim()?.takeIf { it.isNotEmpty() }

    private fun cacheKey(nickname: String, server: Int): String = "$server:$nickname"

    private fun stripHtml(value: String): String =
        value.replace(Regex("<[^>]+>"), "").trim()

    private data class CharacterSearchResult(
        val characterId: String,
        val serverId: Int,
        val level: Int,
        val job: JobClass?
    )

    private data class CacheEntry(
        val info: OfficialCharacterInfo?,
        val expiresAt: Long
    )
}

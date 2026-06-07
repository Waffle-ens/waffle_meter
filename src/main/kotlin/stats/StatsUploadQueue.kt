package com.tbread.stats

import com.tbread.config.PropertyHandler
import com.tbread.data.DataManager
import com.tbread.entity.DpsLog
import org.slf4j.LoggerFactory
import java.awt.Desktop
import java.io.File
import java.util.Collections
import java.util.concurrent.Executors
import java.util.concurrent.atomic.AtomicInteger

object StatsUploadQueue {
    private val logger = LoggerFactory.getLogger(StatsUploadQueue::class.java)
    private val executor = Executors.newSingleThreadExecutor { task ->
        Thread(task, "stats-upload-queue").apply { isDaemon = true }
    }
    private val uploadedHashes = Collections.synchronizedSet(mutableSetOf<String>())
    private val pending = AtomicInteger(0)
    private val uploaded = AtomicInteger(0)
    private val skipped = AtomicInteger(0)
    private val failed = AtomicInteger(0)

    @Volatile
    private var clientVersion = "dev"
    @Volatile
    private var lastPath: String? = null
    @Volatile
    private var lastReason: String? = null
    @Volatile
    private var lastUpdatedAt: Long = 0L

    fun configure(version: String) {
        clientVersion = version
    }

    fun offerIfEligible(log: DpsLog) {
        if (!StatsConsentManager.isUploadAllowed()) {
            markSkipped("consent_not_allowed")
            return
        }

        val target = log.report.target
        if (target == null || !target.mob.boss || target.mob.isDummy) {
            markSkipped("not_boss")
            return
        }

        if (isKillConfirmed(log)) {
            enqueue(log, true)
            return
        }

        executor.execute {
            pending.incrementAndGet()
            try {
                Thread.sleep(4_000L)
                if (isKillConfirmed(log)) {
                    uploadPayload(log, true)
                } else {
                    markSkipped("not_kill")
                }
            } finally {
                pending.decrementAndGet()
            }
        }
    }

    fun status(): StatsUploadStatus {
        return StatsUploadStatus(
            enabled = StatsConsentManager.isUploadAllowed(),
            pending = pending.get(),
            uploaded = uploaded.get(),
            skipped = skipped.get(),
                failed = failed.get(),
                lastPath = lastPath,
            lastReason = lastReason,
            lastUpdatedAt = lastUpdatedAt
        )
    }

    fun openFolder(): String {
        val dir = outputDir().also { it.mkdirs() }
        runCatching {
            if (Desktop.isDesktopSupported()) {
                Desktop.getDesktop().open(dir)
            } else {
                Runtime.getRuntime().exec(arrayOf("explorer.exe", dir.absolutePath))
            }
        }.onFailure {
            logger.warn("통계 업로드 폴더 열기 실패: {}", dir.absolutePath, it)
        }
        return dir.absolutePath
    }

    private fun enqueue(log: DpsLog, killConfirmed: Boolean) {
        executor.execute {
            pending.incrementAndGet()
            try {
            uploadPayload(log, killConfirmed)
            } finally {
                pending.decrementAndGet()
            }
        }
    }

    private fun uploadPayload(log: DpsLog, killConfirmed: Boolean) {
        when (val result = StatsPayloadBuilder.build(log, clientVersion, killConfirmed)) {
            is StatsPayloadBuilder.BuildResult.Skip -> {
                markSkipped(result.reason)
            }
            is StatsPayloadBuilder.BuildResult.Payload -> {
                val payload = result.payload
                if (uploadedHashes.contains(payload.battleHash)) {
                    markSkipped("duplicate")
                    return
                }

                try {
                    val response = StatsApiClient.postReport(payload, clientVersion)
                    uploadedHashes.add(payload.battleHash)
                    uploaded.incrementAndGet()
                    val reason = if (response.duplicate) "uploaded_duplicate" else "uploaded"
                    updateLast(StatsApiClient.reportEndpoint(), "$reason:${response.reportId ?: "no_report_id"}")
                    logger.info(
                        "통계 payload 전송 완료: duplicate={}, reportId={}, endpoint={}",
                        response.duplicate,
                        response.reportId,
                        StatsApiClient.reportEndpoint()
                    )
                } catch (e: Exception) {
                    failed.incrementAndGet()
                    val reason = e.message?.take(160)?.ifBlank { null } ?: e.javaClass.simpleName
                    updateLast(StatsApiClient.reportEndpoint(), "upload_failed:$reason")
                    logger.warn("통계 payload 전송 실패: endpoint={}", StatsApiClient.reportEndpoint(), e)
                }
            }
        }
    }

    private fun isKillConfirmed(log: DpsLog): Boolean {
        val target = log.report.target ?: return false
        val snapshotKill = target.maxHp > 0 && target.remainHp <= 0
        val latestHp = DataManager.mobHp(target.id)
        val latestMaxHp = DataManager.mobMaxHp(target.id) ?: target.maxHp
        val latestKill = latestMaxHp > 0 && latestHp == 0
        return snapshotKill || latestKill
    }

    private fun markSkipped(reason: String) {
        skipped.incrementAndGet()
        updateLast(null, reason)
    }

    private fun updateLast(path: String?, reason: String) {
        lastPath = path
        lastReason = reason
        lastUpdatedAt = System.currentTimeMillis()
    }

    private fun outputDir(): File {
        return File(PropertyHandler.appDirectory(), "stats-upload")
    }

}

package com.tbread.packet

import com.tbread.entity.ParsedDamagePacket
import java.io.BufferedWriter
import java.io.File
import java.io.FileOutputStream
import java.io.OutputStreamWriter
import java.time.LocalDateTime
import java.time.format.DateTimeFormatter
import java.util.Base64

object PacketDebugLogger {
    private const val APP_NAME = "waffle_meter.v1.4"
    private val timestampFormat = DateTimeFormatter.ofPattern("yyyyMMdd-HHmmss")

    private data class Session(
        val file: File,
        val startedAt: Long,
        val writer: BufferedWriter,
        var lines: Long = 0,
        var captureCount: Long = 0,
        var captureBytes: Long = 0,
        var assembledCount: Long = 0,
        var dispatchCount: Long = 0,
        var parsedDamageCount: Long = 0,
        var parsedBattleCount: Long = 0,
        var parsedMetaCount: Long = 0,
        var unknownOpcodeCount: Long = 0,
        var errorCount: Long = 0
    )

    @Volatile
    private var session: Session? = null
    private var lastStatus = statusJson(false, "", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0)

    @Synchronized
    fun start(): String {
        session?.let { return statusJson(it, true) }

        val dir = logDir()
        dir.mkdirs()
        val file = File(dir, "${LocalDateTime.now().format(timestampFormat)}-packet-debug.jsonl")
        val writer = BufferedWriter(OutputStreamWriter(FileOutputStream(file, true), Charsets.UTF_8), 1024 * 1024)
        val next = Session(file = file, startedAt = System.currentTimeMillis(), writer = writer)
        session = next
        write(next, linkedMapOf("type" to "session_start", "at" to next.startedAt, "path" to file.absolutePath))
        lastStatus = statusJson(next, true)
        return lastStatus
    }

    @Synchronized
    fun stop(): String {
        val current = session ?: return lastStatus
        write(current, linkedMapOf("type" to "session_stop", "at" to System.currentTimeMillis()))
        current.writer.flush()
        current.writer.close()
        session = null
        lastStatus = statusJson(current, false)
        return lastStatus
    }

    @Synchronized
    fun status(): String {
        session?.let {
            it.writer.flush()
            lastStatus = statusJson(it, true)
        }
        return lastStatus
    }

    fun logDirectory(): File = logDir()

    fun capture(ip: String, seq: Long, data: ByteArray, arrivedAt: Long) {
        if (session == null) return
        val encoded = Base64.getEncoder().encodeToString(data)
        synchronized(this) {
            val current = session ?: return
            current.captureCount += 1
            current.captureBytes += data.size.toLong()
            write(
                current,
                linkedMapOf(
                    "type" to "capture",
                    "at" to arrivedAt,
                    "ip" to ip,
                    "seq" to seq,
                    "len" to data.size,
                    "head" to hexHead(data),
                    "data" to encoded
                )
            )
        }
    }

    fun assembled(packet: ByteArray, arrivedAt: Long) {
        synchronized(this) {
            val current = session ?: return
            current.assembledCount += 1
            write(
                current,
                linkedMapOf(
                    "type" to "assembled",
                    "at" to arrivedAt,
                    "len" to packet.size,
                    "head" to hexHead(packet)
                )
            )
        }
    }

    fun dispatch(opcodeKey: Int, opcodeName: String?, extraFlag: Boolean, size: Int, arrivedAt: Long) {
        synchronized(this) {
            val current = session ?: return
            current.dispatchCount += 1
            write(
                current,
                linkedMapOf(
                    "type" to "dispatch",
                    "at" to arrivedAt,
                    "opcode" to opcodeKey,
                    "opcodeHex" to opcodeKey.toString(16).padStart(4, '0'),
                    "opcodeName" to opcodeName,
                    "extraFlag" to extraFlag,
                    "len" to size
                )
            )
        }
    }

    fun unknownOpcode(opcodeKey: Int, extraFlag: Boolean, size: Int, arrivedAt: Long, head: ByteArray) {
        synchronized(this) {
            val current = session ?: return
            current.unknownOpcodeCount += 1
            write(
                current,
                linkedMapOf(
                    "type" to "unknown_opcode",
                    "at" to arrivedAt,
                    "opcode" to opcodeKey,
                    "opcodeHex" to opcodeKey.toString(16).padStart(4, '0'),
                    "extraFlag" to extraFlag,
                    "len" to size,
                    "head" to hexHead(head)
                )
            )
        }
    }

    fun parserError(type: String, reason: String, packet: ByteArray? = null, offset: Int? = null) {
        synchronized(this) {
            val current = session ?: return
            current.errorCount += 1
            val fields = linkedMapOf<String, Any?>(
                "type" to "parser_error",
                "at" to System.currentTimeMillis(),
                "stage" to type,
                "reason" to reason
            )
            if (offset != null) fields["offset"] = offset
            if (packet != null) {
                fields["len"] = packet.size
                fields["head"] = hexHead(packet)
            }
            write(current, fields)
        }
    }

    fun damage(kind: String, packet: ParsedDamagePacket, saved: Boolean, reason: String? = null, mobCode: Int? = null) {
        synchronized(this) {
            val current = session ?: return
            current.parsedDamageCount += 1
            write(
                current,
                linkedMapOf(
                    "type" to "damage",
                    "kind" to kind,
                    "at" to (packet.getTimeStamp().takeIf { it > 0 } ?: System.currentTimeMillis()),
                    "saved" to saved,
                    "reason" to reason,
                    "actor" to packet.getActorId(),
                    "target" to packet.getTargetId(),
                    "skill" to packet.getSkillCode1(),
                    "damage" to packet.getDamage(),
                    "crit" to packet.isCrit(),
                    "dot" to packet.isDoT(),
                    "loop" to packet.getLoop(),
                    "mobCode" to mobCode
                )
            )
        }
    }

    fun battle(target: Int, toggle: Int, mobCode: Int?, mobName: String?, accepted: Boolean, reason: String?) {
        synchronized(this) {
            val current = session ?: return
            current.parsedBattleCount += 1
            write(
                current,
                linkedMapOf(
                    "type" to "battle",
                    "at" to System.currentTimeMillis(),
                    "target" to target,
                    "toggle" to toggle,
                    "mobCode" to mobCode,
                    "mobName" to mobName,
                    "accepted" to accepted,
                    "reason" to reason
                )
            )
        }
    }

    fun meta(type: String, fields: Map<String, Any?>) {
        synchronized(this) {
            val current = session ?: return
            current.parsedMetaCount += 1
            val data = linkedMapOf<String, Any?>("type" to type, "at" to System.currentTimeMillis())
            data.putAll(fields)
            write(current, data)
        }
    }

    private fun logDir(): File {
        val appData = System.getenv("APPDATA") ?: System.getProperty("user.home")
        return File(File(appData, APP_NAME), "packet-debug-logs")
    }

    private fun write(current: Session, fields: Map<String, Any?>) {
        current.writer.write(toJson(fields))
        current.writer.newLine()
        current.lines += 1
        if (current.lines % 200L == 0L) current.writer.flush()
    }

    private fun statusJson(current: Session, running: Boolean): String =
        statusJson(
            running = running,
            path = current.file.absolutePath,
            startedAt = current.startedAt,
            lines = current.lines,
            captureCount = current.captureCount,
            captureBytes = current.captureBytes,
            assembledCount = current.assembledCount,
            dispatchCount = current.dispatchCount,
            parsedDamageCount = current.parsedDamageCount,
            parsedBattleCount = current.parsedBattleCount,
            parsedMetaCount = current.parsedMetaCount,
            unknownOpcodeCount = current.unknownOpcodeCount,
            errorCount = current.errorCount
        )

    private fun statusJson(
        running: Boolean,
        path: String,
        startedAt: Long,
        lines: Long,
        captureCount: Long,
        captureBytes: Long,
        assembledCount: Long,
        dispatchCount: Long,
        parsedDamageCount: Long,
        parsedBattleCount: Long,
        parsedMetaCount: Long,
        unknownOpcodeCount: Long,
        errorCount: Long
    ): String = toJson(
        linkedMapOf(
            "running" to running,
            "path" to path,
            "startedAt" to startedAt,
            "lines" to lines,
            "captureCount" to captureCount,
            "captureBytes" to captureBytes,
            "assembledCount" to assembledCount,
            "dispatchCount" to dispatchCount,
            "parsedDamageCount" to parsedDamageCount,
            "parsedBattleCount" to parsedBattleCount,
            "parsedMetaCount" to parsedMetaCount,
            "unknownOpcodeCount" to unknownOpcodeCount,
            "errorCount" to errorCount
        )
    )

    private fun toJson(fields: Map<String, Any?>): String =
        fields.entries.joinToString(prefix = "{", postfix = "}") { (key, value) ->
            "${quote(key)}:${jsonValue(value)}"
        }

    private fun jsonValue(value: Any?): String = when (value) {
        null -> "null"
        is Number -> value.toString()
        is Boolean -> value.toString()
        else -> quote(value.toString())
    }

    private fun quote(value: String): String {
        val out = StringBuilder(value.length + 8)
        out.append('"')
        value.forEach { ch ->
            when (ch) {
                '\\' -> out.append("\\\\")
                '"' -> out.append("\\\"")
                '\b' -> out.append("\\b")
                '\u000C' -> out.append("\\f")
                '\n' -> out.append("\\n")
                '\r' -> out.append("\\r")
                '\t' -> out.append("\\t")
                else -> {
                    if (ch.code < 0x20) out.append("\\u").append(ch.code.toString(16).padStart(4, '0'))
                    else out.append(ch)
                }
            }
        }
        out.append('"')
        return out.toString()
    }

    private fun hexHead(bytes: ByteArray, maxBytes: Int = 24): String =
        bytes.take(maxBytes).joinToString(" ") { "%02X".format(it) }
}

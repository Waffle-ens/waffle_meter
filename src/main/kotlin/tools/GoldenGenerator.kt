package com.tbread.tools

import com.tbread.DpsCalculator
import com.tbread.data.DataManager
import com.tbread.entity.DpsLog
import com.tbread.packet.PacketAlignmenter
import com.tbread.packet.StreamAssembler
import com.tbread.packet.StreamProcessor
import kotlinx.coroutines.runBlocking
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.long
import java.io.File
import java.util.Base64

/**
 * Deterministic DPS golden generator for the WPF/.NET migration parity harness.
 *
 * Replays a recorded packet-debug corpus (.jsonl, the `capture` records) through the REAL live
 * pipeline (PacketAlignmenter -> StreamAssembler -> StreamProcessor -> DataManager + DpsCalculator),
 * mirroring Main.kt's consume loop, and dumps the saved battle history (completed battles) as JSON.
 *
 * Determinism: DataManager.clock is set to a simulated clock that tracks each packet's recorded
 * arrivedAt, so the time-based battle logic (activePacketCutoff = currentBattleStart - 1000ms, the
 * dummy timeout, the ended-battle ignore window) behaves exactly as it did live — instead of using
 * the (far-future) wall clock at replay time, which would trim every packet.
 *
 * Only COMPLETED battles are dumped (their battleStart/End and aggregates are frozen at battleEnd),
 * so the output is reproducible. Raw packets are stripped from each DpsLog (irrelevant to numbers).
 *
 * Usage (via gradle): ./gradlew generateGolden -Pcorpus=<corpus.jsonl> -Pout=<out.json>
 */
fun main(args: Array<String>) {
    val corpusPath = args.getOrNull(0) ?: error("usage: GoldenGenerator <corpus.jsonl> <out.json>")
    val outPath = args.getOrNull(1) ?: error("usage: GoldenGenerator <corpus.jsonl> <out.json>")

    DataManager.load()

    var simNow = 0L
    DataManager.clock = { simNow }

    val processor = StreamProcessor()
    val alignmenter = PacketAlignmenter()
    val assembler = StreamAssembler(processor)
    val calculator = DpsCalculator {
        assembler.flush()
        alignmenter.reset()
    }

    var currentIp = ""
    var captureCount = 0L

    runBlocking {
        for (line in File(corpusPath).readLines()) {
            if (line.isBlank()) continue
            val obj = Json.parseToJsonElement(line).jsonObject
            if (obj["type"]?.jsonPrimitive?.content != "capture") continue

            val seq = obj["seq"]!!.jsonPrimitive.long
            val at = obj["at"]!!.jsonPrimitive.long
            val ip = obj["ip"]?.jsonPrimitive?.content ?: ""
            val data = Base64.getDecoder().decode(obj["data"]!!.jsonPrimitive.content)
            captureCount++

            simNow = at
            if (ip != currentIp) {
                currentIp = ip
                alignmenter.reset()
            }
            val chunks = alignmenter.feed(seq, data, at)
            for ((chunk, ts) in chunks) {
                assembler.processChunk(chunk, ts)
            }
            // Drive the battle state machine each packet so every transition (and its save) fires,
            // exactly as the live 500ms getDps loop does.
            calculator.getDps()
        }
        // Flush the final in-progress battle to history (saves the recent unsaved, non-dummy battle).
        calculator.resetDataStorage()
    }

    // Saved completed battles (BattleLogRepository, up to 12), packets stripped.
    val battles: List<DpsLog> = (0 until DataManager.recentBattleList().size)
        .mapNotNull { DataManager.battleLog(it)?.copy(packets = emptyList()) }

    val json = Json { prettyPrint = false; encodeDefaults = true }
    File(outPath).writeText(json.encodeToString(battles))
    println("GoldenGenerator: captures=$captureCount, battles=${battles.size} -> $outPath")
}

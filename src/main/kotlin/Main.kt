package com.tbread

import com.tbread.config.PcapCapturerConfig
import com.tbread.config.PropertyHandler
import com.tbread.config.VersionConfig
import com.tbread.data.DataManager
import com.tbread.packet.*
import com.tbread.webview.BrowserApp
import javafx.application.Platform
import javafx.stage.Stage
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.runBlocking

fun main() = runBlocking {
    val gpuAcceleration = PropertyHandler.getProperty("gpuAcceleration", "true")?.toBooleanStrictOrNull() ?: true
    if (gpuAcceleration) {
        System.setProperty("prism.order", "d3d,sw")
        System.setProperty("prism.lcdtext", "true")
    } else {
        System.setProperty("prism.order", "sw")
    }

    // 오버레이 렌더링 프레임 제한 (JavaFX 마스터 펄스 캡). 30~60fps 범위로 클램프하며 settings.properties 의
    // meterFrameRate 로 조정 가능. 펄스를 낮추면 씬 그래프(WebView 포함) 재렌더 주기가 줄어 오버레이 GPU 부하가 감소.
    val meterFrameRate = (PropertyHandler.getProperty("meterFrameRate", "40")?.toIntOrNull() ?: 40)
        .coerceIn(30, 60)
    System.setProperty("javafx.animation.framerate", meterFrameRate.toString())

    Thread.setDefaultUncaughtExceptionHandler { t, e ->
        println("thread dead ${t.name}")
        e.printStackTrace()
    }

    DataManager.load()

    val channel = Channel<CapturedPacket>(Channel.UNLIMITED)
    val pcapConfig = PcapCapturerConfig.loadFromProperties()
    val versionConfig = VersionConfig.loadFromProperties()


    val processor = StreamProcessor()
    val alignmenter = PacketAlignmenter()
    val assembler = StreamAssembler(processor)
    val capturer = PcapCapturer(pcapConfig, channel)
    val calculator = DpsCalculator {
        assembler.flush()
        alignmenter.reset()
    }

    launch(Dispatchers.Default) {
        var currentIp = ""
        for ((ip, seq, data, arrivedAt) in channel) {
            if (ip != currentIp) {
                currentIp = ip
                alignmenter.reset()
            }
            val chunks = alignmenter.feed(seq, data, arrivedAt)
            for ((chunk, ts) in chunks) {
                assembler.processChunk(chunk, ts)
            }
        }
    }

    launch(Dispatchers.IO) {
        capturer.start()
    }

    launch {
        while (true) {
            delay(1000)
            DataManager.checkDummyTimeout()
        }
    }

    Platform.startup {
        val browserApp = BrowserApp(versionConfig,calculator)
        browserApp.start(Stage())
    }
}



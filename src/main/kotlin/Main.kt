package com.tbread

import com.tbread.config.PcapCapturerConfig
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
    // 오버레이는 소프트웨어 렌더(prism sw)로 고정한다. d3d(GPU) 렌더는 전체화면 게임(AION2)의 GPU present 와
    // 경합해 오버레이 드래그/입력이 심하게 끊겼다. sw 는 GPU 경합이 없어 게임 위에서도 부드럽다.
    System.setProperty("prism.order", "sw")

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



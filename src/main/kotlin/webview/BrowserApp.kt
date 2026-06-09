package com.tbread.webview

import com.sun.jna.Pointer
import com.sun.jna.platform.win32.*
import com.tbread.DpsCalculator
import com.tbread.addon.UploadManager
import com.tbread.config.HotkeyHandler
import com.tbread.config.PropertyHandler
import com.tbread.config.VersionConfig
import com.tbread.data.DataManager
import com.tbread.entity.DpsReport
import com.tbread.entity.JoinRequestUser
import com.tbread.packet.PacketEvent
import com.tbread.packet.PacketEventBus
import com.tbread.packet.PacketDebugLogger
import com.tbread.stats.StatsConsentManager
import com.tbread.stats.StatsPayloadBuilder
import com.tbread.stats.StatsUploadQueue
import javafx.application.Application
import javafx.application.HostServices
import javafx.application.Platform
import javafx.concurrent.Worker
import javafx.geometry.Rectangle2D
import javafx.scene.Scene
import javafx.scene.paint.Color
import javafx.scene.web.WebEngine
import javafx.scene.web.WebView
import javafx.stage.Screen
import javafx.stage.Stage
import javafx.stage.StageStyle
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import netscape.javascript.JSObject
import org.slf4j.LoggerFactory
import java.awt.*
import java.awt.event.MouseAdapter
import java.awt.event.MouseEvent
import javax.imageio.ImageIO
import kotlin.system.exitProcess

class BrowserApp(private val config: VersionConfig, private val dpsCalculator: DpsCalculator) : Application() {

    private val logger = LoggerFactory.getLogger(BrowserApp::class.java)
    private val dpsRefreshMs = 500L
    private val overlayFocusCheckMs = 300L

    companion object {
        private const val GWL_EXSTYLE = -20
        private const val WS_EX_TOOLWINDOW = 0x00000080
        private const val WS_EX_APPWINDOW = 0x00040000
        private const val WS_EX_LAYERED = 0x00080000
        private const val WS_EX_TRANSPARENT = 0x00000020
        // 오버레이가 포커스(foreground)를 절대 뺏지 않게 한다. 보더리스 전체창 게임은 자기가 foreground일 때만
        // 고FPS(독립 flip/풀스크린 최적화)를 유지하는데, 활성화되는 오버레이는 alt-tab과 동일하게 게임 FPS를 떨군다.
        private const val WS_EX_NOACTIVATE = 0x08000000

        private const val SWP_NOSIZE = 0x0001
        private const val SWP_NOMOVE = 0x0002
        private const val SWP_NOZORDER = 0x0004
        private const val SWP_NOACTIVATE = 0x0010
        private const val SWP_FRAMECHANGED = 0x0020
        private const val SWP_SHOWWINDOW = 0x0040

        private val HWND_TOPMOST = WinDef.HWND(Pointer.createConstant(-1))
        private val HWND_TOP = WinDef.HWND(Pointer.createConstant(0))
        private val HWND_NOTOPMOST = WinDef.HWND(Pointer.createConstant(-2))
        private val HWND_BOTTOM = WinDef.HWND(Pointer.createConstant(1))
    }

    private lateinit var engine: WebEngine
    private var trayIcon: TrayIcon? = null

    inner class JSBridge(
        private val stage: Stage,
        private val webView: WebView,
        private val hostServices: HostServices
    ) {

        fun saveProps(key: String, value: String) {
            PropertyHandler.setProperty(key, value)
        }

        fun loadProps(key: String): String? {
            return PropertyHandler.getProperty(key)
        }

        fun moveWindow(x: Double, y: Double) {
            fitOverlayToScreen(stage, webView)
        }

        fun syncOverlayBounds(): String {
            return fitOverlayToScreen(stage, webView)
        }

        // small-window 방식 작은 창: Stage 를 화면 절대좌표(x,y) + 크기(w,h)로 직접 배치한다(논리 px).
        // 전체화면 fitOverlayToScreen 대신, 보이는 콘텐츠 bbox 만큼만 창을 잡아 게임 위 합성 부담을 줄인다.
        // 창 전체가 화면 안에 남도록 clamp(다중 모니터 모드면 가상 화면, 아니면 주 화면 기준).
        fun setWindowBounds(x: Double, y: Double, w: Double, h: Double) {
            val width = w.coerceAtLeast(1.0)
            val height = h.coerceAtLeast(1.0)
            val (cx, cy) = clampToOverlayBounds(x, y, width, height)
            stage.x = cx
            stage.y = cy
            stage.width = width
            stage.height = height
            webView.minWidth = width
            webView.minHeight = height
            webView.prefWidth = width
            webView.prefHeight = height
            webView.maxWidth = width
            webView.maxHeight = height
        }

        // 네이티브 창 이동(드래그용). 크기 유지, 위치만 화면 안으로 clamp 해 갱신. 적용된 위치를 "x,y" 로 반환
        // → JS 가 저장하는 uiX/uiY 도 clamp 값과 일치(드래그가 화면 밖으로 안 빠짐).
        fun moveWindowTo(x: Double, y: Double): String {
            val (cx, cy) = clampToOverlayBounds(x, y, stage.width, stage.height)
            stage.x = cx
            stage.y = cy
            return "$cx,$cy"
        }

        fun resetDps() {
            dpsCalculator.resetDataStorage()
            engine.executeScript("resetDpsUI()")
        }

        fun hardResetDps() {
            dpsCalculator.hardReset()
        }

        fun updateHotkey(modifiers: Int, vkCode: Int) {
            HotkeyHandler.updateHotkey(modifiers, vkCode)
        }

        fun getHotkey(): String {
            return HotkeyHandler.getCurrentHotkey().toString()
        }

        fun openBrowser(url: String) {
            try {
                hostServices.showDocument(url)
            } catch (e: Exception) {
                e.printStackTrace()
            }
        }

        fun exitApp() {
            shutdown()
        }

        fun hideToTray() {
            hideToTray(stage)
        }

        fun toggleVisibility() {
            if (isVisible) hideToTray(stage) else showFromTray(stage)
        }

        fun showWindow() {
            if (!isVisible) showFromTray(stage)
        }

        fun getHideHotkey(): String {
            return HotkeyHandler.getVisibilityHotkey().toString()
        }

        fun updateHideHotkey(modifiers: Int, vkCode: Int) {
            HotkeyHandler.updateVisibilityHotkey(modifiers, vkCode)
        }

        fun isClickThrough(): Boolean = isClickThrough

        // 설정 패널 토글용: 네이티브 클릭스루(WS_EX_TRANSPARENT)를 실제로 적용한다.
        // (단축키와 동일한 setClickThrough 경로 → onClickThroughChanged 로 JS store 도 동기화됨.)
        fun setClickThrough(enable: Boolean) {
            this@BrowserApp.setClickThrough(enable)
        }

        fun toggleAutoHide() {
            isAutoHide = !isAutoHide
            PropertyHandler.setProperty("isAutoHide", isAutoHide.toString())
        }

        fun isAutoHide(): Boolean = isAutoHide

        fun getClickThroughHotkey(): String {
            return HotkeyHandler.getClickThroughHotkey().toString()
        }

        fun updateClickThroughHotkey(modifiers: Int, vkCode: Int) {
            HotkeyHandler.updateClickThroughHotkey(modifiers, vkCode)
        }

        fun isDevBuild(): Boolean {
            return version.contains("dev", ignoreCase = true)
        }

        fun getDpsData(): String {
            return cachedDpsJson
        }

        fun isDebuggingMode(): Boolean {
            return debugMode
        }

        fun isPacketLoggingEnabled(): Boolean {
            return DataManager.isPacketLoggingEnabled()
        }

        fun setPacketLoggingEnabled(enabled: Boolean) {
            DataManager.setPacketLoggingEnabled(enabled)
        }

        fun exportPacketLog(): String {
            return DataManager.exportRawPacketLog()
        }

        fun startPacketLogging(): String {
            DataManager.setPacketLoggingEnabled(true)
            return PacketDebugLogger.start()
        }

        fun stopPacketLogging(): String {
            val status = PacketDebugLogger.stop()
            DataManager.setPacketLoggingEnabled(false)
            return status
        }

        fun getPacketLoggingStatus(): String {
            return PacketDebugLogger.status()
        }

        fun openPacketLogFolder(): String {
            val dir = PacketDebugLogger.logDirectory().also { it.mkdirs() }
            return try {
                if (Desktop.isDesktopSupported()) {
                    Desktop.getDesktop().open(dir)
                } else {
                    Runtime.getRuntime().exec(arrayOf("explorer.exe", dir.absolutePath))
                }
                dir.absolutePath
            } catch (e: Exception) {
                logger.warn("패킷 로그 폴더 열기 실패: {}", dir.absolutePath, e)
                dir.absolutePath
            }
        }

        fun getStatsConsent(): String {
            return Json.encodeToString(StatsConsentManager.info(syncRemote = true, clientVersion = version))
        }

        fun setStatsConsent(state: String, uploadEnabled: Boolean, publicCharacter: Boolean): String {
            return Json.encodeToString(StatsConsentManager.set(state, uploadEnabled, publicCharacter, version))
        }

        fun getStatsOwnCharacter(): String {
            return Json.encodeToString(StatsPayloadBuilder.ownCharacter())
        }

        fun getStatsUploadStatus(): String {
            return Json.encodeToString(StatsUploadQueue.status())
        }

        fun openStatsUploadFolder(): String {
            return StatsUploadQueue.openFolder()
        }

        fun getBattleDetail(uid: Int): String {
            return Json.encodeToString(dpsCalculator.battleDetails(dpsCalculator.getLiveReport(), uid))
        }

        fun getBattleDetailFromList(idx: Int, uid: Int): String {
            return Json.encodeToString(DataManager.battleLog(idx)?.skillDetails?.get(uid) ?: emptyMap())
        }

        fun getBattleList(): String {
            return Json.encodeToString(DataManager.recentBattleList())
        }

        fun getLiveBuffOperatingRate(uid: Int): String {
            return Json.encodeToString(dpsCalculator.getLiveBuffOperatingRate(uid))
        }

        fun getBuffOperatingRate(idx: Int, uid: Int): String {
            return Json.encodeToString(DataManager.battleLog(idx)?.buffRates?.get(uid) ?: emptyList())
        }

        fun getLiveBossBuffOperatingRate(): String {
            return Json.encodeToString(dpsCalculator.getLiveBossBuffOperatingRate())
        }

        fun getBossBuffOperatingRate(idx: Int): String {
            return Json.encodeToString(DataManager.battleLog(idx)?.bossBuffRates ?: emptyList())
        }

        fun upload(idx: Int): Boolean {
            val log = DataManager.battleLog(idx) ?: return false
            return UploadManager.upload(log)
        }

        fun getVersion(): String {
            return version
        }

        fun startUpdate(msiUrl: String) {
            Thread {
                try {
                    val tempDir = java.io.File(System.getProperty("java.io.tmpdir"), "waffle_meter.v1.6.9-dev").also { it.mkdirs() }
                    val msiFile = java.io.File(tempDir, "waffle_meter_update.msi")

                    // HttpURLConnection 캐스트 제거: http(s) 외 file:// 등도 받을 수 있고(로컬 테스트), 사용하는 API는 모두 URLConnection 기본 메서드라 안전.
                    val connection = java.net.URI(msiUrl).toURL().openConnection()
                    connection.connect()
                    val totalBytes = connection.contentLengthLong

                    var downloadedBytes = 0L
                    connection.inputStream.use { input ->
                        java.io.FileOutputStream(msiFile).use { output ->
                            val buffer = ByteArray(8192)
                            var bytesRead: Int
                            while (input.read(buffer).also { bytesRead = it } != -1) {
                                output.write(buffer, 0, bytesRead)
                                downloadedBytes += bytesRead
                                if (totalBytes > 0) {
                                    val percent = (downloadedBytes * 100 / totalBytes).toInt()
                                    Platform.runLater {
                                        engine.executeScript("onDownloadProgress($percent)")
                                    }
                                }
                            }
                        }
                    }

                    Platform.runLater { engine.executeScript("onDownloadComplete()") }
                } catch (e: Exception) {
                    logger.error("업데이트 실패", e)
                    Platform.runLater { engine.executeScript("onDownloadError()") }
                }
            }.start()
        }

        // 다운로드된 MSI를 즉시 무인 설치하고 앱을 재시작한다("지금 재시작하여 적용").
        fun applyUpdate() {
            Thread {
                if (launchUpdaterProcess(relaunch = true)) {
                    Thread.sleep(600)
                    exitProcess(0)
                } else {
                    Platform.runLater { engine.executeScript("onDownloadError()") }
                }
            }.start()
        }

        // 백그라운드 다운로드 완료 시 호출: 다음 앱 종료 때 무인 설치되도록 예약한다.
        fun armUpdateOnExit() {
            updateReadyForExit = true
        }

        fun pushJoinRequest(data: JoinRequestUser) {
            engine.executeScript("onJoinRequest(${Json.encodeToString(data)})")
        }

        fun pushJoinRequestRemove(id: Int) {
            engine.executeScript("onJoinRequestRemove($id)")
        }

        fun pushExitPartyUI(){
            engine.executeScript("onExitPartyUI()")
        }

        fun pushRefuseJoinRequest(){
            engine.executeScript("onRefuseJoinRequest()")
        }


    }

    @Volatile
    private var dpsData: DpsReport = dpsCalculator.getDps()

    @Volatile
    private var cachedDpsJson: String = Json.encodeToString(dpsData)

    @Volatile
    private var isVisible = true

    @Volatile
    private var isAutoHide = PropertyHandler.getProperty("isAutoHide")?.toBooleanStrictOrNull() ?: true

    @Volatile
    private var aionEverFocused = false

    @Volatile
    private var isClickThrough = false

    @Volatile
    private var isOverlayParked = false

    private var overlayHwnd: WinDef.HWND? = null

    private val debugMode = false

    private val version = config.version


    override fun start(stage: Stage) {
        StatsUploadQueue.configure(version)
        Platform.setImplicitExit(false)
        stage.setOnCloseRequest {
            HotkeyHandler.stop()
            shutdown()
        }
        val webView = WebView()
        engine = webView.engine
        engine.load(javaClass.getResource("/dist/index.html")?.toExternalForm())

        val screenBounds = currentOverlayBounds()
        val bridge = JSBridge(stage, webView, hostServices)
        engine.loadWorker.stateProperty().addListener { _, _, newState ->
            if (newState == Worker.State.SUCCEEDED) {
                val window = engine.executeScript("window") as JSObject
                window.setMember("javaBridge", bridge)
            }
        }


        val scene = Scene(webView, screenBounds.width, screenBounds.height)
        scene.fill = Color.TRANSPARENT


        try {
            val pageField = engine.javaClass.getDeclaredField("page")
            pageField.isAccessible = true
            val page = pageField.get(engine)

            val setBgMethod = page.javaClass.getMethod("setBackgroundColor", Int::class.javaPrimitiveType)
            setBgMethod.isAccessible = true
            setBgMethod.invoke(page, 0)
        } catch (e: Exception) {
            logger.error("리플렉션 실패", e)
        }

        stage.initStyle(StageStyle.TRANSPARENT)
        stage.scene = scene
        stage.isAlwaysOnTop = false
        stage.title = "waffle_meter.v1.6.9-dev"
        fitOverlayToScreen(stage, webView)

        stage.show()
        fitOverlayToScreen(stage, webView)
        applyOverlayWindowStyle(stage.title)

        setupTray(stage)

        HotkeyHandler.registerCallback {
            Platform.runLater {
                // bridge.hardResetDps()
            }
        }
        HotkeyHandler.registerVisibilityCallback {
            if (isVisible) hideToTray(stage) else showFromTray(stage)
        }
        HotkeyHandler.registerClickThroughCallback {
            setClickThrough(!isClickThrough)
        }
        HotkeyHandler.start()
        
        
        CoroutineScope(Dispatchers.IO).launch {
            PacketEventBus.events.collect { event ->
                Platform.runLater {
                    when (event) {
                        is PacketEvent.JoinRequest -> bridge.pushJoinRequest(event.user)
                        is PacketEvent.JoinRequestRemove -> bridge.pushJoinRequestRemove(event.id)
                        is PacketEvent.ExitPartyUI -> bridge.pushExitPartyUI()
                        is PacketEvent.RefuseJoinRequest -> bridge.pushRefuseJoinRequest()
                    }
                }
            }
        }
        
        
        CoroutineScope(Dispatchers.IO).launch {
            while (true) {
                kotlinx.coroutines.delay(dpsRefreshMs)
                val data = dpsCalculator.getDps()
                cachedDpsJson = Json.encodeToString(data)
                dpsData = data
            }
        }

        CoroutineScope(Dispatchers.IO).launch {
            while (true) {
                kotlinx.coroutines.delay(overlayFocusCheckMs)
                if (!isVisible) continue
                val aionFocused = isAion2Focused()
                val selfFocused = isSelfFocused()
                if (!isAutoHide) {
                    Platform.runLater { presentOverlay(stage, aionFocused) }
                    continue
                }

                if (!aionEverFocused) {
                    if (aionFocused) aionEverFocused = true
                    else continue
                }

                val shouldShow = aionFocused || selfFocused
                Platform.runLater {
                    if (shouldShow) {
                        presentOverlay(stage, aionFocused)
                    } else {
                        parkOverlay(stage)
                    }
                }
            }
        }
    }

    private fun presentOverlay(stage: Stage, topMost: Boolean = true) {
        if (stage.isAlwaysOnTop != topMost) {
            stage.isAlwaysOnTop = topMost
        }
        if (stage.opacity != 1.0) {
            stage.opacity = 1.0
        }
        syncOverlayInputStyle("present")
        restoreOverlayPriority(stage, topMost)
        isOverlayParked = false
    }

    private fun parkOverlay(stage: Stage) {
        if (stage.opacity != 0.0) {
            stage.opacity = 0.0
        }
        if (stage.isAlwaysOnTop) {
            stage.isAlwaysOnTop = false
        }
        if (!isOverlayParked) {
            parkOverlayNative(stage)
            isOverlayParked = true
        }
        // alt-tab/포커스 변경 등으로 JavaFX 가 네이티브 ex-style 을 리셋하면 WS_EX_TOOLWINDOW 가 빠져
        // 간헐적으로 작업표시줄에 뜬다. present 만 스타일을 재적용했고 park 경로는 안 했던 게 원인 →
        // park(자동 숨김)에서도 매 사이클 재적용해 항상 작업표시줄에서 제외(스타일 불변이면 no-op).
        syncOverlayInputStyle("park")
    }

    private fun primaryScreenBounds(): Rectangle2D {
        return Screen.getPrimary()?.bounds ?: Rectangle2D(0.0, 0.0, 1920.0, 1080.0)
    }

    private fun virtualScreenBounds(): Rectangle2D {
        val screens = Screen.getScreens()
        if (screens.isNullOrEmpty()) return primaryScreenBounds()

        val minX = screens.minOf { it.bounds.minX }
        val minY = screens.minOf { it.bounds.minY }
        val maxX = screens.maxOf { it.bounds.maxX }
        val maxY = screens.maxOf { it.bounds.maxY }
        return Rectangle2D(minX, minY, maxX - minX, maxY - minY)
    }

    private fun isMultiMonitorMode(): Boolean {
        return PropertyHandler.getProperty("multiMonitorMode")?.toBooleanStrictOrNull() ?: false
    }

    private fun currentOverlayBounds(): Rectangle2D {
        return if (isMultiMonitorMode()) virtualScreenBounds() else primaryScreenBounds()
    }

    // 작은 창(meterOnly) 위치를 화면 안으로 clamp. 창 전체가 보이도록 x∈[minX, maxX-w], y∈[minY, maxY-h].
    // 다중 모니터 모드면 가상 화면 전체 기준(다른 모니터로 이동 가능), 아니면 주 화면 기준(주 화면 밖으로 못 나감).
    private fun clampToOverlayBounds(x: Double, y: Double, w: Double, h: Double): Pair<Double, Double> {
        val b = currentOverlayBounds()
        val cx = x.coerceIn(b.minX, (b.maxX - w).coerceAtLeast(b.minX))
        val cy = y.coerceIn(b.minY, (b.maxY - h).coerceAtLeast(b.minY))
        return cx to cy
    }

    private fun fitOverlayToScreen(stage: Stage, webView: WebView): String {
        val previousX = stage.x
        val previousY = stage.y
        val bounds = currentOverlayBounds()
        stage.x = bounds.minX
        stage.y = bounds.minY
        stage.width = bounds.width
        stage.height = bounds.height
        webView.minWidth = bounds.width
        webView.minHeight = bounds.height
        webView.prefWidth = bounds.width
        webView.prefHeight = bounds.height
        webView.maxWidth = bounds.width
        webView.maxHeight = bounds.height
        val safePreviousX = if (previousX.isFinite()) previousX else bounds.minX
        val safePreviousY = if (previousY.isFinite()) previousY else bounds.minY
        return """{"offsetX":${safePreviousX - bounds.minX},"offsetY":${safePreviousY - bounds.minY},"width":${bounds.width},"height":${bounds.height},"multiMonitor":${isMultiMonitorMode()}}"""
    }

    private fun isSelfFocused(): Boolean {
        val hwnd = User32.INSTANCE.GetForegroundWindow() ?: return false
        val pidRef = com.sun.jna.ptr.IntByReference()
        User32.INSTANCE.GetWindowThreadProcessId(hwnd, pidRef)
        return pidRef.value.toLong() == ProcessHandle.current().pid()
    }

    private fun isAion2Focused(): Boolean {
        val hwnd = User32.INSTANCE.GetForegroundWindow() ?: return false
        val pidRef = com.sun.jna.ptr.IntByReference()
        User32.INSTANCE.GetWindowThreadProcessId(hwnd, pidRef)
        val foregroundPid = pidRef.value.toLong()

        val hProcess = Kernel32.INSTANCE.OpenProcess(WinNT.PROCESS_QUERY_LIMITED_INFORMATION, false, foregroundPid.toInt())
            ?: return false
        return try {
            val buf = com.sun.jna.Memory(2048)
            Psapi.INSTANCE.GetModuleFileNameEx(hProcess, null, buf, 1024)
            val exePath = buf.getWideString(0)
            exePath.endsWith("Aion2.exe", ignoreCase = true)
        } finally {
            Kernel32.INSTANCE.CloseHandle(hProcess)
        }
    }


    private fun applyOverlayWindowStyle(title: String) {
        val user32 = User32.INSTANCE
        val hwnd = user32.FindWindow(null, title) ?: return
        overlayHwnd = hwnd
        syncOverlayInputStyle("apply")
        user32.SetWindowPos(hwnd, null, 0, 0, 0, 0,
            SWP_NOMOVE or SWP_NOSIZE or SWP_NOZORDER or SWP_NOACTIVATE or SWP_FRAMECHANGED)
    }

    private fun setClickThrough(enable: Boolean) {
        isClickThrough = enable
        syncOverlayInputStyle("toggle")
        Platform.runLater {
            engine.executeScript("onClickThroughChanged($enable)")
        }
    }

    private fun syncOverlayInputStyle(reason: String) {
        val hwnd = overlayHwnd ?: return
        val user32 = User32.INSTANCE
        val exStyle = user32.GetWindowLong(hwnd, GWL_EXSTYLE)
        val baseStyle = (exStyle or WS_EX_TOOLWINDOW or WS_EX_LAYERED or WS_EX_NOACTIVATE) and WS_EX_APPWINDOW.inv()
        val newStyle = if (isClickThrough) {
            baseStyle or WS_EX_TRANSPARENT
        } else {
            baseStyle and WS_EX_TRANSPARENT.inv()
        }
        if (newStyle == exStyle) return

        user32.SetWindowLong(hwnd, GWL_EXSTYLE, newStyle)
        user32.SetWindowPos(
            hwnd,
            null,
            0,
            0,
            0,
            0,
            SWP_NOMOVE or SWP_NOSIZE or SWP_NOZORDER or SWP_NOACTIVATE or SWP_FRAMECHANGED
        )
        logger.info(
            "오버레이 입력 스타일 동기화: reason={}, clickThrough={}, transparent={}",
            reason,
            isClickThrough,
            (newStyle and WS_EX_TRANSPARENT) != 0
        )
    }

    private fun restoreOverlayPriority(stage: Stage, topMost: Boolean) {
        val hwnd = overlayHwnd ?: User32.INSTANCE.FindWindow(null, stage.title)?.also { overlayHwnd = it } ?: return
        User32.INSTANCE.SetWindowPos(
            hwnd,
            if (topMost) HWND_TOPMOST else HWND_TOP,
            0,
            0,
            0,
            0,
            SWP_NOMOVE or SWP_NOSIZE or SWP_NOACTIVATE or SWP_SHOWWINDOW
        )
    }

    @Volatile
    private var updateReadyForExit = false

    // 다운로드된 업데이트 MSI를 외부 PowerShell로 무인 설치(per-user, /qb, UAC 불필요)한다.
    // 실행 중인 JVM이 파일을 잠그므로 현재 PID 종료를 기다린 뒤 msiexec를 돌린다. relaunch=true면 설치 후 앱 재실행.
    // 적용할 MSI가 없으면 false.
    private fun launchUpdaterProcess(relaunch: Boolean): Boolean {
        val tempDir = java.io.File(System.getProperty("java.io.tmpdir"), "waffle_meter.v1.6.9-dev")
        val msiFile = java.io.File(tempDir, "waffle_meter_update.msi")
        if (!msiFile.isFile) {
            logger.warn("적용할 업데이트 MSI를 찾을 수 없습니다: {}", msiFile.absolutePath)
            return false
        }
        val pid = ProcessHandle.current().pid()
        val msiArg = "'" + msiFile.absolutePath.replace("'", "''") + "'"
        val appExe = (System.getProperty("jpackage.app-path")
            ?: ProcessHandle.current().info().command().orElse(null))
            ?.takeIf { it.isNotBlank() && java.io.File(it).exists() }
        val script = buildString {
            appendLine("try { Wait-Process -Id $pid -Timeout 120 -ErrorAction SilentlyContinue } catch {}")
            appendLine("Start-Sleep -Milliseconds 1000")
            appendLine("\$p = Start-Process 'msiexec.exe' -ArgumentList @('/i', $msiArg, '/qb', '/norestart') -PassThru -Wait")
            if (relaunch && appExe != null) {
                val relArg = "'" + appExe.replace("'", "''") + "'"
                appendLine("if (\$p.ExitCode -eq 0 -or \$p.ExitCode -eq 3010) { Start-Process -FilePath $relArg }")
            }
        }
        val encoded = java.util.Base64.getEncoder().encodeToString(script.toByteArray(Charsets.UTF_16LE))
        ProcessBuilder(
            "powershell.exe", "-NoProfile", "-WindowStyle", "Hidden",
            "-ExecutionPolicy", "Bypass", "-EncodedCommand", encoded
        ).start()
        return true
    }

    // 모든 종료 경로의 단일 출구: 대기 중인 업데이트가 있으면 설치(재실행 없음)를 띄운 뒤 종료한다.
    private fun shutdown() {
        try {
            if (updateReadyForExit) launchUpdaterProcess(relaunch = false)
        } catch (e: Exception) {
            logger.warn("종료 시 업데이트 적용 실패", e)
        }
        Platform.exit()
        exitProcess(0)
    }

    private fun parkOverlayNative(stage: Stage) {
        val hwnd = overlayHwnd ?: User32.INSTANCE.FindWindow(null, stage.title)?.also { overlayHwnd = it } ?: return
        User32.INSTANCE.SetWindowPos(
            hwnd,
            HWND_NOTOPMOST,
            0,
            0,
            0,
            0,
            SWP_NOMOVE or SWP_NOSIZE or SWP_NOACTIVATE
        )
        User32.INSTANCE.SetWindowPos(
            hwnd,
            HWND_BOTTOM,
            0,
            0,
            0,
            0,
            SWP_NOMOVE or SWP_NOSIZE or SWP_NOACTIVATE
        )
    }

    private fun setupTray(stage: Stage) {
        if (!SystemTray.isSupported()) return
        EventQueue.invokeLater {
            try {
                val tray = SystemTray.getSystemTray()
                val iconUrl = javaClass.getResource("/icons/waffle.png")
                val image = if (iconUrl != null) {
                    ImageIO.read(iconUrl)
                } else {
                    java.awt.image.BufferedImage(16, 16, java.awt.image.BufferedImage.TYPE_INT_ARGB)
                }

                val popup = PopupMenu()
                val showItem = MenuItem("보이기/숨기기")
                showItem.addActionListener { if (isVisible) hideToTray(stage) else showFromTray(stage) }
                val recoverInputItem = MenuItem("오버레이 입력 복구")
                recoverInputItem.addActionListener {
                    Platform.runLater {
                        setClickThrough(false)
                        presentOverlay(stage)
                    }
                }
                val exitItem = MenuItem("종료")
                exitItem.addActionListener {
                    tray.remove(trayIcon)
                    shutdown()
                }
                popup.add(showItem)
                popup.add(recoverInputItem)
                popup.addSeparator()
                popup.add(exitItem)

                trayIcon = TrayIcon(image, "waffle_meter.v1.6.9-dev", popup).apply {
                    isImageAutoSize = true
                    addMouseListener(object : MouseAdapter() {
                        override fun mouseClicked(e: MouseEvent) {
                            if (e.button == MouseEvent.BUTTON1) {
                                if (isVisible) hideToTray(stage) else showFromTray(stage)
                            }
                        }
                    })
                }
                tray.add(trayIcon)
            } catch (e: AWTException) {
                logger.error("트레이 설정 실패", e)
            }
        }
    }

    private fun hideToTray(stage: Stage) {
        isVisible = false
        Platform.runLater { parkOverlay(stage) }
    }

    private fun showFromTray(stage: Stage) {
        isVisible = true
        aionEverFocused = false
        Platform.runLater {
            presentOverlay(stage)
        }
    }

}

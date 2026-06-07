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
            Platform.exit()
            exitProcess(0)
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
                    val tempDir = java.io.File(System.getProperty("java.io.tmpdir"), "waffle_meter.v1.6").also { it.mkdirs() }
                    val msiFile = java.io.File(tempDir, "waffle_meter_update.msi")

                    val connection = java.net.URI(msiUrl).toURL().openConnection() as java.net.HttpURLConnection
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

                    Runtime.getRuntime().exec(arrayOf("explorer.exe", tempDir.absolutePath))
                    Platform.runLater { engine.executeScript("onDownloadComplete()") }
                } catch (e: Exception) {
                    logger.error("업데이트 실패", e)
                    Platform.runLater { engine.executeScript("onDownloadError()") }
                }
            }.start()
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
            exitProcess(0)
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
        stage.title = "waffle_meter.v1.6"
        fitOverlayToScreen(stage, webView)

        stage.show()
        fitOverlayToScreen(stage, webView)
        applyOverlayWindowStyle(stage.title)
        ensureUserDesktopShortcut(stage.title)

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
        val baseStyle = (exStyle or WS_EX_TOOLWINDOW or WS_EX_LAYERED) and WS_EX_APPWINDOW.inv()
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

    private fun ensureUserDesktopShortcut(title: String) {
        val exePath = findLauncherExe(title) ?: return
        val targetPath = psSingleQuoted(exePath)
        val shortcutName = psSingleQuoted("$title.lnk")
        val script = """
            ${'$'}ErrorActionPreference = 'Stop'
            ${'$'}targetPath = $targetPath
            ${'$'}shortcutName = $shortcutName
            ${'$'}paths = New-Object System.Collections.Generic.List[string]
            function Add-DesktopPath([string]${'$'}path) {
              if (![string]::IsNullOrWhiteSpace(${'$'}path)) { ${'$'}paths.Add(${'$'}path) }
            }
            ${'$'}knownDesktop = [Environment]::GetFolderPath('Desktop')
            Add-DesktopPath ${'$'}knownDesktop
            ${'$'}profile = [Environment]::GetFolderPath('UserProfile')
            if (![string]::IsNullOrWhiteSpace(${'$'}profile)) {
              Add-DesktopPath (Join-Path ${'$'}profile 'Desktop')
              Add-DesktopPath (Join-Path ${'$'}profile '바탕 화면')
              Add-DesktopPath (Join-Path ${'$'}profile 'OneDrive\Desktop')
              Add-DesktopPath (Join-Path ${'$'}profile 'OneDrive\바탕 화면')
            }
            try {
              ${'$'}registryDesktop = (Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders' -ErrorAction Stop).Desktop
              Add-DesktopPath ([Environment]::ExpandEnvironmentVariables(${'$'}registryDesktop))
            } catch {
            }
            ${'$'}shell = New-Object -ComObject WScript.Shell
            ${'$'}workingDirectory = Split-Path -Parent ${'$'}targetPath
            ${'$'}paths |
              Where-Object { ![string]::IsNullOrWhiteSpace(${'$'}_) } |
              Select-Object -Unique |
              ForEach-Object {
                ${'$'}desktop = ${'$'}_
                if (Test-Path -LiteralPath ${'$'}desktop) {
                  ${'$'}shortcutPath = Join-Path ${'$'}desktop ${'$'}shortcutName
                  ${'$'}shortcut = ${'$'}shell.CreateShortcut(${'$'}shortcutPath)
                  ${'$'}shortcut.TargetPath = ${'$'}targetPath
                  ${'$'}shortcut.WorkingDirectory = ${'$'}workingDirectory
                  ${'$'}shortcut.IconLocation = "${'$'}targetPath,0"
                  ${'$'}shortcut.Save()
                }
              }
        """.trimIndent()

        runCatching {
            val encoded = java.util.Base64.getEncoder()
                .encodeToString(script.toByteArray(Charsets.UTF_16LE))
            val process = ProcessBuilder(
                "powershell.exe",
                "-NoProfile",
                "-WindowStyle",
                "Hidden",
                "-ExecutionPolicy",
                "Bypass",
                "-EncodedCommand",
                encoded
            ).start()
            if (!process.waitFor(5, java.util.concurrent.TimeUnit.SECONDS)) {
                process.destroyForcibly()
                logger.warn("사용자 바탕화면 바로가기 보강 PowerShell 시간이 초과되었습니다.")
            } else if (process.exitValue() != 0) {
                val error = process.errorStream.bufferedReader().readText().trim()
                logger.warn("사용자 바탕화면 바로가기 보강 PowerShell 실패: exitCode={}, error={}", process.exitValue(), error)
            }
        }.onFailure {
            logger.warn("사용자 바탕화면 바로가기 보강 실패", it)
        }
    }

    private fun psSingleQuoted(value: String): String = "'${value.replace("'", "''")}'"

    private fun findLauncherExe(title: String): String? {
        val currentCommand = ProcessHandle.current().info().command().orElse(null)
        if (currentCommand?.endsWith(".exe", ignoreCase = true) == true) {
            val fileName = java.io.File(currentCommand).name
            if (!fileName.equals("java.exe", ignoreCase = true) && !fileName.equals("javaw.exe", ignoreCase = true)) {
                return currentCommand
            }
        }

        val javaHome = java.io.File(System.getProperty("java.home") ?: "")
        val userDir = java.io.File(System.getProperty("user.dir") ?: "")
        val searchDirs = listOfNotNull(
            currentCommand?.let { java.io.File(it).parentFile },
            userDir,
            userDir.parentFile,
            javaHome.parentFile,
            javaHome.parentFile?.parentFile
        ).filter { it.isDirectory }
            .distinctBy { it.absolutePath.lowercase() }

        val exactName = "$title.exe"
        for (dir in searchDirs) {
            val exact = java.io.File(dir, exactName)
            if (exact.isFile) return exact.absolutePath
            dir.listFiles { file ->
                file.isFile &&
                    file.extension.equals("exe", ignoreCase = true) &&
                    file.name.startsWith("waffle_meter", ignoreCase = true)
            }?.firstOrNull()?.let { return it.absolutePath }
        }

        logger.warn("사용자 바탕화면 바로가기 대상 실행 파일을 찾지 못했습니다. command={}, javaHome={}", currentCommand, javaHome.absolutePath)
        return null
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
                    Platform.exit()
                    exitProcess(0)
                }
                popup.add(showItem)
                popup.add(recoverInputItem)
                popup.addSeparator()
                popup.add(exitItem)

                trayIcon = TrayIcon(image, "waffle_meter.v1.6", popup).apply {
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

package com.tbread.config

import com.sun.jna.platform.win32.User32
import com.sun.jna.platform.win32.WinUser
import org.slf4j.LoggerFactory

object HotkeyHandler {
    private val logger = LoggerFactory.getLogger(HotkeyHandler::class.java)
    private const val propertyKey = "hotkey"
    private const val visibilityPropertyKey = "hideHotkey"

    private const val RESET_HOTKEY_ID = 1
    private const val VISIBILITY_HOTKEY_ID = 2
    private const val CLICK_THROUGH_HOTKEY_ID = 3

    private val defaultHotkey = HotkeyCombo(modifiers = WinUser.MOD_CONTROL, vkCode = 0x52)
    private val defaultVisibilityHotkey = HotkeyCombo(modifiers = WinUser.MOD_CONTROL, vkCode = 0x48) // Ctrl+H
    private val defaultClickThroughHotkey = HotkeyCombo(modifiers = WinUser.MOD_CONTROL, vkCode = 0x54) // Ctrl+T

    @Volatile
    private var currentHotkey: HotkeyCombo = loadHotkeyFromProperties()

    @Volatile
    private var visibilityHotkey: HotkeyCombo = loadVisibilityHotkeyFromProperties()

    @Volatile
    private var clickThroughHotkey: HotkeyCombo = loadClickThroughHotkeyFromProperties()

    private var listenerThread: Thread? = null

    @Volatile
    private var running = false

    private var onHotkeyPressed: (() -> Unit)? = null
    private var onVisibilityHotkeyPressed: (() -> Unit)? = null
    private var onClickThroughHotkeyPressed: (() -> Unit)? = null

    fun registerCallback(callback: () -> Unit) {
        onHotkeyPressed = callback
    }

    fun registerVisibilityCallback(callback: () -> Unit) {
        onVisibilityHotkeyPressed = callback
    }

    fun registerClickThroughCallback(callback: () -> Unit) {
        onClickThroughHotkeyPressed = callback
    }

    data class HotkeyCombo(val modifiers: Int, val vkCode: Int) {
        override fun toString() = "modifiers=$modifiers,vkCode=$vkCode"

        companion object {
            private val logger = LoggerFactory.getLogger(HotkeyCombo::class.java)
            fun fromString(s: String): HotkeyCombo? {
                return try {
                    val map = s.split(",").associate {
                        val (k, v) = it.split("=")
                        k.trim() to v.trim().toInt()
                    }
                    HotkeyCombo(map["modifiers"]!!, map["vkCode"]!!)
                } catch (e: Exception) {
                    logger.warn("문자열 파싱실패로 단축키 초기화")
                    null
                }
            }
        }
    }

    private fun loadHotkeyFromProperties(): HotkeyCombo {
        val raw = PropertyHandler.getProperty(propertyKey)
        return if (raw != null) HotkeyCombo.fromString(raw) ?: defaultHotkey else defaultHotkey
    }

    private fun loadVisibilityHotkeyFromProperties(): HotkeyCombo {
        val raw = PropertyHandler.getProperty(visibilityPropertyKey)
        return if (raw != null) HotkeyCombo.fromString(raw) ?: defaultVisibilityHotkey else defaultVisibilityHotkey
    }

    private fun loadClickThroughHotkeyFromProperties(): HotkeyCombo {
        val raw = PropertyHandler.getProperty("clickThroughHotkey")
        return if (raw != null) HotkeyCombo.fromString(raw) ?: defaultClickThroughHotkey else defaultClickThroughHotkey
    }

    fun getCurrentHotkey(): HotkeyCombo = currentHotkey

    fun getVisibilityHotkey(): HotkeyCombo = visibilityHotkey

    fun getClickThroughHotkey(): HotkeyCombo = clickThroughHotkey

    fun updateClickThroughHotkey(modifiers: Int, vkCode: Int) {
        clickThroughHotkey = HotkeyCombo(modifiers, vkCode)
        PropertyHandler.setProperty("clickThroughHotkey", clickThroughHotkey.toString())
        logger.info("클릭 통과 단축키 변경: $clickThroughHotkey")
        stop()
        start()
    }

    fun updateHotkey(modifiers: Int, vkCode: Int) {
        currentHotkey = HotkeyCombo(modifiers, vkCode)
        PropertyHandler.setProperty(propertyKey, currentHotkey.toString())
        logger.info("단축키 변경: $currentHotkey")

        stop()
        start()
    }

    fun updateVisibilityHotkey(modifiers: Int, vkCode: Int) {
        visibilityHotkey = HotkeyCombo(modifiers, vkCode)
        PropertyHandler.setProperty(visibilityPropertyKey, visibilityHotkey.toString())
        logger.info("숨기기 단축키 변경: $visibilityHotkey")

        stop()
        start()
    }

    fun start() {
        if (running) return
        running = true
        listenerThread = Thread({
            val user32 = User32.INSTANCE

            val registeredReset = user32.RegisterHotKey(null, RESET_HOTKEY_ID, currentHotkey.modifiers, currentHotkey.vkCode)
            if (!registeredReset) logger.error("전투 초기화 단축키 등록 실패: $currentHotkey")
            else logger.info("전투 초기화 단축키 등록 성공: $currentHotkey")

            val registeredVisibility = user32.RegisterHotKey(null, VISIBILITY_HOTKEY_ID, visibilityHotkey.modifiers, visibilityHotkey.vkCode)
            if (!registeredVisibility) logger.error("숨기기 단축키 등록 실패: $visibilityHotkey")
            else logger.info("숨기기 단축키 등록 성공: $visibilityHotkey")

            val registeredClickThrough = user32.RegisterHotKey(null, CLICK_THROUGH_HOTKEY_ID, clickThroughHotkey.modifiers, clickThroughHotkey.vkCode)
            if (!registeredClickThrough) logger.error("클릭 통과 단축키 등록 실패: $clickThroughHotkey")
            else logger.info("클릭 통과 단축키 등록 성공: $clickThroughHotkey")

            if (!registeredReset && !registeredVisibility && !registeredClickThrough) {
                running = false
                return@Thread
            }

            val msg = WinUser.MSG()
            while (running) {
                if (user32.PeekMessage(msg, null, 0, 0, 1)) {
                    if (msg.message == WinUser.WM_HOTKEY) {
                        when (msg.wParam.toInt()) {
                            RESET_HOTKEY_ID -> onHotkeyPressed?.invoke()
                            VISIBILITY_HOTKEY_ID -> onVisibilityHotkeyPressed?.invoke()
                            CLICK_THROUGH_HOTKEY_ID -> onClickThroughHotkeyPressed?.invoke()
                        }
                    }
                } else {
                    try {
                        Thread.sleep(10)
                    } catch (e: InterruptedException) {
                        Thread.currentThread().interrupt()
                    }
                }
            }

            user32.UnregisterHotKey(null, RESET_HOTKEY_ID)
            user32.UnregisterHotKey(null, VISIBILITY_HOTKEY_ID)
            user32.UnregisterHotKey(null, CLICK_THROUGH_HOTKEY_ID)
            logger.info("단축키 해제")
        }, "HotkeyListener").apply { isDaemon = true }

        listenerThread!!.start()
    }
    fun stop() {
        running = false
        listenerThread?.interrupt()
        listenerThread?.join()
        listenerThread = null
    }
}

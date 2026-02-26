package com.tbread.config

import com.sun.jna.platform.win32.User32
import com.sun.jna.platform.win32.WinDef
import com.sun.jna.platform.win32.WinUser
import com.tbread.packet.PropertyHandler
import org.slf4j.LoggerFactory

object HotkeyHandler {
    private val logger = LoggerFactory.getLogger(HotkeyHandler::class.java)
    private const val propertyKey = "hotkey"

    private val defaultHotkey = HotkeyCombo(modifiers = WinUser.MOD_CONTROL, vkCode = 0x52)

    @Volatile
    private var currentHotkey: HotkeyCombo = loadHotkeyFromProperties()
    

    data class HotkeyCombo(val modifiers: Int, val vkCode: Int) {
        override fun toString() = "modifiers=$modifiers,vkCode=$vkCode"

        companion object {
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

}
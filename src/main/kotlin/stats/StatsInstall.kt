package com.tbread.stats

import com.tbread.config.PropertyHandler
import java.util.UUID

object StatsInstall {
    private const val KEY_INSTALL_ID = "statsInstallId"

    fun installId(): String {
        val saved = PropertyHandler.getProperty(KEY_INSTALL_ID)?.takeIf { it.isNotBlank() }
        if (saved != null) return saved
        val generated = UUID.randomUUID().toString()
        PropertyHandler.setProperty(KEY_INSTALL_ID, generated)
        return generated
    }
}

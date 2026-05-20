package com.tbread.config

import org.slf4j.LoggerFactory
import java.io.*
import java.util.*

object PropertyHandler {
    private val props = Properties()
    private const val APP_NAME = "waffle_meter.v1.4"
    private val LEGACY_APP_NAMES = listOf("waffle_meter.v1.3", "waffle_meter.v1.2")
    private const val SETTING_PROPERTY_FILE_NAME = "settings.properties"
    private const val VERSION_PROPERTY_FILE_NAME = "version.properties"
    private val logger = LoggerFactory.getLogger(PropertyHandler::class.java)

    private val settingFile: File = run {
        val appData = System.getenv("APPDATA") ?: System.getProperty("user.home")
        val dir = File(appData, APP_NAME)
        dir.mkdirs()
        val nextSettingFile = File(dir, SETTING_PROPERTY_FILE_NAME)
        if (!nextSettingFile.exists()) {
            for (legacyAppName in LEGACY_APP_NAMES) {
                val legacySettingFile = File(File(appData, legacyAppName), SETTING_PROPERTY_FILE_NAME)
                if (legacySettingFile.exists()) {
                    runCatching {
                        legacySettingFile.copyTo(nextSettingFile, overwrite = false)
                    }.onFailure {
                        logger.warn("이전 설정파일 복사에 실패했습니다.")
                    }
                    break
                }
            }
        }
        nextSettingFile
    }

    init {
        loadSettings()
        loadProperties(VERSION_PROPERTY_FILE_NAME, false)
    }

    private fun loadSettings() {
        try {
            if (settingFile.exists()) {
                FileInputStream(settingFile).use { fis ->
                    props.load(fis)
                }
            } else {
                logger.info("설정파일이 존재하지 않아 파일을 생성합니다. 경로: ${settingFile.absolutePath}")
                settingFile.createNewFile()
            }
        } catch (e: IOException) {
            logger.error("설정파일 읽기에 실패했습니다.")
        }
    }

    private fun loadProperties(fname: String, outerFlag: Boolean) {
        try {
            if (outerFlag) {
                FileInputStream(fname).use { fis ->
                    props.load(fis)
                }
            } else {
                object {}.javaClass.getResourceAsStream("/$fname")?.use {
                    props.load(it)
                }
            }
        } catch (e: FileNotFoundException) {
            logger.info("설정파일이 존재하지 않아 파일을 생성합니다.")
            FileOutputStream(fname).use {}
        } catch (e: IOException) {
            logger.error("설정파일 읽기에 실패했습니다.")
        }
    }

    private fun encodeToEucKr(key: String?): String? {
        if (key == null) return null
        return try {
            String(key.toByteArray(Charsets.ISO_8859_1), charset("EUC-KR"))
        } catch (e: UnsupportedEncodingException) {
            key
        }
    }

    private fun save() {
        FileOutputStream(settingFile).use { fos ->
            props.store(fos, "settings")
        }
    }

    fun appDirectory(): File {
        return settingFile.parentFile
    }

    fun getProperty(key: String): String? {
        return encodeToEucKr(props.getProperty(key))
    }

    fun getProperty(key: String, defaultValue: String): String? {
        return encodeToEucKr(props.getProperty(key, defaultValue))
    }

    fun setProperty(key: String, value: String) {
        props.setProperty(key, value)
        save()
    }


}

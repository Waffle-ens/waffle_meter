package com.tbread.config

import org.slf4j.LoggerFactory

data class VersionConfig(val version:String) {
    companion object {
        private val logger = LoggerFactory.getLogger(javaClass.enclosingClass)
        fun loadFromProperties(): VersionConfig {
            val version = PropertyHandler.getProperty("version") ?: "1.5.0"
            logger.info("프로퍼티스 초기화 완료")
            return VersionConfig(version)
        }
    }
}

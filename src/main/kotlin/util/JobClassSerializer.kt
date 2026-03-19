package com.tbread.util

import com.tbread.entity.enums.JobClass
import kotlinx.serialization.KSerializer
import kotlinx.serialization.descriptors.PrimitiveKind
import kotlinx.serialization.descriptors.PrimitiveSerialDescriptor
import kotlinx.serialization.encoding.Decoder
import kotlinx.serialization.encoding.Encoder

object JobClassSerializer : KSerializer<JobClass> {
    override val descriptor = PrimitiveSerialDescriptor("JobClass", PrimitiveKind.STRING)

    override fun serialize(encoder: Encoder, value: JobClass) {
        encoder.encodeString(value.className)  // "검성" 으로 직렬화
    }

    override fun deserialize(decoder: Decoder): JobClass {
        val className = decoder.decodeString()
        return JobClass.entries.first { it.className == className }
    }
}
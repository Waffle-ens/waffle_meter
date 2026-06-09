namespace WaffleMeter.Capture;

/// <summary>
/// Result of reading a LEB128-style little-endian varint.
/// <c>(-1, -1)</c> signals out-of-bounds or 32-bit overflow.
/// Mirrors Kotlin <c>StreamProcessor.VarIntOutput</c> (StreamProcessor.kt:19).
/// </summary>
public readonly record struct VarIntOutput(int Value, int Length);

/// <summary>
/// Verbatim ports of the byte-reading primitives in Kotlin <c>StreamProcessor</c>
/// (readVarInt / parseUInt16le / parseUInt32le / readUInt32leAsLong / readUInt64le / toHex).
/// CORRECTNESS-CRITICAL: the <c>-1</c> sentinels and the exact OOB / 32-bit-overflow behavior
/// must match Kotlin so the framing and parse layers reject identically. Signed-int results are
/// intentional (Kotlin returns <see cref="int"/>); downstream guards rely on the sign.
/// </summary>
public static class PacketPrimitives
{
    /// <summary>Port of <c>StreamProcessor.readVarInt</c> (StreamProcessor.kt:748-779).</summary>
    public static VarIntOutput ReadVarInt(byte[] bytes, int offset = 0)
    {
        int value = 0;
        int shift = 0;
        int count = 0;

        while (true)
        {
            if (offset + count >= bytes.Length)
            {
                return new VarIntOutput(-1, -1);   // out of bounds
            }

            int byteVal = bytes[offset + count] & 0xff;
            count++;

            value |= (byteVal & 0x7F) << shift;

            if ((byteVal & 0x80) == 0)
            {
                return new VarIntOutput(value, count);
            }

            shift += 7;
            if (shift >= 32)
            {
                return new VarIntOutput(-1, -1);   // 32-bit overflow
            }
        }
    }

    /// <summary>Port of <c>parseUInt16le</c> (StreamProcessor.kt:556).</summary>
    public static int ParseUInt16Le(byte[] packet, int offset = 0)
        => (packet[offset] & 0xff) | ((packet[offset + 1] & 0xff) << 8);

    /// <summary>Port of <c>parseUInt32le</c> (StreamProcessor.kt:560). Returns a signed int, as Kotlin does.</summary>
    public static int ParseUInt32Le(byte[] packet, int offset = 0)
    {
        if (offset + 4 > packet.Length)
        {
            throw new ArgumentException("패킷 길이가 필요길이보다 짧음");
        }

        return (packet[offset] & 0xFF)
             | ((packet[offset + 1] & 0xFF) << 8)
             | ((packet[offset + 2] & 0xFF) << 16)
             | ((packet[offset + 3] & 0xFF) << 24);
    }

    /// <summary>Port of <c>readUInt32leAsLong</c> (StreamProcessor.kt:568).</summary>
    public static long ReadUInt32LeAsLong(byte[] packet, int offset = 0)
    {
        if (offset + 4 > packet.Length)
        {
            throw new ArgumentException("패킷 길이가 필요길이보다 짧음");
        }

        return ((long)(packet[offset] & 0xFF))
             | ((long)(packet[offset + 1] & 0xFF) << 8)
             | ((long)(packet[offset + 2] & 0xFF) << 16)
             | ((long)(packet[offset + 3] & 0xFF) << 24);
    }

    /// <summary>Port of <c>readUInt64le</c> (StreamProcessor.kt:576).</summary>
    public static long ReadUInt64Le(byte[] packet, int offset = 0)
    {
        if (offset + 8 > packet.Length)
        {
            throw new ArgumentException("패킷 길이가 필요길이보다 짧음");
        }

        return ((long)(packet[offset] & 0xFF))
             | ((long)(packet[offset + 1] & 0xFF) << 8)
             | ((long)(packet[offset + 2] & 0xFF) << 16)
             | ((long)(packet[offset + 3] & 0xFF) << 24)
             | ((long)(packet[offset + 4] & 0xFF) << 32)
             | ((long)(packet[offset + 5] & 0xFF) << 40)
             | ((long)(packet[offset + 6] & 0xFF) << 48)
             | ((long)(packet[offset + 7] & 0xFF) << 56);
    }

    /// <summary>Port of <c>toHex</c> (StreamProcessor.kt:743). Uppercase, space-separated.</summary>
    public static string ToHex(byte[] bytes)
        => string.Join(" ", bytes.Select(b => b.ToString("X2")));
}

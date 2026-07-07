namespace WaffleMeter.Capture;

/// <summary>
/// Extracts field-boss respawn timers from the 0x9101 status broadcast. The server periodically sends a
/// table of <c>[.. var-int bossCode .. int64-LE targetMs ..]</c> records; rather than assume a fixed field
/// layout (which a patch could shift), we scan the body for a plausible boss-code var-int immediately
/// followed (within a couple of bytes) by an Int64 little-endian Unix-ms timestamp that lands in a sane
/// future window. Pure and gated behind the opcode by the caller, so the scan can't false-match unrelated
/// packets. Boss codes live in the AION field-boss range (elyos 21xxxxx / asmodian 24xxxxx).
/// </summary>
public static class FieldBossTimerParser
{
    private const int MinCode = 2_100_000;   // elyos field-boss code floor
    private const int MaxCode = 2_499_999;   // asmodian field-boss code ceiling
    private const long TwoMinutesMs = 2 * 60 * 1000L;
    private const long OneDayMs = 24 * 60 * 60 * 1000L;

    /// <summary>Scan <paramref name="packet"/> from <paramref name="bodyStart"/> for boss-code → target-time
    /// records. Deduplicated by code (last write wins). <paramref name="arrivedAtMs"/> bounds the accepted
    /// timestamps (now-2m .. now+24h).</summary>
    public static IReadOnlyList<(int Code, long TargetMs)> Parse(byte[] packet, int bodyStart, long arrivedAtMs)
    {
        var found = new Dictionary<int, long>();
        int i = Math.Max(0, bodyStart);
        while (i < packet.Length)
        {
            VarIntOutput v = PacketPrimitives.ReadVarInt(packet, i);
            if (v.Length <= 0)
            {
                i++;
                continue;
            }

            if (v.Value is >= MinCode and <= MaxCode)
            {
                // the timestamp starts within 0..2 bytes after the code var-int (a small alignment gap)
                for (int gap = 0; gap <= 2; gap++)
                {
                    int at = i + v.Length + gap;
                    if (at + 8 > packet.Length)
                    {
                        break;
                    }

                    long target = PacketPrimitives.ReadUInt64Le(packet, at);
                    if (target >= arrivedAtMs - TwoMinutesMs && target <= arrivedAtMs + OneDayMs)
                    {
                        found[v.Value] = target;
                        i = at + 8; // advance past this record
                        goto next;
                    }
                }
            }

            i++;
        next:;
        }

        return found.Select(kv => (kv.Key, kv.Value)).ToList();
    }
}

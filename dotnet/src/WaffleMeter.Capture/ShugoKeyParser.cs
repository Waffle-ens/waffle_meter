namespace WaffleMeter.Capture;

/// <summary>Outcome of decoding a shugo-festa key (슈고 페스타 보상 열쇠) record: the two pools the game keeps,
/// each authoritative. A pool the record omits is ZERO — the field mask leaves a field out precisely because
/// it is empty, so an omitted field must never be read as "unchanged".</summary>
public readonly record struct ShugoKeyParse(bool Ok, int Base, int Bonus)
{
    public static readonly ShugoKeyParse None = default;

    /// <summary>The key count the player can actually spend.</summary>
    public int Total => Base + Bonus;
}

/// <summary>
/// Decodes the shugo-festa key count carried in the 0x610B/0x610C status family — the SAME opcodes, and the
/// same record layout, as aether: <c>&lt;fieldMask&gt; &lt;resourceKey&gt; &lt;groupId(3)&gt; &lt;values…&gt;</c>.
/// The shugo key is group id <c>00 00 00</c>, key <c>0x01</c>; aether is group <c>87 93 03</c>, so the two are
/// read from disjoint records and never collide. See <see cref="AetherStatusParser"/> for how the mask was
/// established (0x04 = first pool only, 0x08 = second pool only, 0x0C = both; an omitted pool is zero).
///
/// <para>This parser used to treat <c>04 03 00 00 00</c> — group <c>00 00 00</c>, key <b>0x03</b> — as this
/// resource's "bonus" field. Under the record layout that is a DIFFERENT resource, not a second field of key
/// 0x01, so it would have added an unrelated counter into the badge as "(+N)". It never fired (every one of
/// the 101 parses across the 28-session corpus reported bonus 0), but the shape was wrong.</para>
///
/// <para>Both pools are bounded by the stack cap rather than a loose sanity limit: the group id here is three
/// zero bytes, which is far less distinctive than aether's, so the cap is what keeps a coincidental byte run
/// inside an unrelated record from being read as a key count. A count past the cap is ignored (the badge holds
/// its previous value) rather than displayed.</para>
///
/// Pure and allocation-free — the caller gates this behind the resource opcode.
/// </summary>
public static class ShugoKeyParser
{
    private const byte ResourceKey = 0x01;
    private static readonly byte[] GroupId = { 0x00, 0x00, 0x00 };
    private const byte MaskBase = 0x04;   // 자동 충전분만 (보너스 = 0)
    private const byte MaskBonus = 0x08;  // 보너스만 (자동 충전분 = 0)
    private const byte MaskBoth = 0x0C;   // 둘 다, 자동 충전분 먼저
    private const int MaxKeys = 14;       // 열쇠 보유 상한 (일일 2개씩 자동 충전)

    /// <summary>Scan <paramref name="packet"/> from <paramref name="bodyStart"/> for a shugo-key record.</summary>
    public static ShugoKeyParse TryParse(byte[] packet, int bodyStart)
    {
        int from = Math.Max(0, bodyStart);
        for (int g = IndexOf(packet, from + 2, GroupId); g >= 0; g = IndexOf(packet, g + 1, GroupId))
        {
            if (g - 2 < from || packet[g - 1] != ResourceKey)
            {
                continue; // a different resource's record (or the group id straddling the header)
            }

            int o = g + GroupId.Length;
            byte mask = packet[g - 2];
            if (mask == MaskBoth)
            {
                if (TryReadPool(packet, o, out int b, out int next) && TryReadPool(packet, next, out int bonus, out _))
                {
                    return new ShugoKeyParse(true, b, bonus);
                }
            }
            else if (mask == MaskBase && TryReadPool(packet, o, out int baseOnly, out _))
            {
                return new ShugoKeyParse(true, baseOnly, 0);
            }
            else if (mask == MaskBonus && TryReadPool(packet, o, out int bonusOnly, out _))
            {
                return new ShugoKeyParse(true, 0, bonusOnly);
            }

            // an unknown mask (or a count past the cap) — keep scanning; a later record may still be ours
        }

        return ShugoKeyParse.None;
    }

    private static bool TryReadPool(byte[] packet, int at, out int value, out int next)
    {
        VarIntOutput v = PacketPrimitives.ReadVarInt(packet, at);
        value = v.Value;
        next = at + v.Length;
        return v.Length > 0 && v.Value >= 0 && v.Value <= MaxKeys;
    }

    private static int IndexOf(byte[] hay, int start, byte[] needle)
    {
        int last = hay.Length - needle.Length;
        for (int i = Math.Max(0, start); i <= last; i++)
        {
            int j = 0;
            while (j < needle.Length && hay[i + j] == needle[j])
            {
                j++;
            }

            if (j == needle.Length)
            {
                return i;
            }
        }

        return -1;
    }
}

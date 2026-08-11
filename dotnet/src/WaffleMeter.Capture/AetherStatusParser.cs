namespace WaffleMeter.Capture;

/// <summary>Outcome of decoding an aether (오드) record: the two pools the game keeps, each authoritative.
/// A pool the record omits is ZERO — the field mask leaves a field out precisely because it is empty, so an
/// omitted field must never be read as "unchanged".</summary>
/// <param name="Ok">A recognized aether record was found.</param>
/// <param name="Base">자연회복 오드 — regenerates +15 on the server's timer, and is spent first.</param>
/// <param name="Bonus">추가 오드 — granted by 오드 회복 소모품 (+10/+40) and other grants.</param>
public readonly record struct AetherParse(bool Ok, int Base, int Bonus)
{
    public static readonly AetherParse None = default;

    /// <summary>What the player can actually spend.</summary>
    public int Total => Base + Bonus;
}

/// <summary>
/// Decodes the aether (오드) balance carried in the 0x610B/0x610C status family. Records in that family are
/// <c>&lt;fieldMask&gt; &lt;resourceKey&gt; &lt;groupId(3)&gt; &lt;value var-ints…&gt;</c>, and the same packet
/// carries several resources back to back (keys 0x01, 0x02, 0x06…), so we scan for the 오드 record's key +
/// group id rather than a fixed offset — a future field shift can't silently mis-read.
///
/// <para><b>The field mask is a bitmask of which of the two pools the record carries</b> (0x04 = 자연회복,
/// 0x08 = 추가, 0x0C = both, 0x00 = neither, i.e. a balance of exactly zero), and the game omits a pool when it
/// is zero — so an empty mask is a real reading, not a malformed record. Reading the single-field 0x08 form as
/// a <i>total</i> — as this parser did until 2026-07-30 — silently corrupted the split: a 오드 회복 소모품
/// (+10/+40) arrives as a 추가-only record, and back-computing a "total" delta from it credited the gain to
/// 자연회복 instead, so the number outside the parentheses grew. Verified against 28 capture sessions: a 0x08
/// record's value always equals the 추가 pool of the neighbouring 0x0C record, never the sum.</para>
///
/// Pure and allocation-free — the caller gates this behind the resource opcode so the key + group id can't
/// false-match a coincidental byte run in an unrelated packet.
/// </summary>
public static class AetherStatusParser
{
    private const byte ResourceKey = 0x01;                       // 오드 (0x02, 0x06… are other resources)
    private static readonly byte[] GroupId = { 0x87, 0x93, 0x03 };
    private const byte MaskEmpty = 0x00;                         // both pools zero (the game omits BOTH fields)
    private const byte MaskBase = 0x04;                          // 자연회복 오드 only (추가 = 0)
    private const byte MaskBonus = 0x08;                         // 추가 오드 only (자연회복 = 0)
    private const byte MaskBoth = 0x0C;                          // both, 자연회복 first
    private const int MaxComponent = 10_000;                     // sanity bound on a single pool

    /// <summary>Scan <paramref name="packet"/> from <paramref name="bodyStart"/> for the 오드 record.</summary>
    public static AetherParse TryParse(byte[] packet, int bodyStart)
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
                    return new AetherParse(true, b, bonus);
                }
            }
            else if (mask == MaskBase && TryReadPool(packet, o, out int baseOnly, out _))
            {
                return new AetherParse(true, baseOnly, 0);
            }
            else if (mask == MaskBonus && TryReadPool(packet, o, out int bonusOnly, out _))
            {
                return new AetherParse(true, 0, bonusOnly);
            }
            else if (mask == MaskEmpty)
            {
                // Both pools empty, so the record carries NO value fields at all. Reporting this as a parse
                // FAILURE (as this did until 2026-08-11) is what left the footer badge blank for a character
                // that has spent everything: the badge's only gate is "has a value ever arrived", so a player
                // at 0 read as "never seen" and the badge stayed hidden until the next 자연회복 tick — up to
                // three hours. Zero is a balance, not the absence of one. <see cref="WeeklyContentParser"/>
                // documents the same mask on the neighbouring currencies.
                return new AetherParse(true, 0, 0);
            }

            // an unknown mask (or an out-of-range value) — keep scanning; a later record may still be ours
        }

        return AetherParse.None;
    }

    private static bool TryReadPool(byte[] packet, int at, out int value, out int next)
    {
        VarIntOutput v = PacketPrimitives.ReadVarInt(packet, at);
        value = v.Value;
        next = at + v.Length;
        return v.Length > 0 && v.Value >= 0 && v.Value <= MaxComponent;
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

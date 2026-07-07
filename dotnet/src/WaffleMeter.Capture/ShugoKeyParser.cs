namespace WaffleMeter.Capture;

/// <summary>Outcome of decoding a shugo-festa key packet: a base(+bonus) split or a total-only update
/// (the data layer back-computes base/bonus from the previous value, exactly like aether).</summary>
public readonly record struct ShugoKeyParse(bool Ok, bool Split, int Base, int Bonus, int Total)
{
    public static readonly ShugoKeyParse None = default;
}

/// <summary>
/// Decodes the shugo-festa key (슈고 페스타 보상 열쇠) count carried in the 0x610B/0x610C status family — the
/// SAME opcodes as aether. Within that status group, a per-stat KEY byte selects the field: aether rides
/// key <c>0x01</c>, the shugo key rides key <c>0x03</c>. So the record marker is identical to aether's apart
/// from that one byte. As with aether we scan for the marker (never a fixed offset, so a field shift can't
/// silently mis-read) and read the trailing var-ints. Pure and allocation-free; the caller gates it behind
/// the resource opcode so the marker can't false-match a coincidental byte run in an unrelated packet.
/// </summary>
public static class ShugoKeyParser
{
    // base + bonus follow this marker (type 0x0C = carries a bonus; key 0x03 = shugo key).
    private static readonly byte[] SplitMarker = { 0x0C, 0x03, 0x87, 0x93, 0x03 };
    // a single total var-int follows this marker (type 0x08 = total only; key 0x03).
    private static readonly byte[] TotalMarker = { 0x08, 0x03, 0x87, 0x93, 0x03 };
    private const int MaxBase = 14;      // the shugo-festa key stack cap (일일 2개씩 자동 충전)
    private const int MaxBonus = 10_000; // sanity bound on the bonus field

    /// <summary>Scan <paramref name="packet"/> from <paramref name="bodyStart"/> for a shugo-key record.</summary>
    public static ShugoKeyParse TryParse(byte[] packet, int bodyStart)
    {
        int split = IndexOf(packet, bodyStart, SplitMarker);
        if (split >= 0)
        {
            int o = split + SplitMarker.Length;
            VarIntOutput b = PacketPrimitives.ReadVarInt(packet, o);
            if (b.Length <= 0 || b.Value < 0 || b.Value > MaxBase)
            {
                return ShugoKeyParse.None;
            }

            VarIntOutput bonus = PacketPrimitives.ReadVarInt(packet, o + b.Length);
            if (bonus.Length <= 0 || bonus.Value < 0 || bonus.Value > MaxBonus)
            {
                return ShugoKeyParse.None;
            }

            return new ShugoKeyParse(true, Split: true, b.Value, bonus.Value, b.Value + bonus.Value);
        }

        int total = IndexOf(packet, bodyStart, TotalMarker);
        if (total >= 0)
        {
            VarIntOutput t = PacketPrimitives.ReadVarInt(packet, total + TotalMarker.Length);
            if (t.Length <= 0 || t.Value < 0 || t.Value > MaxBase + MaxBonus)
            {
                return ShugoKeyParse.None;
            }

            return new ShugoKeyParse(true, Split: false, Base: 0, Bonus: 0, Total: t.Value);
        }

        return ShugoKeyParse.None;
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

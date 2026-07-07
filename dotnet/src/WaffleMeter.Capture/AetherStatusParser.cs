namespace WaffleMeter.Capture;

/// <summary>Outcome of decoding an aether (오드) resource packet: either a full split (base + bonus both
/// present) or a total-only update (the data layer back-computes base/bonus from the previous value).</summary>
/// <param name="Ok">A recognized aether record was found.</param>
/// <param name="Split">True = base and bonus were both read; false = only the total was carried.</param>
public readonly record struct AetherParse(bool Ok, bool Split, int Base, int Bonus, int Total)
{
    public static readonly AetherParse None = default;
}

/// <summary>
/// Decodes the aether (오드) resource value carried in the 0x610B/0x610C status family. The value rides
/// behind one of two fixed marker sequences anywhere in the packet body; we scan for the marker (never a
/// fixed offset, so a future field shift can't silently mis-read) and read the trailing var-ints. Pure and
/// allocation-free — the caller gates it behind the resource opcode so the markers can't false-match a
/// coincidental byte run in an unrelated packet.
/// </summary>
public static class AetherStatusParser
{
    // base + bonus follow this marker (two var-ints).
    private static readonly byte[] SplitMarker = { 0x0C, 0x01, 0x87, 0x93, 0x03 };
    // a single total var-int follows this marker.
    private static readonly byte[] TotalMarker = { 0x08, 0x01, 0x87, 0x93, 0x03 };
    private const int MaxComponent = 10_000; // sanity bound on a single base/bonus/total field

    /// <summary>Scan <paramref name="packet"/> from <paramref name="bodyStart"/> for an aether record.</summary>
    public static AetherParse TryParse(byte[] packet, int bodyStart)
    {
        int split = IndexOf(packet, bodyStart, SplitMarker);
        if (split >= 0)
        {
            int o = split + SplitMarker.Length;
            VarIntOutput b = PacketPrimitives.ReadVarInt(packet, o);
            if (b.Length <= 0 || b.Value < 0 || b.Value > MaxComponent)
            {
                return AetherParse.None;
            }

            VarIntOutput bonus = PacketPrimitives.ReadVarInt(packet, o + b.Length);
            if (bonus.Length <= 0 || bonus.Value < 0 || bonus.Value > MaxComponent)
            {
                return AetherParse.None;
            }

            return new AetherParse(true, Split: true, b.Value, bonus.Value, b.Value + bonus.Value);
        }

        int total = IndexOf(packet, bodyStart, TotalMarker);
        if (total >= 0)
        {
            VarIntOutput t = PacketPrimitives.ReadVarInt(packet, total + TotalMarker.Length);
            if (t.Length <= 0 || t.Value < 0 || t.Value > MaxComponent * 2)
            {
                return AetherParse.None;
            }

            return new AetherParse(true, Split: false, Base: 0, Bonus: 0, Total: t.Value);
        }

        return AetherParse.None;
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

namespace WaffleMeter.Capture;

/// <summary>Outcome of decoding a shugo-festa key packet: a base(+bonus) split or a total-only update
/// (the data layer back-computes base/bonus from the previous value, like aether).</summary>
public readonly record struct ShugoKeyParse(bool Ok, bool Split, int Base, int Bonus, int Total)
{
    public static readonly ShugoKeyParse None = default;
}

/// <summary>
/// Decodes the shugo-festa key (슈고 페스타 보상 열쇠) count carried in the 0x610B/0x610C status family — the
/// SAME opcodes as aether. Aether rides the big status broadcast (the <c>87 93 03</c> record group, key 1);
/// the shugo key arrives in its own update packet using a "header" field layout:
///   <c>04 01 00 00 00 &lt;base&gt;</c>  (field 1 = the key count, a single byte 0..14)
///   <c>04 03 00 00 00 &lt;bonus&gt;</c> (field 3 = bonus keys, optional, single byte)
/// A "compact" var-int layout (<c>0C 01 00 00 00 &lt;base&gt;&lt;bonus&gt;</c> + a fixed tail) is also accepted
/// as a fallback. The two encodings never overlap the aether <c>87 93 03</c> group, so aether and shugo are
/// read from disjoint packets. As with aether we scan for the marker (never a fixed offset) and the caller
/// gates this behind the resource opcode, so a coincidental byte run in an unrelated packet can't false-match.
/// Pure and allocation-free.
/// </summary>
public static class ShugoKeyParser
{
    private static readonly byte[] HeaderBaseField = { 0x04, 0x01, 0x00, 0x00, 0x00 };  // field 1: key count
    private static readonly byte[] HeaderBonusField = { 0x04, 0x03, 0x00, 0x00, 0x00 }; // field 3: bonus keys
    private static readonly byte[] CompactMarker = { 0x0C, 0x01, 0x00, 0x00, 0x00 };     // var-int base+bonus form
    private static readonly byte[] CompactTailA = { 0x03, 0x03, 0x00, 0x00, 0x00, 0x00 };
    private static readonly byte[] CompactTailB = { 0x01, 0x01, 0x00, 0x00, 0x00 };
    private const int MaxBase = 14;      // the shugo-festa key stack cap (일일 2개씩 자동 충전)
    private const int MaxBonus = 10_000; // sanity bound on the compact bonus field

    /// <summary>Scan <paramref name="packet"/> from <paramref name="bodyStart"/> for a shugo-key record.</summary>
    public static ShugoKeyParse TryParse(byte[] packet, int bodyStart)
    {
        // Header form (what the live client sends): 04 01 00 00 00 <base>, optional 04 03 00 00 00 <bonus>.
        int hb = IndexOf(packet, bodyStart, HeaderBaseField);
        if (hb >= 0)
        {
            int baseOff = hb + HeaderBaseField.Length;
            if (baseOff < packet.Length)
            {
                int baseVal = packet[baseOff];
                if (baseVal >= 0 && baseVal <= MaxBase)
                {
                    int bonus = 0;
                    int bonusMarker = baseOff + 1;
                    if (bonusMarker + HeaderBonusField.Length < packet.Length
                        && Matches(packet, bonusMarker, HeaderBonusField))
                    {
                        int bv = packet[bonusMarker + HeaderBonusField.Length];
                        if (bv >= 0 && bv <= MaxBase)
                        {
                            bonus = bv;
                        }
                    }

                    return new ShugoKeyParse(true, Split: true, baseVal, bonus, baseVal + bonus);
                }
            }
        }

        // Compact form (fallback): 0C 01 00 00 00 <base var-int><bonus var-int> + a fixed tail.
        int cm = IndexOf(packet, bodyStart, CompactMarker);
        if (cm >= 0)
        {
            int o = cm + CompactMarker.Length;
            VarIntOutput b = PacketPrimitives.ReadVarInt(packet, o);
            if (b.Length > 0 && b.Value >= 0 && b.Value <= MaxBase)
            {
                VarIntOutput bonus = PacketPrimitives.ReadVarInt(packet, o + b.Length);
                if (bonus.Length > 0 && bonus.Value >= 0 && bonus.Value <= MaxBonus)
                {
                    int tail = o + b.Length + bonus.Length;
                    if (Matches(packet, tail, CompactTailA) || Matches(packet, tail, CompactTailB))
                    {
                        return new ShugoKeyParse(true, Split: true, b.Value, bonus.Value, b.Value + bonus.Value);
                    }
                }
            }
        }

        return ShugoKeyParse.None;
    }

    private static bool Matches(byte[] hay, int at, byte[] needle)
    {
        if (at < 0 || at + needle.Length > hay.Length)
        {
            return false;
        }

        for (int i = 0; i < needle.Length; i++)
        {
            if (hay[at + i] != needle[i])
            {
                return false;
            }
        }

        return true;
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

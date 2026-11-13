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
/// <para>Each pool is bounded by its own game cap (base 14, bonus 30) rather than a loose sanity limit: the group id here is three
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
    // 풀마다 상한이 다르다 — 클라 ContentsTicket ID 1(Contents_Ticket_FieldEvent):
    // SubscribeRechargeMaxCount 14 = 자동 충전분 상한, ItemChargeMaxCount 30 = 아이템으로 채운 보너스 상한.
    // 보너스에 14 를 걸면 보너스를 30 까지 채운 계정의 레코드(0C 01 00 00 00 0E 1E)를 통째로 버려 배지가 사라진다.
    private const int MaxBase = 14;
    private const int MaxBonus = 30;

    /// <summary>
    /// Decode the shugo key from a 0x610B snapshot (<paramref name="fromSnapshot"/>) or 0x610C change notice by
    /// WALKING the frame record by record (<c>[u8 mask][u32 currencyId][fields…]</c>, the layout
    /// <see cref="AbyssCorridorParser"/> documents), and only falling back to the byte scan when the walk fails.
    /// <para><b>Why the walk: zero.</b> The game omits a pool that is zero, so spending the last key arrives as a
    /// currency-1 record with NO fields (mask 0x00) — the same convention the weekly and corridor counters were
    /// measured on. The byte scan cannot accept that shape (<c>00 01 00 00 00</c> is far too weak a needle), so the
    /// count used to stick at 1 forever; with the per-account 총 키나 line that became a persisted wrong answer
    /// (third review 2026-11-13). Inside a walk that consumed the frame EXACTLY, a mask-0 record is a proven
    /// record boundary, so it is read as 0.</para>
    /// <para>Measured on the local corpus: 10/10 0x610B snapshots and 14/14 0x610C notices walk cleanly, and every
    /// snapshot carries a currency-1 record (masks 0x04 and 0x0C seen; 10+16, 12, 14, 14+30). A clean walk with no
    /// currency-1 record says nothing (a notice about another resource, or a character without 슈고 페스타).</para>
    /// </summary>
    public static ShugoKeyParse TryParse(byte[] packet, int bodyStart, bool fromSnapshot)
    {
        switch (WalkForShugoKey(packet, bodyStart, fromSnapshot, out ShugoKeyParse walked))
        {
            case WalkResult.Found:
                return walked;
            case WalkResult.Absent:
                return ShugoKeyParse.None;
            default:
                return TryParse(packet, bodyStart); // the frame did not walk: the old scan, which never invents a 0
        }
    }

    private enum WalkResult
    {
        Failed,
        Absent,
        Found,
    }

    private const uint ShugoCurrencyId = 1;

    private static WalkResult WalkForShugoKey(byte[] packet, int bodyStart, bool fromSnapshot, out ShugoKeyParse result)
    {
        result = ShugoKeyParse.None;
        if (packet is null || bodyStart < 0 || bodyStart >= packet.Length)
        {
            return WalkResult.Failed;
        }

        int offset;
        int records;
        int tail;
        if (fromSnapshot)
        {
            VarIntOutput count = PacketPrimitives.ReadVarInt(packet, bodyStart);
            if (count.Length <= 0 || count.Value <= 0 || count.Value > 4096)
            {
                return WalkResult.Failed;
            }

            offset = bodyStart + count.Length;
            records = count.Value;
            tail = 0;
        }
        else
        {
            byte optMask = packet[bodyStart];
            if (optMask > 0x01)
            {
                return WalkResult.Failed;
            }

            offset = bodyStart + 1;
            records = 1;
            tail = 1 + ((optMask & 0x01) == 0 ? 0 : 4); // reason byte, plus the optional u32
        }

        ShugoKeyParse found = ShugoKeyParse.None;
        bool any = false;
        for (int i = 0; i < records; i++)
        {
            if (offset + 5 > packet.Length)
            {
                return WalkResult.Failed;
            }

            byte mask = packet[offset];
            if ((mask & 0xF0) != 0)
            {
                return WalkResult.Failed;
            }

            uint currencyId = (uint)PacketPrimitives.ParseUInt32Le(packet, offset + 1);
            int o = offset + 5;
            long baseKeys = 0;
            long bonusKeys = 0;
            for (int bit = 0; bit < 4; bit++)
            {
                if ((mask & (1 << bit)) == 0)
                {
                    continue;
                }

                if (bit < 2)
                {
                    if (o + 8 > packet.Length)
                    {
                        return WalkResult.Failed;
                    }

                    o += 8; // 0x01 / 0x02 = fixed u64 fields (times), not key counts
                    continue;
                }

                VarIntOutput v = PacketPrimitives.ReadVarInt(packet, o);
                if (v.Length <= 0 || v.Value < 0)
                {
                    return WalkResult.Failed;
                }

                if (bit == 2)
                {
                    baseKeys = v.Value;
                }
                else
                {
                    bonusKeys = v.Value;
                }

                o += v.Length;
            }

            offset = o;
            if (currencyId == ShugoCurrencyId && !any)
            {
                any = true;
                found = baseKeys <= MaxBase && bonusKeys <= MaxBonus
                    ? new ShugoKeyParse(true, (int)baseKeys, (int)bonusKeys)
                    : ShugoKeyParse.None; // past the game's caps: not a reading we can vouch for
            }
        }

        if (packet.Length - offset != tail)
        {
            return WalkResult.Failed; // the frame does not end where the records do: nothing it said counts
        }

        result = found;
        return any && found.Ok ? WalkResult.Found : WalkResult.Absent;
    }

    /// <summary>Scan <paramref name="packet"/> from <paramref name="bodyStart"/> for a shugo-key record. The
    /// fallback for a frame that does not walk; it cannot read a zero (see the walking overload).</summary>
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
                if (TryReadPool(packet, o, MaxBase, out int b, out int next) && TryReadPool(packet, next, MaxBonus, out int bonus, out _))
                {
                    return new ShugoKeyParse(true, b, bonus);
                }
            }
            else if (mask == MaskBase && TryReadPool(packet, o, MaxBase, out int baseOnly, out _))
            {
                return new ShugoKeyParse(true, baseOnly, 0);
            }
            else if (mask == MaskBonus && TryReadPool(packet, o, MaxBonus, out int bonusOnly, out _))
            {
                return new ShugoKeyParse(true, 0, bonusOnly);
            }

            // an unknown mask (or a count past the cap) — keep scanning; a later record may still be ours
        }

        return ShugoKeyParse.None;
    }

    private static bool TryReadPool(byte[] packet, int at, int cap, out int value, out int next)
    {
        VarIntOutput v = PacketPrimitives.ReadVarInt(packet, at);
        value = v.Value;
        next = at + v.Length;
        return v.Length > 0 && v.Value >= 0 && v.Value <= cap;
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

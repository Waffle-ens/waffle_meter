namespace WaffleMeter.Capture;

/// <summary>One 어비스 회랑 ticket as the server last stated it: which corridor, and how much of its 이용 시간
/// is left in milliseconds. <c>0</c> is a real answer (spent, or never granted this cycle), not "unknown".</summary>
public readonly record struct AbyssCorridorTicket(int TicketId, long RemainingMs);

/// <summary>
/// Decodes the 어비스 회랑 이용 시간 counters carried in the SAME 0x610B/0x610C resource family as 오드,
/// 슈고 열쇠 and the weekly 성역 처치권. Twelve currency ids — <b>10000001~10000012</b> — are the client's
/// <c>Contents_Ticket_ArtifactDungeon_*</c> tickets, whose table row says <c>ETicketType::Time</c> with
/// <c>RechargeMaxTime = 130</c> (seconds). The wire value is that budget in MILLISECONDS: every corridor entry
/// observed across two capture days arrived as exactly <c>130000</c>, and the matching "spent" broadcast landed
/// 130.05~130.70 s later.
///
/// <para><b>⚠️ This cannot be a copy of the three parsers next door.</b> The record's field mask picks field
/// WIDTHS, and the corridor uses a bit none of them handle: <c>0x01 = one fixed u64 (8 bytes)</c>, where
/// <see cref="AetherStatusParser"/>, <see cref="ShugoKeyParser"/> and <see cref="WeeklyContentParser"/> only ever
/// decode <c>0x04</c>/<c>0x08</c> (varints). Reading <c>D0 FB 01 …</c> as a varint yields 26320 and leaves the
/// record misaligned, so a copied parser falls through and reports NOTHING — silently, with no exception and no
/// parser error, forever showing "회랑 0". The known field widths are: <c>0x01</c> → u64, <c>0x02</c> → u64,
/// <c>0x04</c> → varint, <c>0x08</c> → varint.</para>
///
/// <para><b>Mask 0x00 = zero.</b> Same trap the weekly parser documents: the game omits a field that is zero, so
/// a spent corridor arrives as a record with no fields at all. Treating "no fields" as "no reading" would drop
/// exactly the 130000 → 0 transition this exists to see.</para>
///
/// <para><b>Why a full record walk rather than a byte-scan for the id.</b> Scanning for a four-byte id can match
/// inside an unrelated payload, and here a false match would most likely land on a 0x00 byte and manufacture a
/// bogus "spent". Walking the whole list instead makes the frame validate ITSELF: the walk has to consume the
/// body exactly, and a frame that doesn't is rejected whole. Measured against the corpus, that holds — 20/20
/// 0x610B snapshots walked to their declared record count and ended precisely on the last byte, and 259/259
/// 0x610C deltas carried exactly one record. It also rejects the two garbage frames in the same sample (lead
/// bytes 0xD9 and 0x1E) that a byte-scan would have happily read.</para>
///
/// <para><b>Frame shapes</b> (body = first byte after the two opcode bytes):
/// <list type="bullet">
/// <item>0x610B snapshot — <c>[varint recordCount][record × recordCount]</c>, nothing after.</item>
/// <item>0x610C delta — <c>[u8 optMask][record][u8 reason][u32 when optMask &amp; 0x01]</c>. optMask was 0x00
/// (244×) or 0x01 (15×) across the sample; reason was 0x02 or 0x03.</item>
/// </list></para>
///
/// Pure and allocation-free; the caller gates it behind the resource opcode.
/// </summary>
public static class AbyssCorridorParser
{
    /// <summary>Lowest / highest corridor ticket currency id. The client ships twelve
    /// (<c>Contents_Ticket_ArtifactDungeon_001~006</c> plus six unwired <c>_101~103/_201~203</c> stubs); all
    /// twelve are read because WHICH ids are live changes with what the server occupies — across the capture
    /// corpus the active set moved between {1,4,5}, {1,5,6}, {1}, {5,6}, {1,4} and {2,4,6}. Watching only the
    /// ids seen on one day would have missed every corridor for weeks at a time.</summary>
    public const int FirstTicketId = 10_000_001;

    public const int LastTicketId = 10_000_012;

    /// <summary>How many tickets one frame can carry — a snapshot lists every corridor exactly once.</summary>
    public const int MaxTickets = LastTicketId - FirstTicketId + 1;

    /// <summary>The base grant, in ms, from the client's <c>RechargeMaxTime = 130</c> (seconds). Used as the
    /// display denominator only; the parser never assumes the value equals it, because the same table gives
    /// other Time tickets a doubled cap for subscribers (<c>Contents_Ticket_Abyss</c>: 25200 → 50400 s).</summary>
    public const long FullGrantMs = 130_000;

    /// <summary>Ceiling for a plausible corridor reading. Generous next to the 130 s grant so a doubled or
    /// event-boosted ticket still reads, tight enough that a mis-walked frame can't produce an epoch-sized
    /// number. A record above it is dropped rather than failing the whole frame — the walk already proved the
    /// frame's shape, so the outlier is one value we don't understand, not a broken parse.</summary>
    public const long MaxRemainingMs = 3_600_000;

    /// <summary>
    /// Decode every corridor ticket in one 0x610B snapshot or 0x610C delta.
    /// </summary>
    /// <param name="packet">The reassembled packet.</param>
    /// <param name="bodyStart">First byte after the opcode.</param>
    /// <param name="fromSnapshot">True for 0x610B (full dump), false for 0x610C (change notice).</param>
    /// <param name="into">Receives the corridor tickets found; must hold <see cref="MaxTickets"/>.</param>
    /// <returns>How many tickets were written, or <c>-1</c> when the frame does not walk cleanly — in which
    /// case NOTHING may be inferred from it, not even that a corridor is at zero.</returns>
    public static int TryParse(byte[] packet, int bodyStart, bool fromSnapshot, Span<AbyssCorridorTicket> into)
    {
        if (packet is null || bodyStart < 0 || bodyStart >= packet.Length || into.Length < MaxTickets)
        {
            return -1;
        }

        int offset;
        int expectedRecords;
        int expectedTail;

        if (fromSnapshot)
        {
            VarIntOutput count = PacketPrimitives.ReadVarInt(packet, bodyStart);
            if (count.Length <= 0 || count.Value <= 0 || count.Value > 4096)
            {
                return -1;
            }

            offset = bodyStart + count.Length;
            expectedRecords = count.Value;
            expectedTail = 0;
        }
        else
        {
            byte optMask = packet[bodyStart];
            if (optMask > 0x01)
            {
                return -1; // not a change notice we recognise (garbage frames land here)
            }

            offset = bodyStart + 1;
            expectedRecords = 1;
            expectedTail = 1 + ((optMask & 0x01) == 0 ? 0 : 4); // reason byte, plus the optional u32
        }

        int found = 0;
        for (int i = 0; i < expectedRecords; i++)
        {
            if (!TryReadRecord(packet, offset, out int ticketId, out long value, out int next))
            {
                return -1;
            }

            offset = next;
            if (ticketId is >= FirstTicketId and <= LastTicketId && value >= 0 && value <= MaxRemainingMs)
            {
                into[found++] = new AbyssCorridorTicket(ticketId, value);
            }
        }

        // The whole point of the walk: the frame has to end exactly where the records do. Anything left over
        // means the layout was not what we assumed, so every value we just read is suspect.
        return packet.Length - offset == expectedTail ? found : -1;
    }

    /// <summary>One <c>[u8 mask][u32-LE currencyId][fields…]</c> record. <paramref name="value"/> is the FIRST
    /// field the mask carries (the corridor's remaining ms) or 0 when the mask carries none.</summary>
    private static bool TryReadRecord(byte[] packet, int at, out int currencyId, out long value, out int next)
    {
        currencyId = 0;
        value = 0;
        next = at;

        if (at < 0 || at + 5 > packet.Length)
        {
            return false;
        }

        byte mask = packet[at];
        if ((mask & 0xF0) != 0)
        {
            return false; // only the four low bits are field selectors
        }

        currencyId = PacketPrimitives.ParseUInt32Le(packet, at + 1);
        int o = at + 5;
        bool first = true;

        for (int bit = 0; bit < 4; bit++)
        {
            if ((mask & (1 << bit)) == 0)
            {
                continue;
            }

            long field;
            if (bit < 2)
            {
                if (o + 8 > packet.Length)
                {
                    return false;
                }

                field = PacketPrimitives.ReadUInt64Le(packet, o);
                o += 8;
            }
            else
            {
                VarIntOutput v = PacketPrimitives.ReadVarInt(packet, o);
                if (v.Length <= 0 || v.Value < 0)
                {
                    return false;
                }

                field = v.Value;
                o += v.Length;
            }

            if (first)
            {
                value = field;
                first = false;
            }
        }

        next = o;
        return true;
    }
}

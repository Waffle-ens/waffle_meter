using System.Buffers.Binary;

namespace WaffleMeter.Capture;

/// <summary>What one 0x561B record did to its stack — the wire's own <c>_type</c> byte.</summary>
public enum ItemChangeType : byte
{
    /// <summary>A stack that did not exist before. Depositing into the 서버 창고 lands here with a NEW item key.</summary>
    Add = 0,

    /// <summary>An existing stack's ABSOLUTE count after the change — never a delta.</summary>
    Update = 1,

    /// <summary>The stack is gone. Arrives with count 0; its key is what has to be dropped.</summary>
    Remove = 2,
}

/// <summary>One stack of a tracked currency item as the server stated it.</summary>
/// <param name="ItemKey">The server's per-stack id. NOT stable: it changes between sessions, and a deposit into
/// the warehouse issues a fresh one (measured 2026-10-07: 0x1EA000017884D left, 0x2400000128B82 arrived).</param>
/// <param name="ItemId">The client's item id (see <see cref="CurrencyItemParser"/> for the five tracked).</param>
/// <param name="Count">The stack's absolute count.</param>
/// <param name="Container">The client's <c>EItemContainerType</c>: 1 = 인벤토리, 2 = 서버 창고, 15 = 캐릭터 창고.</param>
public readonly record struct CurrencyItem(long ItemKey, int ItemId, long Count, int Container);

/// <summary>One 0x561B record for a tracked currency: what happened, and the stack as it now stands.</summary>
public readonly record struct CurrencyItemChange(ItemChangeType Type, CurrencyItem Item);

/// <summary>How a frame was read — see <see cref="CurrencyItemParser"/>.</summary>
public enum CurrencyParseMode
{
    /// <summary>The walk failed AND the id scan found nothing: nothing may be inferred, not even a zero.</summary>
    Rejected,

    /// <summary>Every record walked and the body ended exactly on the last byte. The list is complete. A 0x5611
    /// snapshot also has to declare at least one record — an empty one walks too easily to be believed.</summary>
    Exact,

    /// <summary>The walk failed, but validated id hits were found. The list is a LOWER bound — what is absent
    /// from it is unknown, never zero.</summary>
    Fallback,
}

/// <summary>A parsed item frame: the tracked-currency records it carried, and how far they can be trusted.</summary>
/// <param name="DeclaredCount">The record count the frame declared (−1 when even that could not be read). Kept
/// for the diagnostic line only.</param>
public readonly record struct CurrencyItemParse(
    CurrencyParseMode Mode,
    IReadOnlyList<CurrencyItemChange> Items,
    int DeclaredCount);

/// <summary>
/// Reads the five currencies the 컨텐츠 관리 panel shows out of the game's ITEM traffic. They are not a wallet
/// message: 키나, 어비스 포인트, 몽환의 파편 and 극복의 증표 are inventory items in the client's own Item table
/// (<c>EItemType::Currency</c>), so their balances only ever travel as item stacks. Two opcodes carry them:
/// <list type="bullet">
/// <item><b>0x5611 ItemDepotList_RS</b> — the world-entry snapshot of every item the character holds, the 서버
/// 창고 included: <c>[2B RS prefix][count varint][Common_Item × count][6 trailing bytes]</c>. One frame (65 KB
/// at most in the corpus, never split), riding an LZ4 bundle, once per game-server connection — not on a zone
/// change.</item>
/// <item><b>0x561B ItemModificationList_NT</b> — what changed:
/// <c>[count varint]{[u8 type][Common_Item][u8 increaseReason][u8 decreaseReason][u8 updateReason]} × count</c>.
/// Counts are ABSOLUTE, so a repeat is harmless.</item>
/// </list>
///
/// <para><b>Common_Item, in the order it actually walks</b> (critic C3, 2026-10-07 — every 0x5611/0x561B frame of
/// the 07-27, 08-08 and 10-07 corpora consumed exactly, 0 failures):
/// <c>[u8 opt][i64 key][u32 itemId][i64 count][u8 container][i32 slot][bool _is_locked][varint-len UTF-8
/// maker][i64 expire][bool _is_period_expire_fixed_time][EquipItemDetail when opt &amp; 1]</c>.
/// ⚠️ The two bools are NOT adjacent. They sit where the schema declares them — one before the maker string,
/// one after expire — and they share a bit-packed byte that is MESSAGE-WIDE: a fresh byte is read only when the
/// eight bits of the previous one are spent, and that state runs on across items and into nested structs. Reading
/// both bools after expire also walks the 10-07 dump (its bool bytes happen to line up), which is how a first
/// decoder got it wrong; the 07-27 dump breaks at its 17th item under that order. Count is a fixed i64, not a
/// varint — kinah passes 2³¹ easily.</para>
///
/// <para><b>Why a full walk, and why a fallback anyway.</b> The walk validates the frame against itself: it has
/// to land on the declared count and end on the last byte, so a misread layout is caught rather than silently
/// believed — and only a frame that walks can say "this currency is absent, so it is zero". Every currency
/// record seen so far has <c>opt = 0</c> (no equipment detail) with count at id+4 and container at id+12, so when
/// the walk fails — a patch extending EquipItemDetail, say — the currencies can still be picked out by their id.
/// Such a reading only ever ADDS what it found. It must never zero the rest: if a patch ever hands one of these
/// opcode numbers to an unrelated message, a scan that zeroed what it did not find would wipe every balance on
/// the first login.</para>
///
/// <para>The maker string is a player nickname. It is skipped, never decoded or kept.</para>
///
/// Pure; never throws. The caller gates it behind the two opcodes.
/// </summary>
public static class CurrencyItemParser
{
    /// <summary>키나(각인) — character-bound kinah.</summary>
    public const int BoundKinaId = 930_100_001;

    /// <summary>키나 — the tradeable kind, the only one that can sit in a warehouse (2 = 서버 창고,
    /// 15 = 캐릭터 창고).</summary>
    public const int KinaId = 930_100_031;

    /// <summary>어비스 포인트.</summary>
    public const int AbyssPointId = 930_100_003;

    /// <summary>몽환의 파편.</summary>
    public const int DreamShardId = 930_100_017;

    /// <summary>극복의 증표.</summary>
    public const int TrialMarkId = 930_100_048;

    // 930100030 키나(통합) is deliberately absent: the client computes it (각인 + 거래 가능, inventory only) and
    // it never appears on the wire.

    /// <summary>The client's <c>EItemContainerType</c> values a tracked currency can sit in.</summary>
    public const int InventoryContainer = 1;

    public const int ServerStorageContainer = 2;

    public const int CharacterStorageContainer = 15;

    /// <summary>Ceiling for a believable stack. The holding cap itself is server-side and unpublished; this only
    /// has to sit far above any real balance (the largest seen is 3.3억) and far below what a misread i64 yields.</summary>
    public const long MaxPlausibleCount = 1_000_000_000_000L;

    /// <summary>Upper bound on a frame's declared record count. The largest snapshot seen holds 711 items.</summary>
    public const int MaxRecords = 8192;

    /// <summary>Upper bound on an EquipItemDetail array length — a misread length must not be multiplied into a
    /// skip of gigabytes.</summary>
    private const int MaxArray = 4096;

    /// <summary>The 2-byte prefix every RS message carries in front of its body ('10 02' on both measured
    /// 0x5611 dumps). Its meaning is unresolved and nothing here depends on it.</summary>
    private const int ResponsePrefixLength = 2;

    /// <summary>0x5611 ends in six u8 counters (per-container slot extensions) after the item list.</summary>
    private const int DepotTrailerLength = 6;

    /// <summary>The three reason bytes after each 0x561B record.</summary>
    private const int ReasonBytes = 3;

    private const byte EquipDetailFlag = 0x01;

    private static readonly IReadOnlyList<CurrencyItemChange> None = [];

    public static bool IsTracked(int itemId) =>
        itemId is BoundKinaId or KinaId or AbyssPointId or DreamShardId or TrialMarkId;

    /// <summary>Decode a 0x5611 world-entry snapshot. <paramref name="bodyStart"/> is the first byte after the two
    /// opcode bytes (the RS prefix). Every record comes back as <see cref="ItemChangeType.Add"/> — a snapshot
    /// lists what exists.</summary>
    public static CurrencyItemParse ParseDepotList(byte[] packet, int bodyStart) =>
        Parse(packet, bodyStart, modification: false);

    /// <summary>Decode a 0x561B change list. <paramref name="bodyStart"/> is the first byte after the two opcode
    /// bytes (a notice has no RS prefix).</summary>
    public static CurrencyItemParse ParseModificationList(byte[] packet, int bodyStart) =>
        Parse(packet, bodyStart, modification: true);

    private static CurrencyItemParse Parse(byte[] packet, int bodyStart, bool modification)
    {
        if (packet is null || bodyStart < 0 || bodyStart >= packet.Length)
        {
            return new CurrencyItemParse(CurrencyParseMode.Rejected, None, -1);
        }

        if (TryWalk(packet, bodyStart, modification, out List<CurrencyItemChange>? walked, out int declared))
        {
            return new CurrencyItemParse(CurrencyParseMode.Exact, walked is null ? None : walked, declared);
        }

        List<CurrencyItemChange> scanned = Scan(packet, bodyStart, modification);
        return scanned.Count > 0
            ? new CurrencyItemParse(CurrencyParseMode.Fallback, scanned, declared)
            : new CurrencyItemParse(CurrencyParseMode.Rejected, None, declared);
    }

    /// <summary>The exact walk. True only when the declared count was walked and the body ended on its last byte
    /// — and no tracked record in it was implausible, because an exact result is what licenses "absent = 0". An
    /// EMPTY depot list is never exact (see below).</summary>
    private static bool TryWalk(
        byte[] packet, int bodyStart, bool modification, out List<CurrencyItemChange>? found, out int declared)
    {
        found = null;
        var r = new ItemWireReader(packet, modification ? bodyStart : bodyStart + ResponsePrefixLength);

        declared = r.VarInt();
        if (r.Failed || declared > MaxRecords)
        {
            declared = r.Failed ? -1 : declared;
            return false;
        }

        // A depot list that declares no records proves nothing about itself: its whole "walk" is a 0 count byte
        // and the six-byte trailer, so ANY 9-byte body whose third byte is 0 passes — the 10-07 login burst
        // carries exactly such a frame on 0x3657 (01 00 | 00 | 00×6). And an exact empty snapshot is the one
        // reading that zeroes every balance at once, which is the accident the fallback rule exists to prevent.
        // A real world-entry list is never empty — equipment alone puts hundreds of records in it (409 / 707 /
        // 711 measured) — so refusing this costs nothing. A change list may still be empty: it zeroes nothing.
        if (!modification && declared == 0)
        {
            return false;
        }

        for (int i = 0; i < declared; i++)
        {
            var type = ItemChangeType.Add;
            if (modification)
            {
                byte t = r.U8();
                if (t > (byte)ItemChangeType.Remove)
                {
                    return false;
                }

                type = (ItemChangeType)t;
            }

            if (!ReadItem(ref r, out CurrencyItem item))
            {
                return false;
            }

            if (modification)
            {
                r.Skip(ReasonBytes);
            }

            if (r.Failed)
            {
                return false;
            }

            if (!IsTracked(item.ItemId))
            {
                continue;
            }

            if (item.Count < 0 || item.Count > MaxPlausibleCount)
            {
                return false; // the frame walked, but a currency in it reads as nonsense: trust none of it
            }

            (found ??= []).Add(new CurrencyItemChange(type, item));
        }

        if (!modification)
        {
            r.Skip(DepotTrailerLength);
        }

        return !r.Failed && r.Position == packet.Length;
    }

    /// <summary>One Common_Item, schema order (see the class summary for why the bools are split).</summary>
    private static bool ReadItem(ref ItemWireReader r, out CurrencyItem item)
    {
        item = default;

        byte opt = r.U8();
        if ((opt & ~EquipDetailFlag) != 0)
        {
            return false; // no other option bit has ever been seen; an unknown one means an unknown layout
        }

        long key = r.I64();
        int itemId = r.I32();
        long count = r.I64();
        byte container = r.U8();
        r.Skip(4);                      // _slot_pos
        r.Bool();                       // _is_locked — BEFORE the maker string (critic C3)
        r.Skip(r.VarInt());             // _maker_nickname — a player name: skipped, never decoded
        r.Skip(8);                      // _period_expire_time
        r.Bool();                       // _is_period_expire_fixed_time

        if ((opt & EquipDetailFlag) != 0)
        {
            SkipEquipDetail(ref r);
        }

        if (r.Failed)
        {
            return false;
        }

        item = new CurrencyItem(key, itemId, count, container);
        return true;
    }

    /// <summary>EquipItemDetail, skipped. Field order follows the schema except that two stat ids are u16 on the
    /// wire though declared as int (StatType._stat_type, MagicStoneSlotInfo._magicstone_stat) — measured on the
    /// 118 equipment items of the 10-07 dump. Currencies never carry it; it only has to be stepped over.</summary>
    private static void SkipEquipDetail(ref ItemWireReader r)
    {
        r.Skip(1 + 4 + 4 + 1 + 4 + 4); // enchant level / exp / bonus, surpass level / prob, item level
        r.Bool();                      // _is_break — bit-packed, sharing the message-wide bool byte
        r.Skip(1);                     // equip slot
        SkipArray(ref r, 4 + 2 + 1);   // magic stones {id i32, stat u16, grade u8}
        r.Skip(8 + 1);                 // soul-bind character dbid, soul-bind count
        SkipArray(ref r, 4);           // god stones {id i32}
        SkipArray(ref r, 2 + 4);       // arcana main stats {stat u16, value i32}
        SkipArray(ref r, 2 + 4);       // arcana sub stats
        SkipArray(ref r, 2 + 4);       // arcana additional sub stats
        SkipArray(ref r, 4 + 1);       // sub skills {id i32, level u8}
        SkipArray(ref r, 4 + 1);       // additional sub skills
        SkipArray(ref r, 2 + 4);       // soul additional sub stats
        SkipArray(ref r, 4 + 1);       // soul additional sub skills
        r.Skip(1);                     // skin extraction count
    }

    private static void SkipArray(ref ItemWireReader r, int elementSize)
    {
        int n = r.VarInt();
        if (n > MaxArray)
        {
            r.Fail();
            return;
        }

        r.Skip(n * elementSize);
    }

    /// <summary>
    /// The fallback: find each tracked id as a u32 and validate the record around it. Every currency record ever
    /// seen has <c>opt = 0</c> nine bytes before the id, count at +4 and container at +12 — unchanged across all
    /// three corpus builds — and a 0x561B record puts its type byte one further back.
    /// <para>Deliberately strict. A record that fails any check is skipped, not guessed at; the reason bytes are
    /// not read at all because their position moves with the shared bool byte.</para>
    /// </summary>
    private static List<CurrencyItemChange> Scan(byte[] packet, int bodyStart, bool modification)
    {
        var found = new List<CurrencyItemChange>();
        var keys = new HashSet<long>();
        int lead = modification ? 10 : 9;

        for (int at = bodyStart + lead; at + 13 <= packet.Length; at++)
        {
            int itemId = BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(at, 4));
            if (!IsTracked(itemId) || packet[at - 9] != 0)
            {
                continue;
            }

            var type = ItemChangeType.Add;
            if (modification)
            {
                byte t = packet[at - 10];
                if (t > (byte)ItemChangeType.Remove)
                {
                    continue;
                }

                type = (ItemChangeType)t;
            }

            long key = BinaryPrimitives.ReadInt64LittleEndian(packet.AsSpan(at - 8, 8));
            long count = BinaryPrimitives.ReadInt64LittleEndian(packet.AsSpan(at + 4, 8));
            int container = packet[at + 12];

            if (container is not (InventoryContainer or ServerStorageContainer or CharacterStorageContainer)
                || count < 0
                || count > MaxPlausibleCount
                || (type == ItemChangeType.Remove && count != 0)
                || !keys.Add(key))
            {
                continue;
            }

            found.Add(new CurrencyItemChange(type, new CurrencyItem(key, itemId, count, container)));
        }

        return found;
    }

    /// <summary>
    /// Little-endian cursor with the protocol's message-wide bool packing. Reading past the end does not throw:
    /// it sets <see cref="Failed"/> and every later read returns zero, so a walk checks once at its checkpoints
    /// instead of after every field.
    /// </summary>
    private ref struct ItemWireReader
    {
        private readonly ReadOnlySpan<byte> _bytes;
        private int _boolByte;
        private int _boolBitsUsed;

        public ItemWireReader(byte[] bytes, int position)
        {
            _bytes = bytes;
            Position = position;
            Failed = position < 0 || position > bytes.Length;
            _boolBitsUsed = 8; // nothing reserved yet: the first bool reads a fresh byte
        }

        public int Position { get; private set; }

        public bool Failed { get; private set; }

        public void Fail()
        {
            Failed = true;
            Position = _bytes.Length;
        }

        public byte U8()
        {
            if (Failed || Position + 1 > _bytes.Length)
            {
                Fail();
                return 0;
            }

            return _bytes[Position++];
        }

        public int I32()
        {
            if (Failed || Position + 4 > _bytes.Length)
            {
                Fail();
                return 0;
            }

            int value = BinaryPrimitives.ReadInt32LittleEndian(_bytes.Slice(Position, 4));
            Position += 4;
            return value;
        }

        public long I64()
        {
            if (Failed || Position + 8 > _bytes.Length)
            {
                Fail();
                return 0;
            }

            long value = BinaryPrimitives.ReadInt64LittleEndian(_bytes.Slice(Position, 8));
            Position += 8;
            return value;
        }

        public void Skip(int count)
        {
            // Compared as "what is left", never as Position + count: a misread maker length near int.MaxValue
            // (FF FF FF FF 07) would wrap the sum negative, pass the check and send Position below zero, and the
            // next read would throw instead of failing the walk over to the id scan.
            if (Failed || count < 0 || count > _bytes.Length - Position)
            {
                Fail();
                return;
            }

            Position += count;
        }

        /// <summary>LEB128, at most five bytes; a value that does not fit a non-negative int is a failure.</summary>
        public int VarInt()
        {
            int value = 0;
            for (int shift = 0; shift < 35; shift += 7)
            {
                byte b = U8();
                if (Failed || (shift == 28 && (b & 0x70) != 0))
                {
                    Fail(); // past 31 bits: no length or count on this wire is that large
                    return 0;
                }

                value |= (b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                {
                    if (value < 0)
                    {
                        Fail();
                        return 0;
                    }

                    return value;
                }
            }

            Fail();
            return 0;
        }

        /// <summary>One bit of the shared bool byte, LSB first. A new byte is consumed only when the previous
        /// one's eight bits are spent — and that state is NOT reset per item.</summary>
        public bool Bool()
        {
            if (_boolBitsUsed >= 8)
            {
                _boolByte = U8();
                _boolBitsUsed = 0;
            }

            return ((_boolByte >> _boolBitsUsed++) & 1) != 0;
        }
    }
}

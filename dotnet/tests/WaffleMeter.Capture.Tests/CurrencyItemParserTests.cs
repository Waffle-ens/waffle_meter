using System.Text;
using K4os.Compression.LZ4;
using WaffleMeter.Capture;
using Xunit;

namespace WaffleMeter.Capture.Tests;

/// <summary>
/// Spec for reading the 컨텐츠 관리 currencies (키나·어비스 포인트·몽환의 파편·극복의 증표) off the item traffic —
/// 0x5611 ItemDepotList_RS (world-entry snapshot) and 0x561B ItemModificationList_NT (absolute changes).
///
/// <para>The two 0x561B fixtures are VERBATIM frames from the 2026-10-07 서버 창고 experiment (00:01:49.309 출고,
/// 00:01:53.809 입고). They carry no nickname. The 0x5611 dump itself is NOT committed — it is 27 KB and its
/// crafted items carry the maker's nickname — so the snapshot cases are synthesized with the same layout and the
/// values that dump held (각인 13,000 / 키나 4,454,882 / 서버 창고 325,000,000 / AP 25,611 / 몽환 18,615 /
/// 극복 3,000).</para>
/// </summary>
public sealed class CurrencyItemParserTests
{
    private static byte[] Hex(string hex) => Convert.FromHexString(hex);

    /// <summary>출고 (00:01:49.309): Update 인벤 키나 → 329,454,882 (inc 16 kItemTakeOut) + Remove the 서버 창고
    /// stack (dec 34). <c>[56 length][1B 56 opcode][02 count]…</c> — 83 bytes, bodyStart 3.</summary>
    private const string Withdraw =
        "561B5602010027030B00006E02003F3370372215A3130000000001000000000000000000000000000010000002004D88170000EA01003F33703700000000000000000200000000000000000000000000002200";

    /// <summary>입고 (00:01:53.809): Update 인벤 키나 → 4,454,882 (dec 25 kItemStore) + Add a NEW 서버 창고 stack
    /// of 325,000,000 (inc 15) under a fresh item key.</summary>
    private const string Deposit =
        "561B5602010027030B00006E02003F337037E2F94300000000000100000000000000000000000000000019000000828B1200004002003F337037401B5F130000000002000000000000000000000000000F0000";

    private const long InventoryKinaKey = 0x00026E00000B0327;
    private const long OldWarehouseKey = 0x0001EA000017884D;
    private const long NewWarehouseKey = 0x0002400000128B82;

    // ---- golden vectors ----

    [Fact]
    public void Reads_the_real_withdraw_frame_exactly()
    {
        CurrencyItemParse p = CurrencyItemParser.ParseModificationList(Hex(Withdraw), 3);

        Assert.Equal(CurrencyParseMode.Exact, p.Mode);
        Assert.Equal(2, p.DeclaredCount);
        Assert.Equal(
            [
                new CurrencyItemChange(ItemChangeType.Update,
                    new CurrencyItem(InventoryKinaKey, CurrencyItemParser.KinaId, 329_454_882, 1)),
                new CurrencyItemChange(ItemChangeType.Remove,
                    new CurrencyItem(OldWarehouseKey, CurrencyItemParser.KinaId, 0, 2)),
            ],
            p.Items);
    }

    /// <summary>The deposit puts the money back under a NEW key — which is why balances are summed per
    /// (item, container) and never kept per item key or per item id alone.</summary>
    [Fact]
    public void Reads_the_real_deposit_frame_exactly_including_the_fresh_warehouse_key()
    {
        CurrencyItemParse p = CurrencyItemParser.ParseModificationList(Hex(Deposit), 3);

        Assert.Equal(CurrencyParseMode.Exact, p.Mode);
        Assert.Equal(
            [
                new CurrencyItemChange(ItemChangeType.Update,
                    new CurrencyItem(InventoryKinaKey, CurrencyItemParser.KinaId, 4_454_882, 1)),
                new CurrencyItemChange(ItemChangeType.Add,
                    new CurrencyItem(NewWarehouseKey, CurrencyItemParser.KinaId, 325_000_000, 2)),
            ],
            p.Items);
        Assert.NotEqual(OldWarehouseKey, p.Items[1].Item.ItemKey);
    }

    /// <summary>Both records of the real frame share ONE bool byte: the first record reserves it for
    /// <c>_is_locked</c> and the second record's two bools land in bits 2 and 3 of it. A decoder that reset the
    /// bool state per record would read one byte too many and miss the exact end — that is what the 83-byte
    /// length pins.</summary>
    [Fact]
    public void The_bool_byte_is_shared_across_records()
    {
        byte[] frame = Hex(Withdraw);
        Assert.Equal(83, frame.Length);

        // Record 1: [type][opt][key 8][id 4][count 8][container][slot 4][BOOL BYTE][maker len][expire 8] = 37 bytes
        // after the count varint; record 2 then has no bool byte of its own: 36 bytes; plus 3 reason bytes each.
        Assert.Equal(3 + 1 + (37 + 3) + (36 + 3), frame.Length);
        Assert.Equal(CurrencyParseMode.Exact, CurrencyItemParser.ParseModificationList(frame, 3).Mode);
    }

    // ---- synthesized snapshots ----

    /// <summary>The 10-07 world-entry values, laid out the way the dump carries them — currencies interleaved
    /// with other items, one crafted equipment piece with a maker name, magic stones and arcana, and enough
    /// records that the shared bool byte rolls over several times.</summary>
    [Fact]
    public void Walks_a_snapshot_with_equipment_and_several_bool_bytes()
    {
        CurrencyItemParse p = Depot(DepotFrame(TenSevenItems()));

        Assert.Equal(CurrencyParseMode.Exact, p.Mode);
        Assert.Equal(TenSevenItems().Count, p.DeclaredCount);
        Assert.All(p.Items, c => Assert.Equal(ItemChangeType.Add, c.Type));
        Assert.Equal(
            [
                (CurrencyItemParser.AbyssPointId, 1, 25_611L),
                (CurrencyItemParser.DreamShardId, 1, 18_615L),
                (CurrencyItemParser.TrialMarkId, 1, 3_000L),
                (CurrencyItemParser.KinaId, 1, 4_454_882L),
                (CurrencyItemParser.BoundKinaId, 1, 13_000L),
                (CurrencyItemParser.KinaId, 2, 325_000_000L),
            ],
            p.Items.Select(c => (c.Item.ItemId, c.Item.Container, c.Item.Count)));
    }

    /// <summary>Count is a fixed i64. A balance past 2³¹ must come through whole — 21억 is not a ceiling a
    /// warehouse respects.</summary>
    [Fact]
    public void Reads_a_count_past_the_int_range()
    {
        CurrencyItemParse p = Depot(DepotFrame([Stack(1, CurrencyItemParser.KinaId, 5_000_000_000, 2)]));

        Assert.Equal(CurrencyParseMode.Exact, p.Mode);
        Assert.Equal(5_000_000_000, Assert.Single(p.Items).Item.Count);
    }

    /// <summary>critic C3. <c>_is_locked</c> is read BEFORE the maker string, <c>_is_period_expire_fixed_time</c>
    /// after expire. A locked crafted item is the case that tells the two orders apart: read both bools after
    /// expire and the lock byte gets taken for the maker string's length.</summary>
    [Fact]
    public void A_locked_crafted_item_walks_only_with_the_lock_bit_before_the_maker()
    {
        var items = new List<WireItem>
        {
            new(10, 100_000_001, 1, 1, Locked: true, Maker: "maker", Expire: 0, Equip: true),
            Stack(11, CurrencyItemParser.BoundKinaId, 13_000, 1),
        };

        CurrencyItemParse p = Depot(DepotFrame(items));

        Assert.Equal(CurrencyParseMode.Exact, p.Mode);
        Assert.Equal(13_000, Assert.Single(p.Items).Item.Count);
    }

    /// <summary>A character that holds none of the five still gets an EXACT answer with nothing in it — that is
    /// "all zero", which the consumer may only conclude because the walk was complete.</summary>
    [Fact]
    public void A_snapshot_without_tracked_currencies_is_exact_and_empty()
    {
        CurrencyItemParse p = Depot(
            DepotFrame([new WireItem(5, 100_000_002, 3, 1, Equip: true), Stack(6, 930_100_023, 20_824, 1)]));

        Assert.Equal(CurrencyParseMode.Exact, p.Mode);
        Assert.Empty(p.Items);
    }

    /// <summary>The fallback. A patch that adds one field to EquipItemDetail breaks the walk at the first
    /// equipment piece; the currencies (never equipment) are still found by id — but only as a lower bound,
    /// so the mode says Fallback and the consumer must not zero what is missing.</summary>
    [Fact]
    public void A_walk_that_breaks_falls_back_to_the_id_scan()
    {
        var items = new List<WireItem>
        {
            Stack(1, CurrencyItemParser.AbyssPointId, 25_611, 1),
            new(2, 100_000_003, 1, 1, Equip: true, EquipExtraField: true), // the "patched" layout
            Stack(3, CurrencyItemParser.KinaId, 4_454_882, 1),
        };

        CurrencyItemParse p = Depot(DepotFrame(items));

        Assert.Equal(CurrencyParseMode.Fallback, p.Mode);
        Assert.Equal(
            [(CurrencyItemParser.AbyssPointId, 25_611L), (CurrencyItemParser.KinaId, 4_454_882L)],
            p.Items.Select(c => (c.Item.ItemId, c.Item.Count)));
    }

    /// <summary>The scan only believes a hit whose surroundings look like a currency record: option byte 0,
    /// a container a currency can be in, a sane count. An id-shaped run anywhere else is skipped.</summary>
    [Fact]
    public void The_scan_skips_id_hits_that_are_not_currency_records()
    {
        var w = new WireWriter();
        w.Raw(0x10, 0x02);                         // RS prefix
        w.VarInt(1);                               // declares one record, but what follows is not one
        w.Raw(0x05);                               // option byte 5 — not a currency record
        w.I64(7);
        w.I32(CurrencyItemParser.KinaId);
        w.I64(1_000);
        w.U8(1);
        w.Raw(0x00);                               // option byte 0 …
        w.I64(8);
        w.I32(CurrencyItemParser.KinaId);
        w.I64(1_000);
        w.U8(9);                                   // … but container 9 is not a currency container
        w.Raw(0, 0, 0, 0);

        CurrencyItemParse p = Depot(Frame(0x5611, w));

        Assert.Equal(CurrencyParseMode.Rejected, p.Mode);
        Assert.Empty(p.Items);
    }

    /// <summary>Content-based capture hands unrelated traffic to the dispatcher; the 08-08 corpus has an 11-byte
    /// LAN frame that happens to start with the 0x5611 bytes. It must read as nothing, not as an empty snapshot.</summary>
    [Fact]
    public void Garbage_is_rejected_not_read_as_an_empty_snapshot()
    {
        byte[] noise = [0x0E, 0x11, 0x56, 0xD7, 0xEF, 0x01, 0x22, 0x9A, 0x00, 0x13, 0x44];

        CurrencyItemParse p = CurrencyItemParser.ParseDepotList(noise, 3);

        Assert.Equal(CurrencyParseMode.Rejected, p.Mode);
        Assert.Empty(p.Items);
    }

    /// <summary>A change record type outside Add/Update/Remove means the layout is not what we think. The exact
    /// walk refuses the frame; the scan refuses that record.</summary>
    [Fact]
    public void An_unknown_change_type_is_not_believed()
    {
        byte[] frame = Hex(Withdraw);
        frame[4] = 0x07; // record 1's type byte

        CurrencyItemParse p = CurrencyItemParser.ParseModificationList(frame, 3);

        Assert.Equal(CurrencyParseMode.Fallback, p.Mode);
        Assert.Equal(ItemChangeType.Remove, Assert.Single(p.Items).Type);
    }

    [Fact]
    public void Truncated_frames_never_throw()
    {
        byte[] full = DepotFrame(TenSevenItems());
        for (int cut = 0; cut < full.Length; cut += 7)
        {
            Assert.NotEqual(CurrencyParseMode.Exact, CurrencyItemParser.ParseDepotList(full[..cut], BodyStart(full)).Mode);
            _ = CurrencyItemParser.ParseModificationList(full[..cut], BodyStart(full));
        }

        byte[] withdraw = Hex(Withdraw);
        for (int cut = 0; cut < withdraw.Length; cut++)
        {
            CurrencyItemParse p = CurrencyItemParser.ParseModificationList(withdraw[..cut], 3);
            Assert.NotEqual(CurrencyParseMode.Exact, p.Mode);
        }
    }

    // ---- end to end through StreamProcessor ----

    /// <summary>Registration is the activation switch (see <see cref="OpcodeRegistrationTests"/>): this drives
    /// the real frames through the dispatcher and checks the data layer actually hears them.</summary>
    [Fact]
    public void The_dispatcher_delivers_the_real_change_frames()
    {
        RecordingData data = Feed(identityOnly: false, Hex(Withdraw), Hex(Deposit));

        Assert.Empty(data.Snapshots);
        Assert.Equal(2, data.Changes.Count);
        Assert.Equal(329_454_882, data.Changes[0][0].Item.Count);
        Assert.Equal(ItemChangeType.Add, data.Changes[1][1].Type);
    }

    /// <summary>The snapshot rides an LZ4 bundle in the login burst — the decompressor must hand the inner
    /// frame to the parser like any other.</summary>
    [Fact]
    public void The_dispatcher_delivers_a_snapshot_from_inside_an_lz4_bundle()
    {
        RecordingData data = Feed(identityOnly: false, Bundle(DepotFrame(TenSevenItems())));

        (IReadOnlyList<CurrencyItem> items, bool exact) = Assert.Single(data.Snapshots);
        Assert.True(exact);
        Assert.Equal(6, items.Count);
        Assert.Contains(items, i => i is { ItemId: CurrencyItemParser.KinaId, Container: 2, Count: 325_000_000 });
    }

    /// <summary>Both opcodes carry absolute state, so they are replayed from a dup-suppressed second game stream
    /// like the 0x610x family — the login snapshot is exactly what tends to ride the suppressed connection.</summary>
    [Fact]
    public void Both_opcodes_are_replayed_from_a_suppressed_duplicate_stream()
    {
        RecordingData data = Feed(identityOnly: true, Bundle(DepotFrame(TenSevenItems())), Hex(Withdraw));

        Assert.Single(data.Snapshots);
        Assert.Single(data.Changes);
    }

    /// <summary>An exact snapshot with nothing tracked in it is still delivered — it is the "everything is zero"
    /// answer. A change list with nothing tracked is not: it is the ordinary loot frame, 300 of them a dungeon.</summary>
    [Fact]
    public void Empty_exact_snapshots_are_delivered_but_untracked_changes_are_not()
    {
        var w = new WireWriter();
        w.VarInt(1);
        w.U8((byte)ItemChangeType.Update);
        WriteItem(w, new WireItem(9, 930_100_023, 5, 1));
        w.Raw(0, 0, 0);

        RecordingData data = Feed(identityOnly: false, DepotFrame([Stack(1, 930_100_023, 20_824, 1)]), Frame(0x561B, w));

        (IReadOnlyList<CurrencyItem> items, bool exact) = Assert.Single(data.Snapshots);
        Assert.True(exact);
        Assert.Empty(items);
        Assert.Empty(data.Changes);
    }

    /// <summary>A fallback snapshot reaches the data layer flagged as NOT exact, and leaves a diagnostic line —
    /// this feature's failure mode is silent, so the line is the only trace that the layout drifted.</summary>
    [Fact]
    public void A_fallback_snapshot_is_flagged_and_logged()
    {
        var items = new List<WireItem>
        {
            new(2, 100_000_003, 1, 1, Equip: true, EquipExtraField: true),
            Stack(3, CurrencyItemParser.KinaId, 4_454_882, 1),
        };

        var sink = new MetaSink();
        var data = new RecordingData();
        new StreamProcessor(sink, data).OnPacketReceived(DepotFrame(items), 0);

        (IReadOnlyList<CurrencyItem> got, bool exact) = Assert.Single(data.Snapshots);
        Assert.False(exact);
        Assert.Equal(4_454_882, Assert.Single(got).Count);
        Assert.Contains(sink.Lines, l => l.Contains("fallback=1", StringComparison.Ordinal));
    }

    // ---- builders ----

    private sealed record WireItem(
        long Key,
        int ItemId,
        long Count,
        int Container,
        bool Locked = false,
        string Maker = "",
        long Expire = 0,
        bool Equip = false,
        bool EquipExtraField = false);

    private static WireItem Stack(long key, int itemId, long count, int container) => new(key, itemId, count, container);

    /// <summary>First byte after the opcode. A frame past 123 content bytes takes a two-byte length varint, so
    /// the body does not always start at 3.</summary>
    private static int BodyStart(byte[] frame) => PacketPrimitives.ReadVarInt(frame).Length + 2;

    private static CurrencyItemParse Depot(byte[] frame) => CurrencyItemParser.ParseDepotList(frame, BodyStart(frame));

    /// <summary>Synthesized items in the order and with the shapes the 10-07 dump carries.</summary>
    private static List<WireItem> TenSevenItems() =>
    [
        new(0x100, 100_000_010, 1, 1, Equip: true, Maker: "maker"),
        Stack(0x20300001697A3, CurrencyItemParser.AbyssPointId, 25_611, 1),
        new(0x101, 100_000_011, 1, 11, Expire: 1_791_400_000_000),
        Stack(0x21000000B0FAD, CurrencyItemParser.DreamShardId, 18_615, 1),
        new(0x102, 100_000_012, 1, 14, Locked: true, Equip: true),
        Stack(0x26300000F8176, CurrencyItemParser.TrialMarkId, 3_000, 1),
        Stack(0x103, 930_100_023, 20_824, 1),                    // 강화석: a currency-type item we do not track
        Stack(InventoryKinaKey, CurrencyItemParser.KinaId, 4_454_882, 1),
        Stack(0x104, 930_200_001, 5_060, 3),                     // 큐나, container 3
        Stack(0x187000018A1B0, CurrencyItemParser.BoundKinaId, 13_000, 1),
        new(0x105, 100_000_013, 1, 2, Locked: true),
        Stack(OldWarehouseKey, CurrencyItemParser.KinaId, 325_000_000, 2),
        new(0x106, 100_000_014, 1, 15, Equip: true, Expire: 1_791_500_000_000),
    ];

    private static void WriteItem(WireWriter w, WireItem item)
    {
        w.U8((byte)(item.Equip ? 1 : 0));
        w.I64(item.Key);
        w.I32(item.ItemId);
        w.I64(item.Count);
        w.U8((byte)item.Container);
        w.I32(0);                                  // slot
        w.Bool(item.Locked);                       // _is_locked — before the maker string
        byte[] maker = Encoding.UTF8.GetBytes(item.Maker);
        w.VarInt(maker.Length);
        w.Raw(maker);
        w.I64(item.Expire);
        w.Bool(item.Expire != 0);                  // _is_period_expire_fixed_time
        if (!item.Equip)
        {
            return;
        }

        w.U8(15); w.I32(1_200); w.I32(3); w.U8(2); w.I32(5_000); w.I32(1_450);
        w.Bool(false);                             // _is_break
        w.U8(4);                                   // equip slot
        w.VarInt(2);                               // magic stones {i32, u16, u8}
        w.I32(1_001); w.U16(17); w.U8(3);
        w.I32(1_002); w.U16(18); w.U8(4);
        w.I64(0x07D3_0000_0000_0001); w.U8(1);      // soul-bind dbid (synthetic), count
        w.VarInt(1); w.I32(2_001);                 // god stones
        for (int k = 0; k < 3; k++)
        {
            w.VarInt(1); w.U16(20 + k); w.I32(100 + k); // arcana stats {u16, i32}
        }

        for (int k = 0; k < 2; k++)
        {
            w.VarInt(1); w.I32(3_000 + k); w.U8(1);  // sub skills {i32, u8}
        }

        w.VarInt(0);                               // soul additional sub stats
        w.VarInt(1); w.I32(3_100); w.U8(2);        // soul additional sub skills
        w.U8(0);                                   // skin extraction count
        if (item.EquipExtraField)
        {
            w.U8(0x7F);                            // a field a future patch appended
        }
    }

    private static byte[] DepotFrame(IReadOnlyList<WireItem> items)
    {
        var w = new WireWriter();
        w.Raw(0x10, 0x02);                         // RS prefix, as on both measured dumps
        w.VarInt(items.Count);
        foreach (WireItem item in items)
        {
            WriteItem(w, item);
        }

        w.Raw(0x02, 0x0A, 0x1C, 0x00, 0x11, 0x00); // the six trailing counters of the 10-07 dump
        return Frame(0x5611, w);
    }

    /// <summary>[length varint][opcode LE][body]. The length value is content + 4, which is what makes the
    /// assembler's <c>value + varintLength − 4</c> land on the whole frame (0x561B: 0x56 = 82 + 4).</summary>
    private static byte[] Frame(int opcode, WireWriter body)
    {
        var content = new List<byte> { (byte)(opcode & 0xFF), (byte)(opcode >> 8) };
        content.AddRange(body.Bytes);
        var frame = new WireWriter();
        frame.VarInt(content.Count + 4);
        frame.Raw([.. content]);
        return [.. frame.Bytes];
    }

    /// <summary>[varint len=1][FF FF][originLength u32 LE][lz4 block] — the login burst's container.</summary>
    private static byte[] Bundle(params byte[][] frames)
    {
        byte[] restored = frames.SelectMany(f => f).ToArray();
        var compressed = new byte[LZ4Codec.MaximumOutputSize(restored.Length)];
        int clen = LZ4Codec.Encode(restored, 0, restored.Length, compressed, 0, compressed.Length);
        Assert.True(clen > 0);

        var outer = new List<byte> { 0x01, 0xFF, 0xFF };
        outer.AddRange(BitConverter.GetBytes(restored.Length));
        outer.AddRange(compressed[..clen]);
        return [.. outer];
    }

    /// <summary>Little-endian writer that packs bools the way the wire does: LSB first into a byte reserved at
    /// the moment the first bool needs one, shared until its eight bits are spent — across records.</summary>
    private sealed class WireWriter
    {
        private int _boolIndex = -1;
        private int _boolBitsUsed = 8;

        public List<byte> Bytes { get; } = [];

        public void Raw(params byte[] bytes) => Bytes.AddRange(bytes);

        public void U8(byte v) => Bytes.Add(v);

        public void U16(int v) => Bytes.AddRange(BitConverter.GetBytes((ushort)v));

        public void I32(int v) => Bytes.AddRange(BitConverter.GetBytes(v));

        public void I64(long v) => Bytes.AddRange(BitConverter.GetBytes(v));

        public void VarInt(int value)
        {
            uint v = (uint)value;
            while (v >= 0x80)
            {
                Bytes.Add((byte)(v | 0x80));
                v >>= 7;
            }

            Bytes.Add((byte)v);
        }

        public void Bool(bool value)
        {
            if (_boolBitsUsed >= 8)
            {
                _boolIndex = Bytes.Count;
                Bytes.Add(0);
                _boolBitsUsed = 0;
            }

            if (value)
            {
                Bytes[_boolIndex] |= (byte)(1 << _boolBitsUsed);
            }

            _boolBitsUsed++;
        }
    }

    private static RecordingData Feed(bool identityOnly, params byte[][] frames)
    {
        var data = new RecordingData();
        var processor = new StreamProcessor(NullStreamProcessorSink.Instance, data);
        foreach (byte[] frame in frames)
        {
            processor.OnPacketReceived(frame, 0, identityOnly);
        }

        return data;
    }

    private sealed class MetaSink : IStreamProcessorSink
    {
        public readonly List<string> Lines = [];

        public void Dispatch(int opcode, string? opcodeName, bool extraFlag, int len) { }
        public void UnknownOpcode(int opcode, bool extraFlag, int len) { }
        public void CompressedPacket(int len, bool extraFlag) { }
        public void ParserError(string stage, string reason) { }
        public void Damage(string kind, ParsedDamagePacket packet, bool saved, string? reason, int? mobCode) { }

        public void Meta(string type, params (string Key, object? Value)[] fields) =>
            Lines.Add(type + " " + string.Join(' ', fields.Select(f => $"{f.Key}={f.Value}")));

        public void Battle(int target, int toggle, int? mobCode, string? mobName, bool accepted, string? reason) { }
    }

    private sealed class RecordingData : ICaptureGameData
    {
        public readonly List<(IReadOnlyList<CurrencyItem> Items, bool Exact)> Snapshots = [];

        public readonly List<IReadOnlyList<CurrencyItemChange>> Changes = [];

        public void SaveCurrencySnapshot(IReadOnlyList<CurrencyItem> items, bool exact) => Snapshots.Add((items, exact));

        public void SaveCurrencyChanges(IReadOnlyList<CurrencyItemChange> changes) => Changes.Add(changes);

        public Mob? GetMob(int code) => null;
        public int? GetMobId(int instanceId) => null;
        public void SaveMobId(int instanceId, int mobCode) { }
        public bool SkillExists(long code) => false;
        public long CurrentEpoch() => 0;
        public void SaveDamage(ParsedDamagePacket pdp, long epoch) { }
        public void StartBattle(int target) { }
        public void EndBattle(int target) { }
        public void SaveNickname(int uid, string nickname, bool isExecutor, int server, int jobByte) { }
        public void SaveUserPower(int uid, int power) { }
        public void SaveSummon(int summonId, int ownerId) { }
        public void SaveMobHp(int instanceId, long hp) { }
        public void SaveUseBuff(int uid, int skillCode, long buffStart, long buffEnd, long duration, int actorId) { }
        public void RequestOfficialCharacterLookup(int uid) { }
        public void SavePartyRoster(IReadOnlyList<(string Nickname, int Server, int Slot)> members) { }
        public void SaveAetherStatus(int baseVal, int bonus) { }
        public void SaveShugoKey(int baseVal, int bonus) { }
        public void SaveFieldBossTimers(IReadOnlyList<(int Code, long TargetMs)> timers) { }
    }
}

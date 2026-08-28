namespace WaffleMeter.Capture;

/// <summary>Which side of the current 어비스 matchup holds one 아티팩트, as the server states it.</summary>
/// <param name="ArtifactId">The client's artifact id — <c>1001~1003</c> (하층 / AR1) and <c>2001~2003</c>
/// (중층 / AR3), in the same RR → STI → WSA order the corridor catalog uses.</param>
/// <param name="OwnerSide">The matchup slot holding it: <c>1</c> or <c>2</c>, never a race.
/// <para><b>⚠️ Which slot is OURS is not fixed and must not be hard-coded.</b> 콘팡 (uid-independent identity,
/// server 2003, job 16) was slot 1 on 2026-08-23 and slot 2 on 2026-08-28 — same character, same server, five
/// days apart. That is the in-game guide's "서버 매칭 변경" showing up on the wire: the abyss pairs servers, and
/// the slot is an index inside THAT pairing. <see cref="AbyssArtifactBuffCatalog"/> is how the slot is
/// resolved.</para></param>
public readonly record struct AbyssArtifactHolding(int ArtifactId, int OwnerSide);

/// <summary>The 점령 window one artifact zone is currently inside, straight from the server.</summary>
/// <param name="ZoneId"><c>1001</c> = 하층, <c>2001</c> = 중층 — the zone's first artifact id, which is what the
/// server uses as the zone key.</param>
/// <param name="StartMs">When the current 점령 settled. Measured 2026-08-26 22:15:54 KST for 하층 on the 08-28
/// capture, 12 s apart from 중층 — i.e. the real per-zone settle moment, not a rounded schedule.</param>
/// <param name="EndMs">When the next 점령전 takes it away. Measured 2026-08-29 Sat 22:05:00 KST, identical for
/// both zones.</param>
public readonly record struct AbyssArtifactZone(int ZoneId, long StartMs, long EndMs);

/// <summary>
/// Decodes the 어비스 아티팩트 점령 현황 broadcast — <b>0xE305</b> (one zone, sent on zone entry) and
/// <b>0xE307</b> (every zone, sent at login and on each world-map open).
///
/// <para><b>This is the packet the corridor feature was missing.</b> It was never a wire change: these frames
/// are present, byte-for-byte the same shape, in corpora from 2026-08-17 onward — the meter simply dispatched
/// them as unknown opcodes. Occupation was proved instead by watching the character walk into a corridor's own
/// instance map, which works but answers far less: it names only the corridors this character personally got
/// into, only while it had time left, and only if the meter happened to be running. Across the whole of August
/// that fired six times. These frames state, per artifact, which side holds it — the question
/// <c>AbyssCorridorStore.MarkEntered</c> was standing in for — for every corridor at once and without going
/// anywhere.</para>
///
/// <para><b>Measured across four capture days</b> (2026-08-17, 08-19, 08-23, 08-28): 44 frames carrying 62
/// zone records, every frame walking to its last byte, none rejected.</para>
///
/// <para><b>Frame shapes</b> (body = first byte after the two opcode bytes). Both end EXACTLY on their last
/// record, which is what the walk below verifies:
/// <list type="bullet">
/// <item>0xE307 — <c>[u16 reserved][u8 zoneCount][zone × zoneCount][u8 recordCount][record × recordCount]</c>.
/// Measured 152-byte body: reserved 0, 2 zones, 6 records.</item>
/// <item>0xE305 — <c>[zone][u8 recordCount][record × recordCount]</c>, no prefix and exactly one zone.
/// Measured 75-byte body: 1 zone, 3 records.</item>
/// </list></para>
/// <para><b>zone</b> = <c>[u8 flag][u32-LE zoneId][u8 artifactCount][u8 kind][u64-LE startMs][u64-LE endMs]</c>
/// (23 bytes). <c>flag</c> was 1 in all four observed zones; <c>kind</c> was 0 on 0xE307 and 2 on 0xE305 and is
/// NOT the owning side (it read 2 on the day the player was side 1).</para>
/// <para><b>record</b> = <c>[u32-LE artifactId][u32-LE objectCode][u8 ownerSide][u32-LE][u32-LE]</c>
/// (17 bytes). Object codes observed: 하층 1100017/1100019/1100021, 중층 1100464/1100465/1100466. The trailing
/// eight bytes differ by frame — 0xE307 repeats the object code and then zero, 0xE305 sends zeros for both — so
/// only the id and the owner byte are read, and the record length is what the walk checks.</para>
///
/// <para><b>Why a strict walk and not a scan.</b> Capture is direction-unrestricted, so unrelated traffic frames
/// through this pipeline — the 08-28 corpus alone carries DNS answers for chatgpt.com and googlevideo hosts that
/// reach the dispatcher as 0xEDC2 and 0x3AE9. A frame is therefore accepted only when its declared counts consume
/// the body to the last byte, and rejected whole otherwise.</para>
///
/// <para><b>Cross-checked three ways, on days this parser was not built from.</b> 2026-08-28: side 2 holds
/// 1001/1003 and 2001/2003, and the 0x610B snapshot in the same capture reports 130000 ms on exactly tickets
/// 10000001/10000003/10000004/10000006. 2026-08-17 and 08-19: the abnormals name side 1, side 1 holds
/// 1002/2001/2003 → tickets 10000002/10000004/10000006 → maps 503001/503004/503006 through the catalog's
/// non-obvious map permutation — and those are precisely the three corridor maps the player actually walked
/// into on both days. Artifact→ticket being the identity mapping and ticket→map being the permutation are
/// confirmed together by that.</para>
///
/// Pure and allocation-free; the caller gates it behind the opcode.
/// </summary>
public static class AbyssArtifactParser
{
    /// <summary>Zones one frame can describe — 하층 and 중층. The client wires no others.</summary>
    public const int MaxZones = 2;

    /// <summary>Artifacts one frame can describe: three per zone.</summary>
    public const int MaxArtifacts = 6;

    /// <summary>Artifacts per zone, per the zone header's own count field in every observed frame.</summary>
    public const int ArtifactsPerZone = 3;

    private const int ZoneBytes = 23;

    private const int RecordBytes = 17;

    /// <summary>Lowest / highest artifact id the client wires. Anything outside is a frame we do not understand,
    /// not an artifact we have not shipped: a record out of range rejects the whole frame, because the ids are
    /// what the walk's self-validation rests on.</summary>
    public const int FirstArtifactId = 1001;

    public const int LastArtifactId = 2003;

    /// <summary>Epoch-ms bounds a 점령 window has to sit inside (2020-01-01 .. 2100-01-01), mirroring the
    /// instance-phase gate. Without it a mis-walked frame could publish an epoch-sized cycle that would keep
    /// every stale record alive forever.</summary>
    private const long MinPlausibleEpochMs = 1_577_836_800_000L;

    private const long MaxPlausibleEpochMs = 4_102_444_800_000L;

    /// <summary>
    /// Decode one 0xE305 or 0xE307 frame.
    /// </summary>
    /// <param name="packet">The reassembled packet.</param>
    /// <param name="bodyStart">First byte after the opcode.</param>
    /// <param name="wholeAbyss">True for 0xE307 (every zone, carries the count prefix), false for 0xE305.</param>
    /// <param name="zones">Receives the 점령 windows; must hold <see cref="MaxZones"/>.</param>
    /// <param name="holdings">Receives the per-artifact owners; must hold <see cref="MaxArtifacts"/>.</param>
    /// <param name="zoneCount">How many zones were written.</param>
    /// <returns>How many holdings were written, or <c>-1</c> when the frame does not walk cleanly — in which
    /// case NOTHING may be inferred from it, not even that a zone is uncontested.</returns>
    public static int TryParse(
        byte[] packet,
        int bodyStart,
        bool wholeAbyss,
        Span<AbyssArtifactZone> zones,
        Span<AbyssArtifactHolding> holdings,
        out int zoneCount)
    {
        zoneCount = 0;
        if (packet is null
            || bodyStart < 0
            || bodyStart >= packet.Length
            || zones.Length < MaxZones
            || holdings.Length < MaxArtifacts)
        {
            return -1;
        }

        int offset = bodyStart;
        int expectedZones;

        if (wholeAbyss)
        {
            if (offset + 3 > packet.Length)
            {
                return -1;
            }

            // The two reserved bytes were 00 00 in every observed frame. They are required to be so: this is the
            // only thing separating a real 0xE307 from a same-length frame that happens to start with a plausible
            // zone count.
            if (packet[offset] != 0 || packet[offset + 1] != 0)
            {
                return -1;
            }

            expectedZones = packet[offset + 2];
            offset += 3;

            if (expectedZones is <= 0 or > MaxZones)
            {
                return -1;
            }
        }
        else
        {
            expectedZones = 1;
        }

        for (int i = 0; i < expectedZones; i++)
        {
            if (!TryReadZone(packet, offset, out AbyssArtifactZone zone))
            {
                return -1;
            }

            zones[zoneCount++] = zone;
            offset += ZoneBytes;
        }

        if (offset >= packet.Length)
        {
            return -1;
        }

        int expectedRecords = packet[offset++];
        if (expectedRecords <= 0 || expectedRecords > MaxArtifacts)
        {
            return -1;
        }

        int found = 0;
        for (int i = 0; i < expectedRecords; i++)
        {
            if (offset + RecordBytes > packet.Length)
            {
                return -1;
            }

            int artifactId = PacketPrimitives.ParseUInt32Le(packet, offset);
            int ownerSide = packet[offset + 8];

            // The owner byte is the payload; an unknown value means the field is not what we think it is, so the
            // frame goes rather than the record. Observed values were 1 and 2 only, across 18 records.
            if (artifactId is < FirstArtifactId or > LastArtifactId || ownerSide is < 1 or > 2)
            {
                return -1;
            }

            holdings[found++] = new AbyssArtifactHolding(artifactId, ownerSide);
            offset += RecordBytes;
        }

        // The whole point of the walk: the declared counts have to consume the body exactly. A frame that leaves
        // bytes over was not this packet, and every value just read is suspect.
        return offset == packet.Length ? found : -1;
    }

    /// <summary>One 23-byte zone header. Rejects rather than clamps: the window drives which records stay
    /// credible, so a window we cannot believe must not be published at all.</summary>
    private static bool TryReadZone(byte[] packet, int at, out AbyssArtifactZone zone)
    {
        zone = default;
        if (at < 0 || at + ZoneBytes > packet.Length)
        {
            return false;
        }

        if (packet[at] != 1)
        {
            return false; // flag; 1 in every observed zone
        }

        int zoneId = PacketPrimitives.ParseUInt32Le(packet, at + 1);
        int artifactCount = packet[at + 5];
        long startMs = PacketPrimitives.ReadUInt64Le(packet, at + 7);
        long endMs = PacketPrimitives.ReadUInt64Le(packet, at + 15);

        if (zoneId is < FirstArtifactId or > LastArtifactId
            || artifactCount != ArtifactsPerZone
            || startMs < MinPlausibleEpochMs
            || startMs > MaxPlausibleEpochMs
            || endMs <= startMs
            || endMs > MaxPlausibleEpochMs)
        {
            return false;
        }

        zone = new AbyssArtifactZone(zoneId, startMs, endMs);
        return true;
    }
}

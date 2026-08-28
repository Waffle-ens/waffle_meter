using WaffleMeter.Capture;
using Xunit;

namespace WaffleMeter.Capture.Tests;

/// <summary>
/// Golden spec for <see cref="AbyssArtifactParser"/> against real captured 0xE305/0xE307 frames.
///
/// <para>Every frame here is verbatim from a packet-debug corpus, including its length varint, so bodyStart is
/// the real one: 4 for the two-byte-length 0xE307, 3 for 0xE305.</para>
///
/// <para>The assertion that carries the most weight is not any single owner byte but that the walk consumes the
/// body EXACTLY. Capture is direction-unrestricted and unrelated traffic frames through the same dispatcher
/// (the 08-28 corpus alone delivers DNS answers as 0xEDC2 and 0x3AE9), so "the declared counts land on the last
/// byte" is the only thing separating this packet from a coincidence.</para>
/// </summary>
public class AbyssArtifactParserTests
{
    private const int WholeAbyssBody = 4;  // 0xE307, two-byte length varint
    private const int SingleZoneBody = 3;  // 0xE305, one-byte length varint

    // 2026-08-28 23:47:43 KST — login broadcast. Side 2 holds 1001/1003 (하층) and 2001/2003 (중층).
    private const string WholeAbyss0828 =
        "9E0107E300000201D107000003002023373EA001000060989F4DA001000001E90300000300107F363EA0010000"
        + "60989F4DA001000006D1070000B0CA100002B0CA100000000000D2070000B1CA100001B1CA100000000000D3"
        + "070000B2CA100002B2CA100000000000E9030000F1C8100002F1C8100000000000EA030000F3C8100001F3C8"
        + "100000000000EB030000F5C8100002F5C8100000000000";

    // 2026-08-23 02:08:10 KST — same shape, DIFFERENT split: side 2 holds 1001/1002 and 2001/2002.
    private const string WholeAbyss0823 =
        "9E0107E300000201D10700000300F8F4A129A00100004018313EA001000001E9030000030018C6A129A0010000"
        + "4018313EA001000006D1070000B0CA100002B0CA100000000000D2070000B1CA100002B1CA100000000000D3"
        + "070000B2CA100001B2CA100000000000E9030000F1C8100002F1C8100000000000EA030000F3C8100002F3C8"
        + "100000000000EB030000F5C8100001F5C8100000000000";

    // 2026-08-28 23:47:51 KST — 어비스 하층 load, 5.6 s after the capture began.
    private const string LowerZone0828 =
        "5105E301E90300000302107F363EA001000060989F4DA001000003E9030000F1C81000020000000000000000EA"
        + "030000F3C81000010000000000000000EB030000F5C81000020000000000000000";

    // 2026-08-28 23:48:12 KST — 어비스 중층 load, 20 s later.
    private const string MiddleZone0828 =
        "5105E301D107000003022023373EA001000060989F4DA001000003D1070000B0CA1000020000000000000000D2"
        + "070000B1CA1000010000000000000000D3070000B2CA1000020000000000000000";

    private static (AbyssArtifactZone[] Zones, AbyssArtifactHolding[] Holdings) Parse(
        string hex, int bodyStart, bool wholeAbyss)
    {
        byte[] packet = Convert.FromHexString(hex);
        var zones = new AbyssArtifactZone[AbyssArtifactParser.MaxZones];
        var holdings = new AbyssArtifactHolding[AbyssArtifactParser.MaxArtifacts];
        int count = AbyssArtifactParser.TryParse(packet, bodyStart, wholeAbyss, zones, holdings, out int zoneCount);
        Assert.True(count >= 0, "frame did not walk cleanly");
        return (zones[..zoneCount], holdings[..count]);
    }

    /// <summary>The whole-abyss broadcast: two zones, six artifacts, consumed to the last byte.</summary>
    [Fact]
    public void Whole_abyss_frame_reads_both_zones_and_all_six_artifacts()
    {
        (AbyssArtifactZone[] zones, AbyssArtifactHolding[] holdings) =
            Parse(WholeAbyss0828, WholeAbyssBody, wholeAbyss: true);

        Assert.Equal(2, zones.Length);
        Assert.Equal(AbyssArtifactBuffCatalog.MiddleZoneId, zones[0].ZoneId);
        Assert.Equal(AbyssArtifactBuffCatalog.LowerZoneId, zones[1].ZoneId);

        Assert.Equal(
            new[] { 2001, 2002, 2003, 1001, 1002, 1003 },
            holdings.Select(h => h.ArtifactId).ToArray());
        Assert.Equal(
            new[] { 2, 1, 2, 2, 1, 2 },
            holdings.Select(h => h.OwnerSide).ToArray());
    }

    /// <summary>The 점령 window comes straight from the server — no schedule arithmetic. 2026-08-26 Wed
    /// 22:15:54 KST → 2026-08-29 Sat 22:05:00 KST, and the two zones settle 42 s apart, which a derived
    /// timetable could not have produced.</summary>
    [Fact]
    public void Whole_abyss_frame_carries_the_real_occupation_window()
    {
        (AbyssArtifactZone[] zones, _) = Parse(WholeAbyss0828, WholeAbyssBody, wholeAbyss: true);

        Assert.Equal(1_787_750_196_000, zones[0].StartMs);
        Assert.Equal(1_787_750_154_000, zones[1].StartMs);
        Assert.Equal(1_788_008_700_000, zones[0].EndMs);
        Assert.Equal(1_788_008_700_000, zones[1].EndMs);
        Assert.All(zones, z => Assert.True(z.EndMs > z.StartMs));
    }

    /// <summary>The window is per cycle, not a constant: the Wednesday→Saturday cycle measured on 08-28 runs
    /// 71.8 h and the Saturday→Wednesday one on 08-23 runs 95.8 h. Anything that assumed a fixed span, or that
    /// derived the boundary from a weekday timetable, would be wrong every other cycle — and wrong by the ~11
    /// minutes the settle lags 22:00 even when it picked the right day.</summary>
    [Fact]
    public void Occupation_window_length_differs_between_cycles()
    {
        (AbyssArtifactZone[] wed, _) = Parse(WholeAbyss0828, WholeAbyssBody, wholeAbyss: true);
        (AbyssArtifactZone[] sat, _) = Parse(WholeAbyss0823, WholeAbyssBody, wholeAbyss: true);

        Assert.Equal(71, (wed[0].EndMs - wed[0].StartMs) / 3_600_000);  // 71.81 h
        Assert.Equal(95, (sat[0].EndMs - sat[0].StartMs) / 3_600_000);  // 95.81 h
    }

    /// <summary>The owner byte is a matchup SLOT, not a race: the same character on the same server reads the
    /// two frames differently five days apart. A build that hard-coded "side 2 is 마족" would have shown the
    /// enemy's corridors on one of these two days.</summary>
    [Fact]
    public void Owner_side_is_not_stable_across_cycles()
    {
        (_, AbyssArtifactHolding[] wed) = Parse(WholeAbyss0828, WholeAbyssBody, wholeAbyss: true);
        (_, AbyssArtifactHolding[] sat) = Parse(WholeAbyss0823, WholeAbyssBody, wholeAbyss: true);

        Assert.Equal(2, wed.Single(h => h.ArtifactId == 1001).OwnerSide);
        Assert.Equal(1, wed.Single(h => h.ArtifactId == 1002).OwnerSide);
        Assert.Equal(2, sat.Single(h => h.ArtifactId == 1002).OwnerSide);
        Assert.Equal(1, sat.Single(h => h.ArtifactId == 1003).OwnerSide);
    }

    /// <summary>Zone entry sends only that zone, with no count prefix. Both loads from the 08-28 run.</summary>
    [Theory]
    [InlineData(LowerZone0828, AbyssArtifactBuffCatalog.LowerZoneId, 1001, 1002, 1003)]
    [InlineData(MiddleZone0828, AbyssArtifactBuffCatalog.MiddleZoneId, 2001, 2002, 2003)]
    public void Zone_entry_frame_reads_one_zone(string hex, int zoneId, int first, int second, int third)
    {
        (AbyssArtifactZone[] zones, AbyssArtifactHolding[] holdings) =
            Parse(hex, SingleZoneBody, wholeAbyss: false);

        AbyssArtifactZone zone = Assert.Single(zones);
        Assert.Equal(zoneId, zone.ZoneId);
        Assert.Equal(new[] { first, second, third }, holdings.Select(h => h.ArtifactId).ToArray());
        Assert.Equal(new[] { 2, 1, 2 }, holdings.Select(h => h.OwnerSide).ToArray());
    }

    /// <summary>The zone frame and the whole-abyss frame agree, which is what lets either one alone be trusted:
    /// 0xE307 arrives at login and on a world-map open, 0xE305 only on the zone load.</summary>
    [Fact]
    public void Zone_frame_agrees_with_the_whole_abyss_frame()
    {
        (_, AbyssArtifactHolding[] all) = Parse(WholeAbyss0828, WholeAbyssBody, wholeAbyss: true);
        (_, AbyssArtifactHolding[] lower) = Parse(LowerZone0828, SingleZoneBody, wholeAbyss: false);
        (_, AbyssArtifactHolding[] middle) = Parse(MiddleZone0828, SingleZoneBody, wholeAbyss: false);

        foreach (AbyssArtifactHolding h in lower.Concat(middle))
        {
            Assert.Equal(all.Single(a => a.ArtifactId == h.ArtifactId).OwnerSide, h.OwnerSide);
        }
    }

    /// <summary>A frame whose declared counts do not consume the body is rejected WHOLE. One trailing byte is
    /// enough — the counts are the only self-validation this layout has.</summary>
    [Fact]
    public void Trailing_byte_rejects_the_frame()
    {
        byte[] packet = Convert.FromHexString(WholeAbyss0828 + "00");
        var zones = new AbyssArtifactZone[AbyssArtifactParser.MaxZones];
        var holdings = new AbyssArtifactHolding[AbyssArtifactParser.MaxArtifacts];

        Assert.Equal(-1, AbyssArtifactParser.TryParse(packet, WholeAbyssBody, true, zones, holdings, out _));
    }

    /// <summary>A truncated frame is rejected rather than reporting the artifacts it did manage to read: a
    /// partial answer here reads as "the rest are not held", which is a claim the bytes never made.</summary>
    [Fact]
    public void Truncated_frame_is_rejected()
    {
        string hex = WholeAbyss0828;
        byte[] packet = Convert.FromHexString(hex[..^34]);
        var zones = new AbyssArtifactZone[AbyssArtifactParser.MaxZones];
        var holdings = new AbyssArtifactHolding[AbyssArtifactParser.MaxArtifacts];

        Assert.Equal(-1, AbyssArtifactParser.TryParse(packet, WholeAbyssBody, true, zones, holdings, out _));
    }

    /// <summary>Reading a 0xE305 with the 0xE307 shape (or the reverse) must fail rather than half-succeed —
    /// the dispatcher picks the flag from the opcode and a mix-up would silently publish garbage owners.</summary>
    [Fact]
    public void Wrong_frame_shape_is_rejected()
    {
        byte[] zone = Convert.FromHexString(LowerZone0828);
        byte[] whole = Convert.FromHexString(WholeAbyss0828);
        var zones = new AbyssArtifactZone[AbyssArtifactParser.MaxZones];
        var holdings = new AbyssArtifactHolding[AbyssArtifactParser.MaxArtifacts];

        Assert.Equal(-1, AbyssArtifactParser.TryParse(zone, SingleZoneBody, true, zones, holdings, out _));
        Assert.Equal(-1, AbyssArtifactParser.TryParse(whole, WholeAbyssBody, false, zones, holdings, out _));
    }

    /// <summary>The six 아티팩트 점령 abnormals decode to (zone, count) with no value to read — the code IS the
    /// count. Neighbouring codes must not resolve, or an unrelated abnormal would restate the occupation.</summary>
    [Theory]
    [InlineData(12_000_261, AbyssArtifactBuffCatalog.LowerZoneId, 1)]
    [InlineData(12_000_262, AbyssArtifactBuffCatalog.LowerZoneId, 2)]
    [InlineData(12_000_263, AbyssArtifactBuffCatalog.LowerZoneId, 3)]
    [InlineData(12_000_264, AbyssArtifactBuffCatalog.MiddleZoneId, 1)]
    [InlineData(12_000_265, AbyssArtifactBuffCatalog.MiddleZoneId, 2)]
    [InlineData(12_000_266, AbyssArtifactBuffCatalog.MiddleZoneId, 3)]
    public void Artifact_count_abnormal_resolves(long code, int zoneId, int count)
    {
        Assert.True(AbyssArtifactBuffCatalog.TryResolve(code, out int gotZone, out int gotCount));
        Assert.Equal(zoneId, gotZone);
        Assert.Equal(count, gotCount);
    }

    [Theory]
    [InlineData(12_000_260)]
    [InlineData(12_000_267)]
    [InlineData(190_000_000)]
    [InlineData(0)]
    public void Unrelated_abnormal_does_not_resolve(long code)
    {
        Assert.False(AbyssArtifactBuffCatalog.TryResolve(code, out _, out _));
    }

    /// <summary>The measured cross-check that ties the two independent sources together: on 2026-08-28 the side
    /// the player's abnormals identify (2개 in each zone → slot 2) owns exactly the four artifacts whose
    /// corridors the 0x610B snapshot in the SAME capture reported 130000 ms on.</summary>
    [Fact]
    public void Owned_side_matches_the_ticket_snapshot_from_the_same_capture()
    {
        (_, AbyssArtifactHolding[] holdings) = Parse(WholeAbyss0828, WholeAbyssBody, wholeAbyss: true);

        // 하층 2개 + 중층 2개 → the slot holding two in each zone.
        AbyssArtifactBuffCatalog.TryResolve(12_000_262, out int lowerZone, out int lowerCount);
        AbyssArtifactBuffCatalog.TryResolve(12_000_265, out int middleZone, out int middleCount);

        int[] lower = holdings.Where(h => h.ArtifactId / 1000 == lowerZone / 1000).Select(h => h.OwnerSide).ToArray();
        int[] middle = holdings.Where(h => h.ArtifactId / 1000 == middleZone / 1000).Select(h => h.OwnerSide).ToArray();
        int ours = Assert.Single(
            new[] { 1, 2 },
            side => lower.Count(s => s == side) == lowerCount && middle.Count(s => s == side) == middleCount);

        Assert.Equal(2, ours);
        Assert.Equal(
            new[] { 1001, 1003, 2001, 2003 },
            holdings.Where(h => h.OwnerSide == ours).Select(h => h.ArtifactId).Order().ToArray());
    }
}

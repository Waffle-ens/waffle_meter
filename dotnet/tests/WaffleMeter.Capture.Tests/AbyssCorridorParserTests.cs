using WaffleMeter.Capture;
using Xunit;

namespace WaffleMeter.Capture.Tests;

/// <summary>
/// Golden spec for <see cref="AbyssCorridorParser"/> against real captured 0x610B/0x610C frames.
///
/// <para>These are the frames the three parsers next door would silently return NOTHING for: the corridor
/// record's field mask is <c>0x01</c> = one fixed <b>u64</b>, and every other 0x610x consumer only decodes the
/// varint bits (<c>0x04</c>/<c>0x08</c>). Reading <c>D0 FB 01 …</c> as a varint gives 26320 and misaligns the
/// rest of the record, which is why the assertion that matters most here is not any single value but that the
/// walk consumes the frame EXACTLY — a mis-shaped read cannot do that.</para>
///
/// <para>bodyStart = first byte after the two opcode bytes: 3 for the 1-byte-length deltas, 4 for the
/// 2-byte-length snapshot.</para>
/// </summary>
public class AbyssCorridorParserTests
{
    private const int DeltaBody = 3;
    private const int SnapshotBody = 4;

    private static AbyssCorridorTicket[] Parse(string hex, int bodyStart, bool fromSnapshot)
    {
        byte[] packet = Convert.FromHexString(hex);
        var buffer = new AbyssCorridorTicket[AbyssCorridorParser.MaxTickets];
        int count = AbyssCorridorParser.TryParse(packet, bodyStart, fromSnapshot, buffer);
        Assert.True(count >= 0, "frame did not walk cleanly");
        return buffer[..count];
    }

    /// <summary>Entering a corridor. Measured six times across two capture days — the value was 130000 every
    /// time, which is the client's <c>ContentsTicket.RechargeMaxTime = 130</c> seconds in milliseconds.</summary>
    [Theory]
    [InlineData("150C61000182969800D0FB01000000000003", 10_000_002)] // 08-19 18:08:55.798 / 08-17 08:38:34.575
    [InlineData("150C61000184969800D0FB01000000000003", 10_000_004)] // 08-19 18:11:42.655
    [InlineData("150C61000186969800D0FB01000000000003", 10_000_006)] // 08-19 18:15:41.362
    public void Entry_delta_reads_the_full_grant(string hex, int ticketId)
    {
        AbyssCorridorTicket[] tickets = Parse(hex, DeltaBody, fromSnapshot: false);

        AbyssCorridorTicket ticket = Assert.Single(tickets);
        Assert.Equal(ticketId, ticket.TicketId);
        Assert.Equal(130_000, ticket.RemainingMs);
        Assert.Equal(AbyssCorridorParser.FullGrantMs, ticket.RemainingMs);
    }

    /// <summary>The 130000 → 0 transition, and the whole reason this parser exists. The spent record carries NO
    /// fields at all (mask 0x00), so a parser that treats "no fields" as "no reading" reports a corridor that
    /// never runs out. Measured six times, 130.05~130.70 s after the matching entry.</summary>
    [Theory]
    [InlineData("0D0C6100008296980003", 10_000_002)] // 08-19 18:11:06.252 / 08-17 08:40:44.978
    [InlineData("0D0C6100008496980003", 10_000_004)] // 08-19 18:13:53.008
    [InlineData("0D0C6100008696980003", 10_000_006)] // 08-19 18:17:52.066
    public void Spent_delta_reads_as_zero_not_as_absent(string hex, int ticketId)
    {
        AbyssCorridorTicket[] tickets = Parse(hex, DeltaBody, fromSnapshot: false);

        AbyssCorridorTicket ticket = Assert.Single(tickets);
        Assert.Equal(ticketId, ticket.TicketId);
        Assert.Equal(0, ticket.RemainingMs);
    }

    /// <summary>A delta for an unrelated currency (id 3, 어비스 이용 시간, mask 0x03 = two u64 fields) walks
    /// cleanly and yields no corridor. "Understood the frame" and "found something" are different answers, and
    /// conflating them would make every neighbouring broadcast look like a parse failure.</summary>
    [Fact]
    public void An_unrelated_two_field_record_walks_but_yields_nothing()
    {
        byte[] packet = Convert.FromHexString("1D0C6100030300000078DE000300000000009749010000000003");
        var buffer = new AbyssCorridorTicket[AbyssCorridorParser.MaxTickets];

        Assert.Equal(0, AbyssCorridorParser.TryParse(packet, DeltaBody, fromSnapshot: false, buffer));
    }

    /// <summary>A frame whose lead byte is not a recognised option mask is rejected whole. Two such frames sit
    /// in the corpus (lead 0xD9 and 0x1E); a byte-scan for the currency id would have read them as corridor
    /// records, and the most likely accident — a stray 0x00 in front of a matching id — reads as "spent".</summary>
    [Theory]
    [InlineData("150C61D90182969800D0FB01000000000003")]
    [InlineData("150C611E0182969800D0FB01000000000003")]
    public void A_frame_with_an_unknown_lead_byte_is_rejected(string hex)
    {
        byte[] packet = Convert.FromHexString(hex);
        var buffer = new AbyssCorridorTicket[AbyssCorridorParser.MaxTickets];

        Assert.Equal(-1, AbyssCorridorParser.TryParse(packet, DeltaBody, fromSnapshot: false, buffer));
    }

    /// <summary>A delta with one byte too many is rejected: the tail check is what proves the layout was the one
    /// assumed. Same bytes as the entry frame plus a trailing 0x00.</summary>
    [Fact]
    public void A_delta_that_does_not_end_where_its_record_does_is_rejected()
    {
        byte[] packet = Convert.FromHexString("150C61000182969800D0FB0100000000000300");
        var buffer = new AbyssCorridorTicket[AbyssCorridorParser.MaxTickets];

        Assert.Equal(-1, AbyssCorridorParser.TryParse(packet, DeltaBody, fromSnapshot: false, buffer));
    }

    /// <summary>The real login snapshot from 2026-08-17 08:38:01.134 (character 콘팡, 534 bytes, 73 records).
    /// It declares its own record count and this walk has to land exactly on the last byte — the strongest
    /// available proof that the mask → field-width table is right, because 73 records of four different masks
    /// cannot all line up by accident. Three corridors were stocked at that moment; nine were not.</summary>
    private const string LoginSnapshot20260817 =
        "98040B614904010000000E0303000000B83BE6020000000000F36F060000000004040000000D000600000004070000002004" +
        "0800000007030900000088D77A0200000000202C530100000000040A0000000E040B00000002040C0000000E010D00000000" +
        "0B010300000000040E0000000E000F000000006500000004660000000704670000000701C9000000808580010000000001CA" +
        "000000808580010000000001CB000000808580010000000001CC000000808580010000000001CD0000008085800100000000" +
        "01CE000000808580010000000000819698000182969800D0FB01000000000000839698000184969800D0FB01000000000000" +
        "859698000186969800D0FB010000000000008796980000889698000089969800008A969800008B969800008C9698000C0187" +
        "93032DA0010C028793030E0604038793030E04048793030E04058793030E0C06879303150A0C07879303230504088793031C" +
        "04098793030A040A8793030704658793030E04C987930307044D8B93030704B18B93030E04358F93030704998F93030E041D" +
        "9393030704819393030E04059793030704699793030704B59B930304049D9F9303040CBDA2930307010421A393030E048DAA" +
        "93030704F1AA93030E0445B693030704A9B6930307042DBA9303070415BE93030704FDC193030604811D2C04030401B4C404" +
        "0304814A5D050400824A5D0504834A5D050400844A5D0504854A5D050400864A5D05";

    [Fact]
    public void Login_snapshot_lists_every_corridor_stocked_and_unstocked()
    {
        AbyssCorridorTicket[] tickets = Parse(LoginSnapshot20260817, SnapshotBody, fromSnapshot: true);

        // All twelve, in id order — the panel needs the zeroes too, because "not stocked" is an answer.
        Assert.Equal(12, tickets.Length);
        Assert.Equal(
            Enumerable.Range(AbyssCorridorParser.FirstTicketId, 12),
            tickets.Select(t => t.TicketId));

        Assert.Equal(
            [10_000_002, 10_000_004, 10_000_006],
            tickets.Where(t => t.RemainingMs > 0).Select(t => t.TicketId));
        Assert.All(tickets.Where(t => t.RemainingMs > 0), t => Assert.Equal(130_000, t.RemainingMs));
    }

    /// <summary>The same snapshot read as a delta must be rejected rather than half-parsed: the two frames put
    /// different things in their first body byte (a record count vs an option mask).</summary>
    [Fact]
    public void A_snapshot_read_with_the_delta_shape_is_rejected()
    {
        byte[] packet = Convert.FromHexString(LoginSnapshot20260817);
        var buffer = new AbyssCorridorTicket[AbyssCorridorParser.MaxTickets];

        Assert.Equal(-1, AbyssCorridorParser.TryParse(packet, SnapshotBody, fromSnapshot: false, buffer));
    }

    /// <summary>A caller that hands over too small a buffer gets a rejection, not a buffer overrun.</summary>
    [Fact]
    public void A_short_buffer_is_refused()
    {
        byte[] packet = Convert.FromHexString("150C61000182969800D0FB01000000000003");
        var buffer = new AbyssCorridorTicket[AbyssCorridorParser.MaxTickets - 1];

        Assert.Equal(-1, AbyssCorridorParser.TryParse(packet, DeltaBody, fromSnapshot: false, buffer));
    }
}

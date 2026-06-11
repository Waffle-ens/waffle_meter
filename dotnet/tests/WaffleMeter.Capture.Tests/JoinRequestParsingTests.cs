using WaffleMeter.Capture;
using Xunit;

namespace WaffleMeter.Capture.Tests;

/// <summary>
/// Parity spec for the six party join-request handlers (Kotlin parseJoinRequest / Cancel / Admit /
/// Refuse / InstanceStart / ExitParty). The JoinRequest golden frame is the byte-exact first packet
/// extracted from corpus 20260604-175315 (requester=94890, job=26→마도성, nickname="쿵해쫑",
/// server=1001, power=423359). Cancel/Admit pin the byte-exact Kotlin offset (offset+2 → u32); the
/// no-payload events use the real corpus frame shapes.
/// </summary>
public class JoinRequestParsingTests
{
    private sealed class RecordingJoinSink : IJoinRequestSink
    {
        public readonly List<(int Requester, string Nickname, int JobCode, int Server, int Power, long ArrivedAt)> Requests = [];
        public readonly List<int> Removed = [];
        public int Refused;
        public int Cleared;

        public void OnJoinRequest(int requester, string nickname, int jobCode, int server, int power, long arrivedAt)
            => Requests.Add((requester, nickname, jobCode, server, power, arrivedAt));
        public void OnJoinRequestRemove(int requester) => Removed.Add(requester);
        public void OnRefuseJoinRequest() => Refused++;
        public void OnExitPartyUi() => Cleared++;
    }

    private sealed class CountingSink : IStreamProcessorSink
    {
        public int ParserErrors;
        public void Dispatch(int opcode, string? opcodeName, bool extraFlag, int len) { }
        public void UnknownOpcode(int opcode, bool extraFlag, int len) { }
        public void CompressedPacket(int len, bool extraFlag) { }
        public void ParserError(string stage, string reason) => ParserErrors++;
        public void Damage(string kind, ParsedDamagePacket packet, bool saved, string? reason, int? mobCode) { }
        public void Meta(string type, params (string Key, object? Value)[] fields) { }
        public void Battle(int target, int toggle, int? mobCode, string? mobName, bool accepted, string? reason) { }
    }

    // Corpus-verified first JoinRequest frame (post-assembler, extraFlag=false). 59 bytes.
    private static readonly byte[] GoldenJoinRequest =
    [
        0x3e, 0x07, 0x97, 0x1e, 0x23, 0x02, 0x00, 0xaa, 0x72, 0x01, 0x00, 0x00, 0x00, 0xe9,
        0x03, 0x1a, 0x00, 0x00, 0x00, 0x2d, 0x00, 0x00, 0x00, 0xec, 0x0f, 0x00, 0x00, 0x09,
        0xec, 0xbf, 0xb5, 0xed, 0x95, 0xb4, 0xec, 0xab, 0x91, 0xe9, 0x03, 0x00, 0x00, 0x00,
        0x00, 0xbf, 0x75, 0x06, 0x00, 0x00, 0x00, 0x00, 0x00, 0x6d, 0x95, 0xd8, 0x91, 0x9e,
        0x01, 0x00, 0x00,
    ];

    private static (RecordingJoinSink Join, CountingSink Diag, StreamProcessor Proc) NewProcessor()
    {
        var join = new RecordingJoinSink();
        var diag = new CountingSink();
        return (join, diag, new StreamProcessor(diag, null, join));
    }

    [Fact]
    public void ParseJoinRequest_extracts_all_fields()
    {
        var (join, _, proc) = NewProcessor();
        proc.OnPacketReceived(GoldenJoinRequest, 1717_000_000);

        Assert.Single(join.Requests);
        var r = join.Requests[0];
        Assert.Equal(94890, r.Requester);
        Assert.Equal("쿵해쫑", r.Nickname);
        Assert.Equal(26, r.JobCode);   // JobClass.ConvertFromCode(26) == SORCERER (마도성)
        Assert.Equal(1001, r.Server);
        Assert.Equal(423359, r.Power);
        Assert.Equal(1717_000_000, r.ArrivedAt);
    }

    [Fact]
    public void ParseCancelJoin_emits_remove_with_verbatim_offset()
    {
        var (join, _, proc) = NewProcessor();
        // [len=8][0x25][0x97][requester=94890 u32 LE]
        proc.OnPacketReceived([0x08, 0x25, 0x97, 0xaa, 0x72, 0x01, 0x00], 0);
        Assert.Equal([94890], join.Removed);
    }

    [Fact]
    public void ParseAdmitJoin_emits_remove_with_verbatim_offset()
    {
        var (join, _, proc) = NewProcessor();
        // [len=8][0x0B][0x97][requester=94890 u32 LE]
        proc.OnPacketReceived([0x08, 0x0B, 0x97, 0xaa, 0x72, 0x01, 0x00], 0);
        Assert.Equal([94890], join.Removed);
    }

    [Fact]
    public void ParseRefuseJoin_emits_refuse()
    {
        var (join, _, proc) = NewProcessor();
        proc.OnPacketReceived([0x08, 0x09, 0x97, 0x00, 0x00], 0); // real corpus shape
        Assert.Equal(1, join.Refused);
    }

    [Fact]
    public void ParseInstanceStart_clears_all()
    {
        var (join, _, proc) = NewProcessor();
        proc.OnPacketReceived([0x08, 0x18, 0x97, 0x00, 0x00], 0);
        Assert.Equal(1, join.Cleared);
    }

    [Fact]
    public void ParseExitParty_clears_all()
    {
        var (join, _, proc) = NewProcessor();
        proc.OnPacketReceived([0x08, 0x1D, 0x97, 0x00, 0x00], 0);
        Assert.Equal(1, join.Cleared);
    }

    [Fact]
    public void Truncated_join_request_is_swallowed_not_thrown()
    {
        var (join, diag, proc) = NewProcessor();
        // Golden frame cut to 43 bytes — the power u32 read runs off the end.
        byte[] truncated = GoldenJoinRequest[..43];

        Exception? ex = Record.Exception(() => proc.OnPacketReceived(truncated, 0));

        Assert.Null(ex);                  // must not crash the consumer thread
        Assert.Empty(join.Requests);      // no partial request emitted
        Assert.Equal(1, diag.ParserErrors);
    }
}

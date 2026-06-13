using WaffleMeter.Capture;
using WaffleMeter.Capture.Live;
using Xunit;

namespace WaffleMeter.Capture.Live.Tests;

/// <summary>
/// Locks the load-bearing invariant of the P2P/streaming noise guard: the ConnKey the APP derives from a
/// segment's dotted-quad strings (<see cref="ConnKey.TryFrom"/>) must EQUAL the one the HELPER reads
/// straight from the raw IPv4+TCP header (<see cref="WinDivertBackend.TryReadConnKey"/>), or excludes
/// never match and the feature is silently dead. Uses high octets/ports (&gt;=128 / &gt;=32768) to catch
/// any sign/byte-order bug.
/// </summary>
public sealed class ConnKeyTests
{
    [Fact]
    public void App_and_helper_derive_the_same_ConnKey()
    {
        // src 206.127.156.35:13328  ->  dst 192.168.0.70:51355  (high octet 206/192, high port 51355)
        const string srcIp = "206.127.156.35";
        const string dstIp = "192.168.0.70";
        const int srcPort = 13328;
        const int dstPort = 51355;

        // Helper path: a minimal IPv4(20)+TCP header carrying that 4-tuple.
        var header = new byte[24];
        header[0] = 0x45;                 // IPv4, IHL=5 (20-byte header)
        header[9] = 6;                    // protocol = TCP
        header[12] = 206; header[13] = 127; header[14] = 156; header[15] = 35;  // src IP
        header[16] = 192; header[17] = 168; header[18] = 0; header[19] = 70;     // dst IP
        header[20] = (byte)(srcPort >> 8); header[21] = (byte)(srcPort & 0xFF);   // src port (BE)
        header[22] = (byte)(dstPort >> 8); header[23] = (byte)(dstPort & 0xFF);   // dst port (BE)

        Assert.True(WinDivertBackend.TryReadConnKey(header, header.Length, out ConnKey fromHeader));

        // App path: the same 4-tuple as a captured segment's strings/ints.
        var seg = new CapturedSegment(0, System.Array.Empty<byte>(), 0, srcIp, srcPort, dstIp, dstPort);
        Assert.True(ConnKey.TryFrom(seg, out ConnKey fromSegment));

        Assert.Equal(fromSegment, fromHeader); // MUST agree, else the guard never drops anything
    }

    [Fact]
    public void ConnKey_survives_a_wire_round_trip()
    {
        var key = new ConnKey(0xCE7F9C23, 13328, 0xC0A80046, 51355);
        ConnKey back = CaptureWireProtocol.DecodeConnKey(CaptureWireProtocol.EncodeConnKey(key));
        Assert.Equal(key, back);
    }

    [Fact]
    public void TryReadConnKey_rejects_non_ipv4_and_non_tcp()
    {
        var udp = new byte[24];
        udp[0] = 0x45; udp[9] = 17; // UDP
        Assert.False(WinDivertBackend.TryReadConnKey(udp, udp.Length, out _));

        var ipv6 = new byte[24];
        ipv6[0] = 0x60; // version 6
        Assert.False(WinDivertBackend.TryReadConnKey(ipv6, ipv6.Length, out _));

        Assert.False(WinDivertBackend.TryReadConnKey(new byte[10], 10, out _)); // truncated
    }

    [Fact]
    public void LooksLikeGamePacket_accepts_known_opcode_and_lz4_and_rejects_text()
    {
        // [len varint][04 38] => opcode 0x3804 (Damage), a known game opcode.
        Assert.True(StreamProcessor.LooksLikeGamePacket(new byte[] { 0x0A, 0x04, 0x38, 0x00, 0x00 }));

        // [len varint][FF FF ...] => LZ4-compressed game packet.
        Assert.True(StreamProcessor.LooksLikeGamePacket(new byte[] { 0x0A, 0xFF, 0xFF, 0x00, 0x00 }));

        // Text bytes ("hell") that frame to an unknown opcode key => not a game packet.
        Assert.False(StreamProcessor.LooksLikeGamePacket(new byte[] { 0x0A, 0x68, 0x65, 0x6C, 0x6C }));
    }
}

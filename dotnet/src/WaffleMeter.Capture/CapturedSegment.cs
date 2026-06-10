namespace WaffleMeter.Capture;

/// <summary>
/// One TCP segment as seen at capture time, BEFORE sequence-ordering and framing.
/// This is the single uniform contract that every source produces:
///   - the live WinDivert backend (default, embedded),
///   - the live Npcap/SharpPcap backend (optional),
///   - the Phase 0 corpus replay (<see cref="Corpus.CaptureCorpusReader"/>).
/// The live elevated capture helper ships exactly these fields over the named pipe
/// (see docs/wpf-migration-plan.md §0.2), so live and replay share one downstream path.
/// </summary>
/// <param name="Seq">
/// TCP sequence number masked to 32 bits. Mirrors Kotlin <c>PcapCapturer.kt:71</c>
/// (<c>sequenceNumber.toLong() and 0xffffffffL</c>); kept as a wrap-safe <see cref="long"/>.
/// </param>
/// <param name="Payload">Raw TCP payload bytes (no Ethernet/IP/TCP headers).</param>
/// <param name="ArrivedAtMs">
/// Wall-clock capture time (<c>System.currentTimeMillis()</c> equivalent), stamped closest to
/// the wire. Flows into ParsedDamagePacket.timestamp → battleStart/End → DPS/uptime, so it is
/// always CONSUMED downstream and NEVER regenerated (see docs/phase-0-parity-harness.md §7).
/// </param>
/// <param name="SrcIp">Source IP string (the <c>ip</c> field of Kotlin CapturedPacket).</param>
/// <param name="SrcPort">TCP source port.</param>
/// <param name="DstIp">Destination IP string.</param>
/// <param name="DstPort">TCP destination port.</param>
/// <remarks>
/// Carries the full TCP 4-tuple (Kotlin CapturedPacket after dev commit d00c850). The downstream
/// consumer demuxes streams by <see cref="StreamKey"/> — accelerator/VPN users route the game over a
/// local proxy on the loopback adapter with dynamic ports where multiple connections share a srcPort,
/// so srcIP-only keying merged distinct connections and the aligner discarded the plaintext game
/// stream. The game stream is never targeted by IP/port; it is identified purely by opcode content.
/// </remarks>
public readonly record struct CapturedSegment(
    long Seq,
    byte[] Payload,
    long ArrivedAtMs,
    string SrcIp,
    int SrcPort = 0,
    string DstIp = "",
    int DstPort = 0)
{
    /// <summary>Per-connection stream key (Kotlin "$ip:$srcPort-$dstIp:$dstPort").</summary>
    public string StreamKey => $"{SrcIp}:{SrcPort}-{DstIp}:{DstPort}";
}

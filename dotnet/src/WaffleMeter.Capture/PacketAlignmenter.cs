namespace WaffleMeter.Capture;

/// <summary>An ordered, contiguous chunk emitted by <see cref="PacketAlignmenter"/>.</summary>
/// <param name="Data">Payload bytes, in TCP-sequence order.</param>
/// <param name="ArrivedAt">Capture wall-clock carried through from the segment.</param>
public readonly record struct AlignedChunk(byte[] Data, long ArrivedAt);

/// <summary>
/// Verbatim port of Kotlin <c>PacketAlignmenter</c> (src/main/kotlin/packet/PacketAlignmenter.kt).
/// Holds out-of-order TCP segments in a sequence-ordered map until they become contiguous,
/// drops pure retransmits (seq below the next expected), and stalls forward progress on a
/// permanent gap.
///
/// CORRECTNESS-CRITICAL: a divergence here silently mis-counts damage rather than crashing.
/// Behavior — including the 32-bit wrap arithmetic and its edge quirks — must match Kotlin
/// EXACTLY. Do not "improve" the gap/retransmit/wrap logic here; any fix belongs behind a flag
/// after parity is proven (see docs/wpf-migration-plan.md risk register).
/// </summary>
public sealed class PacketAlignmenter
{
    // Kotlin: TreeMap<Long, Pair<ByteArray, Long>> — SortedDictionary keeps keys ascending,
    // so .Keys.First() == TreeMap.firstKey() (smallest key).
    private readonly SortedDictionary<long, AlignedChunk> _holdBuffer = new();

    // Kotlin: -1L sentinel meaning "not yet initialized".
    private long _nextExpectedSeq = -1L;

    // Bytes currently held (out-of-order, not-yet-contiguous). Above MaxHoldBytes the gap is treated as
    // permanent (a capture-dropped segment SNIFF will never re-observe) and skipped, so a stalled stream
    // can't grow the hold buffer without bound. Well above any legitimate game reorder window — only a
    // truly-stalled (usually high-volume non-game) stream reaches it. Mirrors PacketAccumulator's 2MB reset.
    private const long MaxHoldBytes = 2_000_000;
    private long _heldBytes;

    /// <summary>Diagnostic only (non-behavioral): count of permanent-gap SKIPS — a capture-dropped segment
    /// window that was discarded to re-sync the stream. A rising count during a crowded fight is direct
    /// evidence of capture-layer loss on this stream (which, for the refresh-only buff overlay, shows up as
    /// buffs expiring early / flickering / never appearing). Read via <c>MeterServices.AlignerGapSkips</c>.</summary>
    public long GapSkips { get; private set; }

    /// <summary>
    /// Feed one captured segment. Returns the chunks (possibly none, possibly several) that
    /// became contiguous as a result, in sequence order.
    /// </summary>
    public IReadOnlyList<AlignedChunk> Feed(long seq, byte[] data, long arrivedAt)
    {
        if (_nextExpectedSeq == -1L)
        {
            _nextExpectedSeq = seq;
        }

        // Kotlin uses `holdBuffer[seq] = ...` which overwrites on duplicate seq; indexer matches.
        if (_holdBuffer.TryGetValue(seq, out AlignedChunk previous))
        {
            _heldBytes -= previous.Data.Length; // overwriting a duplicate seq: drop its byte count first
        }

        _holdBuffer[seq] = new AlignedChunk(data, arrivedAt);
        _heldBytes += data.Length;

        var result = new List<AlignedChunk>();

        while (_holdBuffer.Count > 0)
        {
            long firstSeq = _holdBuffer.Keys.First();

            if (firstSeq == _nextExpectedSeq)
            {
                AlignedChunk chunk = _holdBuffer[firstSeq];
                _holdBuffer.Remove(firstSeq);
                _heldBytes -= chunk.Data.Length;
                _nextExpectedSeq = (_nextExpectedSeq + chunk.Data.Length) & 0xffffffffL;
                result.Add(chunk);
            }
            else if (firstSeq < _nextExpectedSeq)
            {
                // Pure retransmit / already-consumed range: drop it.
                _heldBytes -= _holdBuffer[firstSeq].Data.Length;
                _holdBuffer.Remove(firstSeq);
            }
            else
            {
                // Gap: the next expected bytes have not arrived yet. Normally STALL and wait. But once the
                // hold buffer has grown past MaxHoldBytes the gap is treated as permanent (a capture-dropped
                // segment SNIFF will never re-observe): SKIP it — re-sync _nextExpectedSeq to the smallest
                // held seq and resume. This bounds memory AND recovers the stream (a stalled game stream
                // resumes emitting -> DPS recovers; a noise stream resumes emitting -> the noise guard can
                // then exclude it). Fires ONLY above the cap, so the normal in-order/reorder arithmetic is
                // unchanged below it (DPS parity preserved).
                if (_heldBytes > MaxHoldBytes)
                {
                    GapSkips++; // diagnostic tally only; the re-sync behavior below is unchanged
                    _nextExpectedSeq = firstSeq;
                    continue;
                }

                break;
            }
        }

        return result;
    }

    /// <summary>Reset on source-IP change (Kotlin Main.kt:43-46) — clear holds, re-init next-expected.</summary>
    public void Reset()
    {
        _holdBuffer.Clear();
        _heldBytes = 0;
        _nextExpectedSeq = -1L;
    }
}

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
        _holdBuffer[seq] = new AlignedChunk(data, arrivedAt);

        var result = new List<AlignedChunk>();

        while (_holdBuffer.Count > 0)
        {
            long firstSeq = _holdBuffer.Keys.First();

            if (firstSeq == _nextExpectedSeq)
            {
                AlignedChunk chunk = _holdBuffer[firstSeq];
                _holdBuffer.Remove(firstSeq);
                _nextExpectedSeq = (_nextExpectedSeq + chunk.Data.Length) & 0xffffffffL;
                result.Add(chunk);
            }
            else if (firstSeq < _nextExpectedSeq)
            {
                // Pure retransmit / already-consumed range: drop it.
                _holdBuffer.Remove(firstSeq);
            }
            else
            {
                // Gap: the next expected bytes have not arrived yet. Stall.
                break;
            }
        }

        return result;
    }

    /// <summary>Reset on source-IP change (Kotlin Main.kt:43-46) — clear holds, re-init next-expected.</summary>
    public void Reset()
    {
        _holdBuffer.Clear();
        _nextExpectedSeq = -1L;
    }
}

using WaffleMeter.Capture;
using Xunit;

namespace WaffleMeter.Capture.Tests;

/// <summary>
/// Parity spec for the verbatim <see cref="PacketAlignmenter"/> port. Each case encodes the
/// documented behavior of Kotlin <c>PacketAlignmenter.feed</c>
/// (src/main/kotlin/packet/PacketAlignmenter.kt). These are hand-authored synthetic cases —
/// they need NO live corpus and cover the situations that almost never appear in a short live
/// capture (seq-wrap, reorder, retransmit, permanent gap), per docs/phase-0-parity-harness.md §5.
///
/// Chunks are identified by their <c>ArrivedAt</c>, used here as a unique id, so assertions read
/// as "which segments were emitted, in what order".
/// </summary>
public class PacketAlignmenterTests
{
    private static long[] Ids(IReadOnlyList<AlignedChunk> emitted)
        => emitted.Select(c => c.ArrivedAt).ToArray();

    private static IReadOnlyList<AlignedChunk> Feed(PacketAlignmenter a, long seq, int len, long id)
        => a.Feed(seq, new byte[len], id);

    [Fact]
    public void First_segment_sets_next_expected_and_emits_in_order()
    {
        var a = new PacketAlignmenter();
        Assert.Equal(new[] { 1L }, Ids(Feed(a, 0, 10, 1)));   // first seq initializes nextExpected
        Assert.Equal(new[] { 2L }, Ids(Feed(a, 10, 10, 2)));  // contiguous
    }

    [Fact]
    public void Holds_out_of_order_segment_until_the_gap_is_filled()
    {
        var a = new PacketAlignmenter();
        Assert.Equal(new[] { 1L }, Ids(Feed(a, 0, 10, 1)));        // next = 10
        Assert.Empty(Feed(a, 20, 10, 2));                          // 20 > 10 -> held
        Assert.Equal(new[] { 3L, 2L }, Ids(Feed(a, 10, 10, 3)));   // fill 10, then flush held 20
    }

    [Fact]
    public void Drops_pure_retransmit_below_next_expected()
    {
        var a = new PacketAlignmenter();
        Assert.Equal(new[] { 1L }, Ids(Feed(a, 0, 10, 1)));   // next = 10
        Assert.Empty(Feed(a, 0, 10, 2));                      // 0 < 10 -> dropped, nothing emitted
    }

    [Fact]
    public void Stalls_on_permanent_gap_then_flushes_in_order_once_filled()
    {
        var a = new PacketAlignmenter();
        Assert.Equal(new[] { 1L }, Ids(Feed(a, 0, 10, 1)));        // next = 10
        Assert.Empty(Feed(a, 30, 10, 2));                          // held (gap at 10..30)
        Assert.Empty(Feed(a, 50, 10, 3));                          // held
        Assert.Equal(new[] { 4L }, Ids(Feed(a, 10, 10, 4)));       // next=20; firstKey 30>20 -> stop
        Assert.Equal(new[] { 5L, 2L }, Ids(Feed(a, 20, 10, 5)));   // 20 -> next=30; flush 30(id2); 50>40 stop
        Assert.Equal(new[] { 6L, 3L }, Ids(Feed(a, 40, 10, 6)));   // 40 -> next=50; flush 50(id3)
    }

    [Fact]
    public void Handles_32bit_sequence_wrap()
    {
        var a = new PacketAlignmenter();
        // 0xFFFFFFF0 + 0x10 = 0x1_0000_0000, masked by 0xffffffffL -> 0x0000_0000
        Assert.Equal(new[] { 1L }, Ids(Feed(a, 0xFFFFFFF0L, 0x10, 1)));
        Assert.Equal(new[] { 2L }, Ids(Feed(a, 0x0L, 4, 2)));   // next had wrapped to 0
        Assert.Equal(new[] { 3L }, Ids(Feed(a, 0x4L, 4, 3)));
    }

    [Fact]
    public void Reset_reinitializes_next_expected_to_the_following_first_seq()
    {
        var a = new PacketAlignmenter();
        Assert.Equal(new[] { 1L }, Ids(Feed(a, 100, 10, 1)));
        a.Reset();
        Assert.Equal(new[] { 2L }, Ids(Feed(a, 500, 10, 2)));   // next re-initializes to 500
    }
}

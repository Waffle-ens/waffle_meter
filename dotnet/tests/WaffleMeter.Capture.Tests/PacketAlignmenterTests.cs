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
    public void Skips_a_permanent_gap_once_the_hold_buffer_exceeds_the_cap()
    {
        // Anti-leak guard: a stream permanently stalled on a gap SNIFF never re-observes must not grow the
        // hold buffer without bound. Below the 2MB cap it stalls (parity behavior); above it, the gap is
        // skipped and the buffer drains.
        var a = new PacketAlignmenter();
        Assert.Equal(new[] { 1L }, Ids(Feed(a, 0, 10, 1)));   // next = 10

        Assert.Empty(Feed(a, 1_000, 1_000_000, 2));           // held (gap at 10); heldBytes = 1,000,000
        Assert.Empty(Feed(a, 5_000_000, 1_000_000, 3));       // held; heldBytes = 2,000,000 (== cap, no skip)

        // This push takes heldBytes PAST 2MB -> the gap is treated as permanent and skipped: the aligner
        // re-syncs to the smallest held seq and drains it, instead of holding forever / growing unbounded.
        long[] emitted = Ids(Feed(a, 9_000_000, 1_000_000, 4));
        Assert.NotEmpty(emitted);
        Assert.Equal(2L, emitted[0]);                         // oldest held segment released first
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

    [Fact]
    public void Gap_open_timestamp_marks_when_the_stall_began_and_clears_on_progress()
    {
        // Observability for the app's stream-scoped self-heal: the 2MB cap above is a memory bound, not a
        // recovery path (it needs 8-28 minutes of traffic at a real game connection's rate), so the app needs
        // to see HOW LONG a stream has been emitting nothing. Diagnostic only — emission is unchanged.
        var a = new PacketAlignmenter();
        Assert.Null(a.GapOpenAtMs);

        Assert.Equal(new[] { 1L }, Ids(Feed(a, 0, 10, 1)));
        Assert.Null(a.GapOpenAtMs);                             // in order: never stalled

        Assert.Empty(Feed(a, 100, 10, 5_000));                  // gap at 10
        Assert.Equal(5_000L, a.GapOpenAtMs);
        Assert.Equal(10, a.HeldBytes);

        Assert.Empty(Feed(a, 110, 10, 9_000));                  // still stalled: keeps the ORIGINAL open time
        Assert.Equal(5_000L, a.GapOpenAtMs);
        Assert.Equal(20, a.HeldBytes);

        Assert.Equal(3, Feed(a, 10, 90, 9_500).Count);          // hole filled -> 90 + both held chunks drain
        Assert.Null(a.GapOpenAtMs);
        Assert.Equal(0, a.HeldBytes);
    }

    [Fact]
    public void Reset_clears_the_stall_marker()
    {
        var a = new PacketAlignmenter();
        Assert.Equal(new[] { 1L }, Ids(Feed(a, 0, 10, 1)));
        Assert.Empty(Feed(a, 100, 10, 2));
        Assert.NotNull(a.GapOpenAtMs);

        a.Reset();

        Assert.Null(a.GapOpenAtMs);
        Assert.Equal(0, a.HeldBytes);
    }
}

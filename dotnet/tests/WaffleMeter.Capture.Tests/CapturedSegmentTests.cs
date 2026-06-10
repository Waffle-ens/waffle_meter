using WaffleMeter.Capture;
using Xunit;

namespace WaffleMeter.Capture.Tests;

public sealed class CapturedSegmentTests
{
    [Fact]
    public void StreamKey_is_the_full_4_tuple()
    {
        var seg = new CapturedSegment(1, new byte[] { 1 }, 0, "127.0.0.1", 51234, "206.127.156.10", 13328);
        Assert.Equal("127.0.0.1:51234-206.127.156.10:13328", seg.StreamKey);
    }

    [Fact]
    public void Tuple_defaults_keep_corpus_segments_on_one_stream()
    {
        // The corpus log carries only ip/seq/data/at, so replayed segments use the default tuple and
        // collapse to a single stream key (the pre-rework single-stream parity baseline).
        var a = new CapturedSegment(1, new byte[] { 1 }, 0, "10.0.0.1");
        var b = new CapturedSegment(2, new byte[] { 2 }, 1, "10.0.0.1");
        Assert.Equal(a.StreamKey, b.StreamKey);
        Assert.Equal("10.0.0.1:0-:0", a.StreamKey);
    }
}

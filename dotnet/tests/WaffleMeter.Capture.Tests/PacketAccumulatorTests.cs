using WaffleMeter.Capture;
using Xunit;

namespace WaffleMeter.Capture.Tests;

/// <summary>Behavior spec for the verbatim <see cref="PacketAccumulator"/> port.</summary>
public class PacketAccumulatorTests
{
    private static byte[] Pattern(int start, int len)
    {
        var b = new byte[len];
        for (int i = 0; i < len; i++)
        {
            b[i] = (byte)(start + i);
        }

        return b;
    }

    [Fact]
    public void Append_tracks_size_and_peek_slice_return_data()
    {
        var acc = new PacketAccumulator();
        acc.Append(new byte[] { 1, 2, 3, 4, 5 });

        Assert.Equal(5, acc.Size);
        Assert.Equal(new byte[] { 1, 2, 3 }, acc.Peek(3));
        Assert.Equal(new byte[] { 2, 3 }, acc.Slice(1, 2));
    }

    [Fact]
    public void Peek_caps_at_available_bytes()
    {
        var acc = new PacketAccumulator();
        acc.Append(new byte[] { 9, 8 });
        Assert.Equal(new byte[] { 9, 8 }, acc.Peek(8));
    }

    [Fact]
    public void Slice_out_of_range_returns_empty()
    {
        var acc = new PacketAccumulator();
        acc.Append(new byte[] { 1, 2, 3 });
        Assert.Empty(acc.Slice(1, 10));
    }

    [Fact]
    public void DiscardBytes_advances_read_cursor()
    {
        var acc = new PacketAccumulator();
        acc.Append(new byte[] { 1, 2, 3, 4 });
        acc.DiscardBytes(2);
        Assert.Equal(2, acc.Size);
        Assert.Equal(new byte[] { 3, 4 }, acc.Peek(8));
    }

    [Fact]
    public void Flush_resets()
    {
        var acc = new PacketAccumulator();
        acc.Append(new byte[] { 1, 2, 3 });
        acc.Flush();
        Assert.Equal(0, acc.Size);
        Assert.Empty(acc.Peek(8));
    }

    [Fact]
    public void IndexOf_finds_pattern_relative_to_read_cursor()
    {
        var acc = new PacketAccumulator();
        acc.Append(new byte[] { 0xAA, 0xBB, 0xFF, 0xFF, 0xCC });
        Assert.Equal(2, acc.IndexOf(new byte[] { 0xFF, 0xFF }));
        acc.DiscardBytes(1); // read cursor now at 0xBB
        Assert.Equal(1, acc.IndexOf(new byte[] { 0xFF, 0xFF }));
        Assert.Equal(-1, acc.IndexOf(new byte[] { 0x12, 0x34 }));
    }

    [Fact]
    public void Grows_beyond_initial_64kb_capacity_preserving_data()
    {
        var acc = new PacketAccumulator();
        byte[] a = Pattern(0, 50_000);
        byte[] b = Pattern(50, 50_000);
        acc.Append(a);
        acc.Append(b); // crosses 64KB initial capacity -> grow

        Assert.Equal(100_000, acc.Size);
        Assert.Equal(a, acc.Slice(0, 50_000));
        Assert.Equal(b, acc.Slice(50_000, 50_000));
    }

    [Fact]
    public void Compacts_when_read_cursor_passes_half_then_keeps_data_intact()
    {
        var acc = new PacketAccumulator();
        byte[] a = Pattern(0, 40_000);
        acc.Append(a);
        acc.DiscardBytes(35_000); // readPos 35000 >= 64KB/2 (32768) -> Compact()

        Assert.Equal(5_000, acc.Size);
        Assert.Equal(a[35_000..40_000], acc.Peek(5_000));

        byte[] b = Pattern(200, 3_000);
        acc.Append(b);
        Assert.Equal(8_000, acc.Size);
        Assert.Equal(a[35_000..40_000], acc.Slice(0, 5_000));
        Assert.Equal(b, acc.Slice(5_000, 3_000));
    }

    [Fact]
    public void Force_resets_when_size_reaches_2mb()
    {
        var acc = new PacketAccumulator();
        acc.Append(new byte[2 * 1024 * 1024]); // size == 2MB (MAX)
        Assert.Equal(2 * 1024 * 1024, acc.Size);

        acc.Append(new byte[] { 1 }); // currentSize >= MAX -> force reset, incoming dropped
        Assert.Equal(0, acc.Size);
    }
}

using WaffleMeter.Capture;
using Xunit;

namespace WaffleMeter.Capture.Tests;

/// <summary>Spec for <see cref="FieldBossTimerParser"/> against the 0x9101 timer broadcast layout
/// <c>[.. var-int bossCode .. int64-LE targetMs ..]</c>, verified on a real capture.</summary>
public class FieldBossTimerParserTests
{
    // From a real 0x9101 body: entry count (0C 00), then boss 2406034 (파르곤) as var-int 92 ED 92 01,
    // a 1-byte gap (00), then Int64-LE target ms 0E 11 B1 2B 9F 01 00 00 = 1,783,144,452,366.
    private const long RealTarget = 0x0000019F2BB1110EL;

    [Fact]
    public void Parses_a_boss_code_and_target_time()
    {
        byte[] body =
        {
            0x0C, 0x00,                                     // entry count 12
            0x92, 0xED, 0x92, 0x01,                          // var-int 2406034
            0x00,                                            // 1-byte gap
            0x0E, 0x11, 0xB1, 0x2B, 0x9F, 0x01, 0x00, 0x00,  // int64-LE target
        };
        // arrivedAt ~43 min before the target (the record's window)
        long arrivedAt = RealTarget - 43 * 60_000L;

        IReadOnlyList<(int Code, long TargetMs)> r = FieldBossTimerParser.Parse(body, 0, arrivedAt);

        (int code, long target) = Assert.Single(r);
        Assert.Equal(2406034, code);
        Assert.Equal(RealTarget, target);
    }

    [Fact]
    public void Rejects_a_target_time_outside_the_sane_window()
    {
        byte[] body = { 0x92, 0xED, 0x92, 0x01, 0x00, 0x0E, 0x11, 0xB1, 0x2B, 0x9F, 0x01, 0x00, 0x00 };
        // "now" is a week past the target → not a plausible future respawn → dropped
        long arrivedAt = RealTarget + 7 * 24 * 60 * 60_000L;
        Assert.Empty(FieldBossTimerParser.Parse(body, 0, arrivedAt));
    }

    [Fact]
    public void Ignores_var_ints_outside_the_boss_code_range()
    {
        // a small var-int (5) followed by 8 bytes that happen to be a valid time must NOT parse as a boss
        byte[] body = { 0x05, 0x00, 0x0E, 0x11, 0xB1, 0x2B, 0x9F, 0x01, 0x00, 0x00 };
        Assert.Empty(FieldBossTimerParser.Parse(body, 0, RealTarget - 60_000L));
    }
}

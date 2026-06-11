using WaffleMeter.App.Core;
using WaffleMeter.Capture;
using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

public class BattleHistoryTests
{
    private static (int, DpsReport) Battle(int index, string mob, bool boss, long start, long end, params double[] amounts)
    {
        var info = new Dictionary<int, DpsInformation>();
        for (int i = 0; i < amounts.Length; i++)
        {
            info[i + 1] = new DpsInformation(amounts[i], 0, 0, 0);
        }

        return (index, new DpsReport
        {
            BattleStart = start,
            BattleEnd = end,
            Target = new MobInfo(index + 1, new Mob(100 + index, mob, boss)),
            Information = info,
        });
    }

    [Fact]
    public void Build_maps_fields_and_orders_newest_first()
    {
        var battles = new[]
        {
            Battle(0, "잡몹", false, 1000, 1000 + 30_000, 100, 50),   // oldest
            Battle(1, "보스", true, 100_000, 100_000 + 90_000, 500, 300), // newest
        };

        var items = BattleHistory.Build(battles);

        Assert.Equal(2, items.Count);
        // newest first
        Assert.Equal("보스", items[0].MobName);
        Assert.True(items[0].IsBoss);
        Assert.Equal(800, items[0].TotalAmount);
        Assert.Equal(90_000, items[0].BattleTimeMs);
        Assert.Equal(100_000, items[0].BattleStartMs);
        Assert.Equal(1, items[0].Index);

        Assert.Equal("잡몹", items[1].MobName);
        Assert.False(items[1].IsBoss);
        Assert.Equal(150, items[1].TotalAmount);
    }

    [Fact]
    public void Build_drops_zero_duration_battles()
    {
        var battles = new[]
        {
            Battle(0, "유효", false, 1000, 5000, 10),
            Battle(1, "즉사", false, 2000, 2000, 10), // battleTime == 0 -> dropped
        };

        var items = BattleHistory.Build(battles);

        Assert.Single(items);
        Assert.Equal("유효", items[0].MobName);
    }

    [Fact]
    public void Build_uses_fallback_mob_name_when_target_missing()
    {
        var report = new DpsReport { BattleStart = 1000, BattleEnd = 5000, Target = null };
        var items = BattleHistory.Build([(0, report)]);
        Assert.Single(items);
        Assert.Equal("알 수 없음", items[0].MobName);
    }

    [Theory]
    [InlineData(0, "00:00")]
    [InlineData(-5, "00:00")]
    [InlineData(90_000, "01:30")]
    [InlineData(59_999, "00:59")]
    [InlineData(3_600_000, "60:00")]
    public void FormatBattleTime_is_mm_ss(long ms, string expected)
        => Assert.Equal(expected, MeterFormat.FormatBattleTime(ms));
}

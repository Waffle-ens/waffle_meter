using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>Covers the saved-battle history cap (raised 12 -> 30 so the scrollable history panel can show more).</summary>
public sealed class BattleLogRepositoryTests
{
    [Fact]
    public void History_keeps_at_most_30_battles_dropping_the_oldest()
    {
        var repo = new BattleLogRepository();
        for (int i = 1; i <= 31; i++)
        {
            // Target null => IsSameBattle is always false => no merge, each Save is a distinct battle.
            var report = new DpsReport { BattleStart = i * 1000, BattleEnd = i * 1000 + 500 };
            report.Information[1] = new DpsInformation(100, 0, 0, 0);
            repo.Save(new DpsLog { Report = report });
        }

        Assert.Equal(30, repo.GetAll().Count);                 // capped at 30
        Assert.Equal(2000, repo.GetAll()[0].Report.BattleStart); // the first (1000) was dropped; oldest now 2000
    }
}

using WaffleMeter.Data;

namespace WaffleMeter.App.Core;

/// <summary>One saved battle as shown in the history panel (port of React useHistory HistoryItem).
/// <see cref="Index"/> is the repository index used to re-open the full log.</summary>
public sealed record BattleHistoryItem(
    int Index,
    string MobName,
    bool IsBoss,
    double TotalAmount,
    long BattleTimeMs,
    long BattleStartMs);

/// <summary>
/// Maps the data layer's saved-battle list into history rows. Pure (no WPF) so it is unit-testable.
/// Mirrors React useHistory: drop zero-duration battles, newest first.
/// </summary>
public static class BattleHistory
{
    public static IReadOnlyList<BattleHistoryItem> Build(IEnumerable<(int Index, DpsReport Report)> battles)
    {
        var items = new List<BattleHistoryItem>();
        foreach ((int index, DpsReport report) in battles)
        {
            long battleTime = Math.Max(report.BattleEnd - report.BattleStart, 0L);
            if (battleTime <= 0)
            {
                continue; // React filters battleTime > 0
            }

            items.Add(new BattleHistoryItem(
                Index: index,
                MobName: report.Target?.Mob.Name ?? "알 수 없음",
                IsBoss: report.Target?.Mob.Boss ?? false,
                TotalAmount: report.Information.Values.Sum(i => i.Amount),
                BattleTimeMs: battleTime,
                BattleStartMs: report.BattleStart));
        }

        items.Reverse(); // repository is oldest-first; show newest first
        return items;
    }
}

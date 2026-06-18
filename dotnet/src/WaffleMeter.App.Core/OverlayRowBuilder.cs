using WaffleMeter.Data;

namespace WaffleMeter.App.Core;

/// <summary>
/// Pure (UI-free, deterministic) selection of which players the meter shows and in what order — extracted
/// from <c>OverlayViewModel.Update</c> so it is unit-testable (the VM lives in a WinExe WPF project with no
/// test host). Mirrors the React MeterList: sort by metric desc, take top N, always include self. Three
/// behaviors live here:
/// <list type="bullet">
/// <item>Blank-row filter: a COMBAT row whose player has no nickname (a bare 난입/mid-join provisional actor,
/// registered so its DPS isn't dropped) is hidden — it shows "broken". Its DPS still accumulates upstream and
/// the row appears the moment identity (0x3633/0x3645) enriches the SAME user, with its full history.</item>
/// <item>Pre-combat preview: when there is no combat data at all (<see cref="DpsReport.Information"/> empty)
/// AND this is the fresh pre-combat report (<see cref="DpsReport.ExecutorId"/> == 0), the recently-seen party
/// roster is surfaced as idle 0-DPS rows (the App injects the recognized self into that roster, so the local
/// player shows too).</item>
/// <item>Combat-row presence (<paramref name="hasCombatRows"/>): derived from the FILTERED set, so an all-bare
/// mid-join never renders a live boss bar / ticking timer over the "전투 대기 중" placeholder.</item>
/// </list>
/// </summary>
public static class OverlayRowBuilder
{
    /// <summary>One selected row: the player's uid, its metric info, the resolved <see cref="User"/> (may be
    /// null for an unmatched uid), and whether it is the local player ("본인").</summary>
    public readonly record struct Row(int Uid, DpsInformation Info, User? User, bool IsSelf);

    /// <summary>
    /// Builds the ordered display rows for one report. <paramref name="liveSelfId"/> is the live-recognized
    /// 본인 uid (used only when the report carries no frozen <see cref="DpsReport.ExecutorId"/>).
    /// <paramref name="roster"/> is the pre-combat party (already executor-first / power-desc, App-supplied).
    /// </summary>
    public static IReadOnlyList<Row> Build(
        DpsReport report,
        IReadOnlyList<User> roster,
        int liveSelfId,
        bool useTotalDamage,
        bool showPreCombatRoster,
        out bool hasCombatRows,
        int topN = 8)
    {
        double Metric(DpsInformation info) => useTotalDamage ? info.Amount : info.Dps;

        // 본인 id for coloring: prefer the id frozen INTO the report (a saved/history battle), else the live one.
        int selfId = report.ExecutorId != 0 ? report.ExecutorId : liveSelfId;
        bool IsSelf(int uid, User? user) => user?.IsExecutor == true || (selfId != 0 && uid == selfId);

        // COMBAT rows: drop a row whose player has no nickname (a bare mid-join provisional actor). Gate on
        // nickname ALONE — never Power (arrives async, would transiently hide known party members) nor Server.
        List<Row> entries = report.Information
            .Select(kv => new Row(kv.Key, kv.Value, report.Contributors.FirstOrDefault(c => c.Id == kv.Key), false))
            .Where(e => !string.IsNullOrWhiteSpace(e.User?.Nickname))
            .OrderByDescending(e => Metric(e.Info))
            .ToList();

        // Drives the target-info bar + combat timer: count of DISPLAYABLE combat rows, not report.Information.
        hasCombatRows = entries.Count > 0;

        // Pre-combat party preview: only when there is NO combat data AT ALL (report.Information empty — NOT the
        // post-filter entries count) AND this is the fresh report (ExecutorId is frozen non-zero into the
        // post-combat _recentData). Gating on report.Information.Count is what keeps the preview from resurfacing
        // OVER a live fight whose only damager is a bare mid-join actor (filtered out above) — that case shows
        // the placeholder, not a 0-DPS party preview. After the first combat the frozen ExecutorId also holds
        // the preview shut so the last battle persists (the issue-#2 lifecycle).
        if (report.Information.Count == 0 && report.ExecutorId == 0 && showPreCombatRoster && roster.Count > 0)
        {
            entries.AddRange(roster.Select(m => new Row(m.Id, new DpsInformation(), m, false)));
        }

        List<Row> display = entries.Take(topN).ToList();
        int selfIndex = entries.FindIndex(e => IsSelf(e.Uid, e.User));
        if (selfIndex >= 0 && !display.Contains(entries[selfIndex]))
        {
            display.Add(entries[selfIndex]); // always show self, even outside the top N
        }

        // Stamp IsSelf for the rows the VM will render (after the value-equality Contains check above).
        for (int i = 0; i < display.Count; i++)
        {
            Row r = display[i];
            display[i] = r with { IsSelf = IsSelf(r.Uid, r.User) };
        }

        return display;
    }
}

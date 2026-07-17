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

    // Minimum share of the top dealer's metric a bare actor must have to be a self-recovery candidate, so an
    // incidental low-damage bare entity (pet/NPC) is never mistaken for the local player.
    private const double SelfRecoveryMinShare = 0.2;

    /// <summary>
    /// Builds the ordered display rows for one report. <paramref name="liveSelfId"/> is the live-recognized
    /// 본인 uid (used only when the report carries no frozen <see cref="DpsReport.ExecutorId"/>).
    /// <paramref name="roster"/> is the pre-combat party (already executor-first / power-desc, App-supplied).
    /// The <paramref name="selfNickname"/>/<paramref name="selfServer"/>/<paramref name="selfJob"/>/
    /// <paramref name="selfPower"/> are the recognized executor's known identity, used only for lost-executor
    /// recovery (below); pass them whenever 본인 is recognized.
    /// </summary>
    public static IReadOnlyList<Row> Build(
        DpsReport report,
        IReadOnlyList<User> roster,
        int liveSelfId,
        bool useTotalDamage,
        bool showPreCombatRoster,
        out bool hasCombatRows,
        // Max raid is 10 (two parties of 5) since the 2026-07-01 patch; was 8 (4+4). At 8 a 10-인 공대 lost
        // its two lowest dealers off the bottom of the meter (all 10 are still tracked — this only caps display).
        int topN = 10,
        string? selfNickname = null,
        int selfServer = 0,
        JobClass? selfJob = null,
        int selfPower = 0,
        IReadOnlyList<User>? authoritativeParty = null)
    {
        double Metric(DpsInformation info) => useTotalDamage ? info.Amount : info.Dps;

        // 본인 id for coloring: prefer the id frozen INTO the report (a saved/history battle), else the live one.
        int selfId = report.ExecutorId != 0 ? report.ExecutorId : liveSelfId;
        bool IsSelf(int uid, User? user) => user?.IsExecutor == true || (selfId != 0 && uid == selfId);

        // Raw COMBAT rows (pre blank-row filter), metric-sorted.
        List<Row> rawCombat = report.Information
            .Select(kv => new Row(kv.Key, kv.Value, report.Contributors.FirstOrDefault(c => c.Id == kv.Key), false))
            .OrderByDescending(e => Metric(e.Info))
            .ToList();

        // Lost-executor recovery: when 본인 re-instances (new entity id) and the new id's own-load packet
        // (0x3633) never arrives — e.g. moving to the next boss while the prior encounter is unresolved, so the
        // game never broadcasts the new identity — 본인 fights as a bare actor that the blank-row filter would
        // hide, dropping the local player's whole DPS. Recover it, but ONLY under 5 guards so normal play and
        // skipped fights are untouched: (1) GATE = the recognized executor uid dealt NO damage in this fight
        // (in a normal fight 본인 damages under its registered uid, so this is false and we never get here);
        // (2) the candidate is bare (no 0x3633/0x3645); (3) its own job-locked skills match the known executor
        // job; (4) it is a MAJOR dealer (≥ SelfRecoveryMinShare of the top); (5) it is the UNIQUE such candidate;
        // (6) PARTY-CONTEXT: no NAMED damager is a non-party OUTSIDER (see <see cref="HasNamedOutsider"/>). A
        // re-instance happens in a DUNGEON, where the only players are your party; a NAMED player who is NOT in
        // your authoritative (0x9702) party proves this is a PUBLIC scene — a field-boss zerg — where 본인 is a
        // bystander who simply dealt no damage, not a bare re-instance. Without (6), the recovery relabels a
        // random strong stranger at a field boss as 본인 (the "someone else's field boss shows as my DPS" bug).
        // Done on a display-only copy (no state mutation), recomputed every tick, so it self-corrects the moment
        // the real 본인 id appears.
        int? recoveredUid = null;
        if (selfId != 0 && selfJob is { } knownJob && !string.IsNullOrWhiteSpace(selfNickname)
            && rawCombat.All(e => e.Uid != selfId))
        {
            double topMetric = rawCombat.Count > 0 ? rawCombat.Max(e => Metric(e.Info)) : 0.0;
            List<Row> candidates = rawCombat
                .Where(e => string.IsNullOrWhiteSpace(e.User?.Nickname)
                            && e.User?.Job == knownJob
                            && topMetric > 0 && Metric(e.Info) >= topMetric * SelfRecoveryMinShare)
                .ToList();
            if (candidates.Count == 1 && !HasNamedOutsider(rawCombat, candidates[0].Uid, selfId, authoritativeParty))
            {
                recoveredUid = candidates[0].Uid;
            }
        }

        // COMBAT rows: keep named rows + the recovered 본인 (named from the known identity on a copy); drop other
        // bare rows. Gate on nickname ALONE — never Power (arrives async, would transiently hide known party
        // members) nor Server.
        var entries = new List<Row>();
        foreach (Row e in rawCombat)
        {
            if (recoveredUid is { } ru && e.Uid == ru)
            {
                User disp = (e.User ?? new User(e.Uid)).Copy(); // display-only: never mutate the live User
                disp.Nickname = selfNickname;
                if (selfServer > 0) disp.Server = selfServer;
                disp.Job ??= selfJob;
                disp.IsExecutor = true;
                if (selfPower > 0 && disp.Power <= 0) disp.Power = selfPower;
                entries.Add(e with { User = disp });
            }
            else if (!string.IsNullOrWhiteSpace(e.User?.Nickname))
            {
                entries.Add(e);
            }
        }

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

    /// <summary>Party-context guard for lost-executor recovery (guard 6). Returns true when some NAMED damaging
    /// contributor — other than the recovery candidate or 본인 — is NOT a member of the authoritative
    /// (0x9702-only) party, which marks the fight as a PUBLIC scene (field-boss zerg / nearby players) rather
    /// than a private dungeon party. Matches party membership by uid OR (nickname, server) identity, so a
    /// re-instanced party member whose new uid hasn't reconciled still counts as party. A solo dungeon (no other
    /// named damager) and a party dungeon (every named damager is your party) both return false → recovery runs.
    /// <paramref name="authoritativeParty"/> == null disables the guard (unit tests / callers that don't supply
    /// it) — the live App always passes the 0x9702 party (never the display roster, which folds in recent combat
    /// contributors that at a field boss would themselves be the zerg).</summary>
    private static bool HasNamedOutsider(
        IReadOnlyList<Row> rawCombat, int candidateUid, int selfId, IReadOnlyList<User>? authoritativeParty)
    {
        if (authoritativeParty is null)
        {
            return false;
        }

        var partyUids = new HashSet<int>();
        var partyIdentities = new HashSet<(string, int)>();
        foreach (User m in authoritativeParty)
        {
            partyUids.Add(m.Id);
            if (!string.IsNullOrWhiteSpace(m.Nickname))
            {
                partyIdentities.Add((m.Nickname!, m.Server));
            }
        }

        return rawCombat.Any(e =>
            e.Uid != candidateUid && e.Uid != selfId
            && e.User is { } u && !string.IsNullOrWhiteSpace(u.Nickname)
            && !partyUids.Contains(e.Uid)
            && !partyIdentities.Contains((u.Nickname!, u.Server)));
    }
}

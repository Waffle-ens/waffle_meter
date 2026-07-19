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

    // Bug 4: minimum share of the top metric a BARE combat row must reach to be a roster-recovery candidate — a
    // real party member whose 0x3645 name-link was missed (capture began mid-fight / packet lost) is a major
    // dealer, while the trace non-party leaks the blank-row filter targets sit far below this.
    private const double BareMajorMinShare = 0.15;

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
        IReadOnlyList<User>? authoritativeParty = null,
        // Opt-in "던전 강제 집계": the caller passes true ONLY when the toggle is on AND the current target is a
        // classified instanced (원정/초월/성역) boss. It surfaces bare MAJOR dealers (identity packets missed on a
        // mid-dungeon meter start) as generic placeholders so a mid-start user sees the fight instead of an empty
        // meter. Safe here because instanced content has no outsiders (and the outsider guard still applies).
        bool forceInstanceTracking = false)
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
        double topMetric = rawCombat.Count > 0 ? rawCombat.Max(e => Metric(e.Info)) : 0.0;
        bool rosterConfirmed = authoritativeParty is { Count: > 0 };
        int? recoveredUid = null;
        if (selfId != 0 && selfJob is { } knownJob && !string.IsNullOrWhiteSpace(selfNickname)
            && rawCombat.All(e => e.Uid != selfId))
        {
            // A recovery candidate is a bare row OR — bug 3 — a row whose stored name is NOT a member of the
            // authoritative (0x9702) party: the self's re-instanced combat uid can inherit a STALE identity from
            // a prior entity that reused that id (observed: 본인 shown as a random "틸놈틸"), which the bare-only
            // filter never reclaimed. The stale-name relaxation is gated on a CONFIRMED roster so that, when the
            // party is unknown, we fall back to the safe bare-only rule (never relabel a genuine stranger).
            List<Row> candidates = rawCombat
                .Where(e => (string.IsNullOrWhiteSpace(e.User?.Nickname)
                             || (rosterConfirmed && !IsPartyMember(e.User, authoritativeParty)))
                            && e.User?.Job == knownJob
                            && topMetric > 0 && Metric(e.Info) >= topMetric * SelfRecoveryMinShare)
                .ToList();
            if (candidates.Count == 1 && !HasNamedOutsider(rawCombat, candidates[0].Uid, selfId, authoritativeParty))
            {
                recoveredUid = candidates[0].Uid;
            }
        }

        // Bug 4: name a bare MAJOR dealer from the authoritative (0x9702) roster when a party member is
        // UNACCOUNTED for. A member whose per-uid nickname (0x3645) was missed (capture began mid-fight / the
        // packet was lost) fights bare, and the blank-row filter would hide their whole DPS (observed: the top
        // dealer vanished for two bosses). Recover ONLY on positive roster evidence and an UNAMBIGUOUS 1:1 match
        // — exactly one unclaimed roster member ↔ exactly one bare major — in a party scene with no named
        // outsider, so a field-boss stranger (a bare non-party dealer) is never relabeled a party member. Absent
        // a roster (party unknown) this does nothing: the bare row stays hidden and enriches when identity lands.
        var partyRecovered = new Dictionary<int, User>();
        // Exclude the recovered self's combat uid from the outsider check: in the bug-3 case it carries a STALE
        // non-party name that would otherwise read as a "named outsider" and wrongly mark this a public scene.
        if (rosterConfirmed && !HasNamedOutsider(rawCombat, recoveredUid ?? -1, selfId, authoritativeParty))
        {
            var claimed = new HashSet<(string, int)>();
            foreach (Row e in rawCombat)
            {
                if (!string.IsNullOrWhiteSpace(e.User?.Nickname))
                {
                    claimed.Add((e.User!.Nickname!, e.User.Server));
                }
            }

            if (recoveredUid is not null && !string.IsNullOrWhiteSpace(selfNickname))
            {
                claimed.Add((selfNickname!, selfServer));
            }

            List<User> unclaimed = authoritativeParty!
                .Where(m => m.Id != selfId && !string.IsNullOrWhiteSpace(m.Nickname)
                            && !claimed.Contains((m.Nickname!, m.Server)))
                .ToList();
            List<Row> bareMajors = rawCombat
                .Where(e => string.IsNullOrWhiteSpace(e.User?.Nickname)
                            && (recoveredUid is not { } r || e.Uid != r)
                            && topMetric > 0 && Metric(e.Info) >= topMetric * BareMajorMinShare)
                .ToList();

            if (unclaimed.Count == 1 && bareMajors.Count == 1)
            {
                User disp = (bareMajors[0].User ?? new User(bareMajors[0].Uid)).Copy();
                disp.Nickname = unclaimed[0].Nickname;
                disp.Server = unclaimed[0].Server;
                disp.Job ??= unclaimed[0].Job;
                if (unclaimed[0].Power > 0 && disp.Power <= 0) disp.Power = unclaimed[0].Power;
                partyRecovered[bareMajors[0].Uid] = disp;
            }
        }

        // Opt-in "던전 강제 집계": on a classified instanced boss (caller-gated), surface bare MAJOR dealers as
        // generic placeholders so a mid-dungeon meter start (identity packets missed at load) shows the fight
        // instead of an empty meter. Still barred if a named OUTSIDER is present (defence-in-depth — instanced
        // content shouldn't have one). The recovered self is excluded from the outsider check.
        bool showBarePlaceholders = forceInstanceTracking
            && !HasNamedOutsider(rawCombat, recoveredUid ?? -1, selfId, authoritativeParty);
        int placeholderN = 0;

        // COMBAT rows: keep named rows + the recovered 본인 + a roster-recovered party member (named on a copy);
        // drop other bare rows. Gate on nickname ALONE — never Power (arrives async, would transiently hide known
        // party members) nor Server.
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
            else if (partyRecovered.TryGetValue(e.Uid, out User? recoveredMate))
            {
                entries.Add(e with { User = recoveredMate }); // bug 4: bare party member named from the roster
            }
            else if (!string.IsNullOrWhiteSpace(e.User?.Nickname))
            {
                entries.Add(e);
            }
            else if (showBarePlaceholders && topMetric > 0 && Metric(e.Info) >= topMetric * BareMajorMinShare)
            {
                User disp = (e.User ?? new User(e.Uid)).Copy();
                disp.Nickname = $"파티원 {++placeholderN}"; // identity missed on mid-start; enriches in place when it arrives
                entries.Add(e with { User = disp });
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
    /// <summary>True when <paramref name="u"/> is a member of the authoritative party — by uid, or by
    /// (nickname, server) identity (so a re-instanced member whose new uid hasn't reconciled still counts).</summary>
    private static bool IsPartyMember(User? u, IReadOnlyList<User>? party)
    {
        if (u is null || party is null)
        {
            return false;
        }

        foreach (User m in party)
        {
            if (m.Id == u.Id
                || (!string.IsNullOrWhiteSpace(u.Nickname)
                    && string.Equals(m.Nickname, u.Nickname, StringComparison.Ordinal) && m.Server == u.Server))
            {
                return true;
            }
        }

        return false;
    }

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

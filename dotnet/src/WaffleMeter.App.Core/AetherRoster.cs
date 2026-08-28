using System.Globalization;

namespace WaffleMeter.App.Core;

/// <summary>A name the roster can put to an identity hash, sourced from the consent list.</summary>
public readonly record struct AetherRosterName(string IdentityHash, string? Nickname, int Server, string? Job);

/// <summary>One weekly 성역 raid as it stands for one character: how many clears are left of the weekly grant.
/// <paramref name="Known"/> is false when nothing has been recorded for this character since the last reset —
/// the count then shows full, because "not seen" and "not cleared" look the same to the player and the
/// optimistic reading is the one a fresh install should give.</summary>
public readonly record struct WeeklyContentCell(WeeklyContentInfo Content, int Remaining, bool Known)
{
    public int Grant => WeeklyContentCatalog.WeeklyGrant;
}

/// <summary>One 어비스 회랑 this character can enter: how much of the 130-second 이용 시간 is left, and whether
/// the clock is running right now (the character is standing in it).
/// <para>A cell exists only for a corridor some character on this server has been watched WALKING INTO since
/// the last 점령전 — never invented from a ticket value. A zero covers four different situations at once, and
/// a positive value covers two: time granted at the last 점령전, and time granted at an earlier one and never
/// spent, which the server keeps reporting long after the artifact has changed hands.</para></summary>
/// <param name="Inferred">True when nothing this character reported backs the number: the corridor is known
/// held because a character beside it on the same server walked into it, and this one is taken to be
/// untouched, so the cell reads the full grant. See <see cref="AetherRoster"/> for why that is sound.</param>
public readonly record struct AbyssCorridorCell(
    AbyssCorridorInfo Corridor,
    long RemainingMs,
    bool Ticking,
    bool Inferred = false)
{
    /// <summary>The denominator — the base grant, or the reading itself if the server ever hands out more.</summary>
    public long FullMs => Math.Max(AbyssCorridorCatalog.FullGrantMs, RemainingMs);

    public bool Spent => RemainingMs <= 0;
}

/// <summary>One row of the 컨텐츠 관리 목록 — a character, what it holds, and its weekly clears.</summary>
public readonly record struct AetherRosterRow(
    string IdentityHash,
    string Label,
    string SubLabel,
    int Base,
    int Bonus,
    int Total,
    long SavedAtMs,
    bool IsCurrent,
    IReadOnlyList<WeeklyContentCell>? Weekly = null,
    IReadOnlyList<AbyssCorridorCell>? Corridors = null,
    bool CorridorsKnown = false,
    bool CorridorsConfirmed = false)
{
    /// <summary>The weekly raids in catalog order, never null.</summary>
    public IReadOnlyList<WeeklyContentCell> WeeklyCells => Weekly ?? [];

    /// <summary>The 어비스 회랑 this character holds time for, in catalog order. Empty means no character on
    /// this character's server has been watched holding a corridor since the last 점령전 — which is a statement
    /// about what this install has seen, not about what the faction captured.</summary>
    public IReadOnlyList<AbyssCorridorCell> CorridorCells => Corridors ?? [];

    /// <summary>"자연회복(+추가)" as the chip shows it; the bonus half is dropped when there is none.</summary>
    public string AetherText => Bonus > 0
        ? string.Concat(Base.ToString(CultureInfo.InvariantCulture), "(+", Bonus.ToString(CultureInfo.InvariantCulture), ")")
        : Base.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// Builds the 오드 목록 rows from the per-character store. Pure (no WPF) so the ordering and the
/// name-resolution fallbacks are unit-testable.
/// <para>The store is keyed by a one-way identity hash, so a name has to come from somewhere: the record itself
/// (written since 2026-08-05) or the consent list. Deliberately NOT gated on consent state — the settings
/// screen's character list only shows <c>accepted</c> characters because it manages consent, but this list
/// only reports a local balance and has nothing to do with uploading.</para>
/// </summary>
public static class AetherRoster
{
    public static IReadOnlyList<AetherRosterRow> Build(
        AetherPerCharacterStore store,
        IEnumerable<AetherRosterName>? names = null,
        string? currentHash = null,
        WeeklyContentStore? weekly = null,
        long nowMs = 0,
        AbyssCorridorStore? corridors = null,
        AbyssArtifactStore? artifacts = null)
    {
        long at = nowMs > 0 ? nowMs : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var byHash = new Dictionary<string, AetherRosterName>(StringComparer.Ordinal);
        foreach (AetherRosterName name in names ?? [])
        {
            if (!string.IsNullOrWhiteSpace(name.IdentityHash))
            {
                byHash[name.IdentityHash] = name;
            }
        }

        // Taken once: All() sorts and allocates on every call, and this list is walked twice — once to learn
        // which server each character is on, then again to build the rows. The panel rebuilds this once a
        // second while a corridor clock is running.
        List<KeyValuePair<string, AetherSnapshot>> characters = store.All().ToList();

        // Which server each character sits on has to be resolved before ANY row is built, because the 어비스
        // 회랑 a character can enter is a fact about its server rather than about it — see CapturedByServer.
        var serverByHash = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach ((string hash, AetherSnapshot snapshot) in characters)
        {
            byHash.TryGetValue(hash, out AetherRosterName known);
            int resolved = snapshot.Server > 0 ? snapshot.Server : known.Server;
            if (resolved > 0)
            {
                serverByHash[hash] = resolved;
            }
        }

        // Every character's corridor standing, resolved once. Both the server-wide union below and each row's
        // own cells need the same answers, and Standing() re-derives the 점령 cycle boundary from scratch each
        // call — six corridors × two callers × once a second is a date calculation nobody needs twice.
        Dictionary<string, Dictionary<int, long>> standings = StandingsFor(corridors, characters, at);
        Dictionary<int, HashSet<int>> capturedByServer = CapturedByServer(standings, serverByHash);

        // What the server SAYS it holds, which since 2026-08-29 is the primary answer and the entry union above
        // is the fallback. See HeldByServer for why one character's abnormal answers for the whole server.
        Dictionary<int, HashSet<int>> heldByServer = HeldByServer(artifacts, serverByHash, at);

        var rows = new List<AetherRosterRow>();
        foreach ((string hash, AetherSnapshot snapshot) in characters)
        {
            byHash.TryGetValue(hash, out AetherRosterName known);

            // The record's own nickname wins: it is written from the live executor every broadcast, whereas a
            // consent entry can predate a rename. Fall back to the consent list for records written before the
            // name was stored, then to a stable stub so the row still shows its balance.
            string? nickname = FirstNonBlank(snapshot.Nickname, known.Nickname);
            int server = serverByHash.TryGetValue(hash, out int resolvedServer) ? resolvedServer : 0;

            // GetServerLabel returns "" for an id the table doesn't know (a new server, or a record left by
            // the 2026-07-30 identity corruption) — appending empty brackets would read as a rendering bug.
            string serverLabel = server > 0 ? ServerNames.GetServerLabel(server) : string.Empty;
            string label = nickname is null
                ? "이름 없는 캐릭터"
                : serverLabel.Length > 0
                    ? $"{nickname} [{serverLabel}]"
                    : nickname;

            // Carry the reading forward over the 자연회복 that accrued since it was taken. Every row but the
            // current character's is by definition a memory, and showing last night's number for a character
            // that has been regenerating all night is the mismatch this list exists to avoid.
            (int projectedBase, int projectedBonus) =
                AetherRegen.Project(snapshot.Base, snapshot.Bonus, snapshot.SavedAtMs, at);

            rows.Add(new AetherRosterRow(
                IdentityHash: hash,
                Label: label,
                SubLabel: known.Job ?? string.Empty,
                Base: projectedBase,
                Bonus: projectedBonus,
                Total: projectedBase + projectedBonus,
                SavedAtMs: snapshot.SavedAtMs,
                IsCurrent: currentHash != null && string.Equals(hash, currentHash, StringComparison.Ordinal),
                Weekly: WeeklyFor(weekly, hash, at),
                Corridors: CorridorsFor(
                    corridors,
                    hash,
                    standings.GetValueOrDefault(hash),
                    isCurrent: currentHash != null && string.Equals(hash, currentHash, StringComparison.Ordinal),
                    capturedOnServer: server > 0 && capturedByServer.TryGetValue(server, out HashSet<int>? captured)
                        ? captured
                        : null,
                    heldOnServer: server > 0 && heldByServer.TryGetValue(server, out HashSet<int>? held)
                        ? held
                        : null,
                    nowMs: at),

                // "회랑 없음" may only be printed when the emptiness is a FACT, not a silence. The 점령 현황
                // broadcast makes it one: with it on file for this server (and the side settled, which is what
                // HeldByServer entering the map guarantees) an empty list really does mean the side holds
                // nothing. The old witness — a 0x610B dump was seen for this character — stays as the fallback
                // for a server the broadcast has not reached yet.
                CorridorsKnown: (server > 0 && heldByServer.ContainsKey(server))
                    || (corridors?.HasCycleWitness(hash, at) ?? false),

                // The two empty states say different things and must not share a sentence. Confirmed = the
                // server's own 점령 현황 is on file and our side in it is settled, so an empty list really is
                // "우리 진영이 점령한 회랑이 없다". Unconfirmed = we have only watched this character's snapshot,
                // which cannot tell 미점령 from 미방문.
                CorridorsConfirmed: server > 0 && heldByServer.ContainsKey(server)));
        }

        // Current character first (that's the one the user is looking at), then most-recently-seen. Ordering by
        // balance would make the list jump around as the active character spends.
        return rows
            .OrderByDescending(r => r.IsCurrent)
            .ThenByDescending(r => r.SavedAtMs)
            .ToList();
    }

    /// <summary>Every weekly raid's standing for one character, in catalog order. An unknown or stale record
    /// reads as the full grant — the server recharges the counter at the weekly reset, so a value recorded
    /// before it is not "0 left", it is "no longer known".</summary>
    private static IReadOnlyList<WeeklyContentCell> WeeklyFor(WeeklyContentStore? weekly, string hash, long nowMs)
    {
        var cells = new List<WeeklyContentCell>(WeeklyContentCatalog.All.Count);
        foreach (WeeklyContentInfo content in WeeklyContentCatalog.All)
        {
            int? remaining = weekly?.Remaining(hash, content.Slug, nowMs);
            cells.Add(new WeeklyContentCell(
                content,
                remaining ?? WeeklyContentCatalog.WeeklyGrant,
                Known: remaining.HasValue));
        }

        return cells;
    }

    /// <summary>
    /// Which 어비스 회랑 each server is known to hold this 점령 cycle: the union, over that server's characters,
    /// of every corridor one of them has been watched holding.
    ///
    /// <para><b>Why a server may answer for a character that never reported anything.</b> Measured 2026-08-20:
    /// six characters on server 2003, all six logged in between 20:40 and 21:38 KST — a full day after the
    /// Wednesday 점령전 that stocked the corridors — and five of them reported zero on all twelve corridor
    /// currencies while the sixth reported the full 130 s on two of them. Same server, same cycle, an hour
    /// apart. So those zeros cannot mean "our side did not capture it": the ticket simply does not materialise
    /// for a character that has not been to the abyss since the capture. That is a FOURTH meaning for zero
    /// (미방문) on top of the three <c>AbyssCorridorStore</c> already names, and it is what made four characters
    /// read "어비스 회랑 없음" while their own faction held two corridors.</para>
    ///
    /// <para><b>Why the server id is enough of a key.</b> Which corridors are open is decided per 진영, and a
    /// server id already IS a 진영 — 1001~1021 are 천족 and 2001~2021 마족 (<see cref="MeterFormat.ServerTier"/>,
    /// which colours meter rows by exactly this split). Two characters sharing a server therefore share a side
    /// and share an occupation result; two characters on different servers can be matched into different abyss
    /// instances and must never answer for each other.</para>
    ///
    /// <para>Union rather than "first character wins" because each character can only prove the corridors it
    /// personally walked into, so every one of them is a lower bound on what the side holds and the widest one
    /// is the closest to the truth. A corridor one character has already spent still counts — it got in, which
    /// is exactly what the character beside it needs to know.</para>
    ///
    /// <para><b>What may NOT be unioned, learned 2026-08-23.</b> This used to take its input from characters
    /// that had merely been TOLD a corridor held time. That is not the same fact: unspent 이용 시간 survives the
    /// 점령전 that takes the artifact away, and the server goes on reporting it. One such leftover on the main
    /// character put a lost 유황나무 on five siblings as a full 2:10, on a corridor whose portal the player had
    /// already found closed. Entry is the only input now.</para>
    /// </summary>
    /// <summary>
    /// Which 어비스 회랑 each server's side holds, as the 점령 현황 broadcast (0xE305/0xE307) states it.
    ///
    /// <para>This is the fact the feature was missing. Occupation used to be proved by watching a character walk
    /// into a corridor's instance map — sound, but it names only the corridors this character personally got
    /// into, and across the whole of August that fired six times. So the row stayed empty for a player who had
    /// just walked through the abyss and could see their side's corridors in the game's own UI. The broadcast
    /// answers outright, per artifact, and arrives at login (from the ordinary game server, before the abyss is
    /// even entered), on every world-map open, and on every abyss zone load.</para>
    ///
    /// <para><b>The side is resolved per character and then spread across the server</b>, because the broadcast
    /// only says which SLOT holds what and the slot is an index inside the current server matchup — the same
    /// character read slot 1 on 2026-08-23 and slot 2 on 2026-08-28. The 아티팩트 점령 abnormal the server puts on
    /// a character in the abyss says how many our side holds, which picks the slot. A server id already IS a
    /// 진영 (1001~1021 천족 / 2001~2021 마족), so one character's answer is every sibling's answer — exactly the
    /// reasoning <see cref="CapturedByServer"/> runs on.</para>
    ///
    /// <para>A server whose side cannot be settled is left OUT of the dictionary rather than added empty: an
    /// empty entry would let the panel print "점령한 회랑 없음", which is a claim, while a missing one correctly
    /// reads as "we have not heard".</para>
    /// </summary>
    private static Dictionary<int, HashSet<int>> HeldByServer(
        AbyssArtifactStore? artifacts, Dictionary<string, int> serverByHash, long nowMs)
    {
        var byServer = new Dictionary<int, HashSet<int>>();
        if (artifacts is null)
        {
            return byServer;
        }

        foreach ((string hash, int server) in serverByHash)
        {
            if (byServer.ContainsKey(server) || artifacts.SideFor(server, hash, nowMs) is not { } side)
            {
                continue;
            }

            byServer[server] = [.. artifacts.HeldTicketIds(server, side, nowMs)];
        }

        return byServer;
    }

    private static Dictionary<int, HashSet<int>> CapturedByServer(
        Dictionary<string, Dictionary<int, long>> standings, Dictionary<string, int> serverByHash)
    {
        var byServer = new Dictionary<int, HashSet<int>>();
        foreach ((string hash, int server) in serverByHash)
        {
            if (!standings.TryGetValue(hash, out Dictionary<int, long>? forCharacter))
            {
                continue;
            }

            if (!byServer.TryGetValue(server, out HashSet<int>? captured))
            {
                byServer[server] = captured = [];
            }

            foreach (int ticketId in forCharacter.Keys)
            {
                captured.Add(ticketId);
            }
        }

        return byServer;
    }

    /// <summary>Each character's corridor standings for the current 점령 cycle, keyed by ticket id — the entries
    /// <see cref="AbyssCorridorStore.Standing"/> answers with a value rather than null, i.e. the corridors this
    /// character has been watched walking INTO since the last 점령전.
    /// <para>Standing() is where both filters live: the corridor has to have been entered (which the game only
    /// permits while the side holds the artifact) and the entry has to be newer than the last 점령전. Nothing
    /// here can hand last week's occupation to anyone as this week's, and nothing here rests on a ticket value,
    /// which 2026-08-23 established is a stock that outlives the occupation that granted it.</para></summary>
    private static Dictionary<string, Dictionary<int, long>> StandingsFor(
        AbyssCorridorStore? corridors,
        List<KeyValuePair<string, AetherSnapshot>> characters,
        long nowMs)
    {
        var byHash = new Dictionary<string, Dictionary<int, long>>(StringComparer.Ordinal);
        if (corridors is null)
        {
            return byHash;
        }

        foreach ((string hash, _) in characters)
        {
            foreach (AbyssCorridorInfo corridor in AbyssCorridorCatalog.All)
            {
                if (corridors.Standing(hash, corridor.TicketId, nowMs) is not { } remainingMs)
                {
                    continue;
                }

                if (!byHash.TryGetValue(hash, out Dictionary<int, long>? forCharacter))
                {
                    byHash[hash] = forCharacter = [];
                }

                forCharacter[corridor.TicketId] = remainingMs;
            }
        }

        return byHash;
    }

    /// <summary>The 어비스 회랑 this character can enter, in catalog order. A corridor this character was
    /// watched holding shows its own reading; one only its SERVER was watched holding shows the full grant,
    /// flagged <see cref="AbyssCorridorCell.Inferred"/> — a character that has not been to the abyss since the
    /// 점령전 has spent none of its 이용 시간 by definition, so full is the reading, not a placeholder.
    /// <para>A corridor no character on that server has been seen holding is still left out entirely. The wire
    /// cannot tell "우리 진영이 못 뺏었다" from "이 캐릭터가 다 썼다" from "여긴 안 가봤다", so a chip nothing on
    /// this install has ever seen stocked would be an invention.</para>
    /// <para>A reading the store kept for THIS character always wins, including a zero — a corridor watched
    /// running down to 0:00 reads 0:00, never the full grant. The assumed full is only for a character that has
    /// heard nothing about that corridor this cycle, which is precisely when it has spent none of it.</para>
    /// <para><b>The one way the number can still be too high.</b> A character that walked a corridor down while
    /// the meter was CLOSED has no reading from this cycle, so it inherits the full grant. The error only ever
    /// runs in that direction, and the chip's tooltip states the condition it depends on. Spending it while the
    /// meter is running is safe — the 0x610C expiry is filed the moment it lands.</para>
    /// <para><b>Measured 2026-08-21 — the inheritance is not an assumption.</b> 헤로롱 logged in from town
    /// reporting zero on every corridor, and the panel handed it its server's corridors as guesses. Walking that
    /// same character into the abyss made the real values arrive — and 고목나무, a corridor it had never once
    /// entered, came back at the FULL grant, exactly what the guess claimed. A corridor the side holds really is
    /// stocked for a character that has not been to collect it; the ticket just does not surface until it
    /// goes.</para>
    /// <para><b>What 2026-08-23 changed.</b> Which corridors get offered at all used to come from the ticket
    /// VALUE, and that was wrong in the one direction a player notices: unspent time survives the 점령전 that
    /// takes the artifact away, so a corridor the side had lost was still being handed to five sibling
    /// characters as a full 2:10. Entry is what the list is built from now, so a corridor nobody could get into
    /// this cycle cannot appear on anyone.</para></summary>
    private static IReadOnlyList<AbyssCorridorCell> CorridorsFor(
        AbyssCorridorStore? corridors,
        string hash,
        Dictionary<int, long>? standings,
        bool isCurrent,
        IReadOnlySet<int>? capturedOnServer,
        IReadOnlySet<int>? heldOnServer,
        long nowMs)
    {
        // The 점령 현황 broadcast alone is enough to draw a row: it names the corridors, and the corridor store
        // only ever supplies the NUMBER on the chip. Bailing here when that store is absent would make the
        // occupation invisible on a fresh install that has heard the broadcast but banked no reading yet.
        if (corridors is null && heldOnServer is null)
        {
            return [];
        }

        var cells = new List<AbyssCorridorCell>(AbyssCorridorCatalog.All.Count);
        foreach (AbyssCorridorInfo corridor in AbyssCorridorCatalog.All)
        {
            // Held by this side this cycle, proven either by this character walking in or by one beside it on
            // the same server. Anything else is left out entirely: a corridor nobody on this install has been
            // able to enter since the 점령전 would be an invention, whatever the tickets say.
            // Three ways a corridor gets on the list, in order of authority: the server said our side holds it,
            // this character walked in, or a sibling on the same server did. The broadcast is primary — the two
            // entry proofs only still exist to cover a session that has not heard one yet.
            bool ownProof = standings?.ContainsKey(corridor.TicketId) == true;
            if (heldOnServer is not null)
            {
                if (!heldOnServer.Contains(corridor.TicketId))
                {
                    continue;
                }
            }
            else if (!ownProof && capturedOnServer?.Contains(corridor.TicketId) != true)
            {
                continue;
            }

            // The character's own reading is used wherever there is one, even for a corridor it was the server
            // that proved held — that is how a character sitting on a measured 2:10 stops being drawn as a
            // guess just because it happened to be a sibling that walked in.
            long? own = corridors?.Reading(hash, corridor.TicketId, nowMs);
            long remainingMs = own ?? AbyssCorridorCatalog.FullGrantMs;

            // "지금 입장 중" is a claim about the character being played, so a record left ticking on anyone
            // else never makes it — a clock that outlived its visit (the meter closed inside a corridor) would
            // otherwise light up a row the user is not even controlling.
            bool ticking = isCurrent
                && remainingMs > 0
                && corridors?.Get(hash, corridor.TicketId) is { TickingSinceMs: > 0 };
            cells.Add(new AbyssCorridorCell(corridor, remainingMs, ticking, Inferred: own is null));
        }

        return cells;
    }

    private static string? FirstNonBlank(params string?[] candidates)
    {
        foreach (string? candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate.Trim();
            }
        }

        return null;
    }
}

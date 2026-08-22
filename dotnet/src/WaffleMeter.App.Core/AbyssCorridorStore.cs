using System.Globalization;

namespace WaffleMeter.App.Core;

/// <summary>One character's standing for one 어비스 회랑.</summary>
/// <param name="RemainingMs">The 이용 시간 left AS OF <paramref name="ObservedAtMs"/> — never a projection.</param>
/// <param name="ObservedAtMs">When the server last stated it.</param>
/// <param name="GrantedAtMs">When the server last stated a value ABOVE zero for this corridor. Kept as a record
/// of what was heard and when — and deliberately NOT read as evidence that the side holds the artifact.
/// <para><b>Measured 2026-08-23, and it cost a release to learn.</b> The 이용 시간 is a per-character STOCK, not
/// a per-cycle allowance: time handed out at one 점령전 and never spent is still sitting on the character after
/// the artifact changes hands, and the server keeps reporting it. 콘팡 was told 유황나무 held time at 02:04 on
/// the Sunday — three hours and forty-five minutes after the Saturday 점령전 in which the player watched the
/// other faction take that artifact, and with the portal since confirmed closed. So a positive reading answers
/// "이 캐릭터에게 안 쓴 시간이 남아 있는가", never "우리 진영이 이걸 점령했는가".</para>
/// <para>What the occupation question IS answered by lives in a separate proof row — see
/// <see cref="AbyssCorridorStore.MarkEntered"/>.</para></param>
/// <param name="TickingSinceMs">When the character entered the corridor and the clock started, or 0 when it is
/// not running. Persisted rather than kept in memory so a meter that was closed mid-corridor does not come back
/// still claiming 2:10 left on a corridor that expired while it was off.</param>
public readonly record struct AbyssCorridorRecord(
    long RemainingMs,
    long ObservedAtMs,
    long GrantedAtMs,
    long TickingSinceMs)
{
    /// <summary>The reading carried forward to <paramref name="nowMs"/>. Only a corridor the character is
    /// standing in burns time, so a record that is not ticking projects to itself.</summary>
    public long Project(long nowMs)
    {
        if (TickingSinceMs <= 0 || nowMs <= TickingSinceMs)
        {
            return Math.Max(0, RemainingMs);
        }

        return Math.Clamp(RemainingMs - (nowMs - TickingSinceMs), 0, Math.Max(0, RemainingMs));
    }
}

/// <summary>
/// Remembers each character's 어비스 회랑 이용 시간, keyed by stats identity hash + the client's ticket id, so
/// the 컨텐츠 관리 panel can show every character rather than only the one logged in.
///
/// <para><b>The value is the server's.</b> It rides the 0x610B login/zone-in snapshot and the 0x610C change
/// notice (see <c>AbyssCorridorParser</c>). The server states it exactly twice per visit — the full budget on
/// entry and zero on expiry, with no ticks between — so anything shown mid-visit is this store's own projection
/// from those two facts.</para>
///
/// <para><b>Its OWN settings key</b> (<c>content.abyssCorridors</c>), never extra fields on the 오드 or 주간
/// blob. <see cref="WeeklyContentStore"/> spells out why: field count is the format discriminator, an older
/// build DROPS a record it cannot parse, and those blobs are rewritten on every broadcast — so widening one
/// would make a single rollback permanent data loss. A key an old build has never heard of is simply ignored.</para>
///
/// <para><b>What is stored, and what is not.</b> A record is written only for a corridor worth remembering — one
/// the server stated time on, or one this character walked into. Corridors that were merely listed as zero in a
/// snapshot are NOT stored; instead one witness row per character (ticket id 0) records that a full snapshot was
/// seen, which is what lets the panel say "어비스 회랑 기록 없음" instead of staying silent about a character it
/// has never watched.</para>
/// <para>⚠️ That line reports the state of OUR records and nothing more. It used to be read as "이 캐릭터는
/// 점령한 회랑이 없다", which 2026-08-20 showed to be wrong: five characters on one server reported zero on every
/// corridor while a sixth beside them held two, so a zeroed snapshot is also what a character that has not been
/// to the abyss since the 점령전 sends.</para>
///
/// <para><b>Occupation is proved by walking in, and by nothing else.</b> The value cannot answer it — a stock
/// left over from an earlier 점령전 reads exactly like one handed out at the last (see
/// <see cref="AbyssCorridorRecord.GrantedAtMs"/>), and a zero covers four different situations at once. Loading
/// the corridor's instance map is the one event the game only lets happen while the side holds the artifact, so
/// that is what this store records as evidence, in a proof row per corridor
/// (<see cref="MarkEntered"/> / <see cref="Standing"/>). Everything the panel offers about a corridor hangs off
/// that row; the numbers only decorate it.</para>
///
/// Pure and cap-bounded so the projection and the staleness rule are unit-testable.
/// </summary>
public sealed class AbyssCorridorStore
{
    /// <summary>Most-recent characters kept, matching <see cref="AetherPerCharacterStore.MaxCharacters"/>.</summary>
    public const int MaxCharacters = 48;

    /// <summary>Reserved ticket id for the per-character "a full snapshot was seen at" row.</summary>
    public const int WitnessTicketId = 0;

    /// <summary>Base for the reserved ticket ids that hold the "this character walked into that corridor" proof
    /// rows — <c>EntryTicketBase + ticketId</c>, so 10000002 is proved by 30000002.
    /// <para><b>A reserved id rather than a new field, and rather than a new settings key.</b> The blob's field
    /// count is its format discriminator and a build that cannot parse a record DROPS it, so widening the record
    /// would make one rollback wipe every corridor the user has. A reserved id costs nothing instead: the parser
    /// already keeps and re-serialises ids it has no catalog entry for, so an older build carries these rows
    /// through untouched, and this one simply finds none in a blob written before the rule existed — which is
    /// the correct starting state, since a record written under the old rule proves nothing.</para>
    /// <para>Out of reach of the wire by construction: <c>AbyssCorridorParser</c> only ever emits ids
    /// 10000001~10000012, so no broadcast can land on one of these.</para></summary>
    public const int EntryTicketBase = 20_000_000;

    private readonly Dictionary<string, Dictionary<int, AbyssCorridorRecord>> _byHash;

    private AbyssCorridorStore(Dictionary<string, Dictionary<int, AbyssCorridorRecord>> byHash) => _byHash = byHash;

    /// <summary>Parse the serialized blob. Never throws — malformed records are skipped.
    /// <para>Records are <c>hash,ticketId,remainingMs,observedAtMs,grantedAtMs,tickingSinceMs</c>. A ticket id
    /// this build has no catalog entry for is kept and re-serialized untouched, so shipping a corridor later
    /// cannot be undone by a user who rolls back once.</para></summary>
    public static AbyssCorridorStore Parse(string? serialized)
    {
        var map = new Dictionary<string, Dictionary<int, AbyssCorridorRecord>>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(serialized))
        {
            return new AbyssCorridorStore(map);
        }

        foreach (string record in serialized.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] f = record.Split(',');
            if (f.Length != 6
                || string.IsNullOrWhiteSpace(f[0])
                || !int.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int ticketId)
                || !long.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out long remaining)
                || !long.TryParse(f[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out long observedAt)
                || !long.TryParse(f[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out long grantedAt)
                || !long.TryParse(f[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out long tickingSince))
            {
                continue;
            }

            if (!map.TryGetValue(f[0], out Dictionary<int, AbyssCorridorRecord>? forCharacter))
            {
                forCharacter = new Dictionary<int, AbyssCorridorRecord>();
                map[f[0]] = forCharacter;
            }

            forCharacter[ticketId] = new AbyssCorridorRecord(
                Math.Max(0, remaining), observedAt, Math.Max(0, grantedAt), Math.Max(0, tickingSince));
        }

        return new AbyssCorridorStore(map);
    }

    /// <summary>The raw record for a character's corridor, or null when there is none. No staleness rule is
    /// applied — callers that report to the user want <see cref="Standing"/>.</summary>
    public AbyssCorridorRecord? Get(string? identityHash, int ticketId) =>
        identityHash is { Length: > 0 }
        && _byHash.TryGetValue(identityHash, out Dictionary<int, AbyssCorridorRecord>? forCharacter)
        && forCharacter.TryGetValue(ticketId, out AbyssCorridorRecord record)
            ? record
            : null;

    /// <summary>The reserved ticket id that carries the entry proof for <paramref name="ticketId"/>.</summary>
    public static int EntryTicketFor(int ticketId) => EntryTicketBase + ticketId;

    /// <summary>Whether this character was watched INSIDE that corridor during the current 점령 cycle — the one
    /// fact that establishes the side holds the artifact, because the game only opens the portal while it does.
    /// <para>Per character, but not only about that character: it is handed to its server siblings by
    /// <c>AetherRoster.CapturedByServer</c>, since an occupation is a fact about the 진영.</para></summary>
    public bool EnteredThisCycle(string? identityHash, int ticketId, long nowMs) =>
        Get(identityHash, EntryTicketFor(ticketId)) is { } entry
        && AbyssCorridorCycle.IsWithin(entry.ObservedAtMs, BoundaryFor(identityHash, nowMs), nowMs);

    /// <summary>What this character can claim about one corridor right now: <c>null</c> unless it has been
    /// watched inside that corridor since the last 점령전, otherwise the remaining ms carried forward to
    /// <paramref name="nowMs"/> — its own reading when there is one from this cycle, and the full grant when
    /// there is not (walking in proves the corridor was stocked).
    /// <para><b>Why entry and not the value.</b> Until 2026-08-23 this asked whether the server had stated time
    /// on the corridor since the 점령전, which reads like the same question and is not. The 이용 시간 is a stock
    /// the character keeps: 콘팡 was still being told 유황나무 had time three and three-quarter hours after the
    /// 점령전 that handed that artifact to the other side, with the portal already closed. That one stale
    /// reading was then spread to five sibling characters as a full 2:10 by the server-wide union, which is the
    /// bug the player reported.</para></summary>
    public long? Standing(string? identityHash, int ticketId, long nowMs)
    {
        if (!EnteredThisCycle(identityHash, ticketId, nowMs))
        {
            return null;
        }

        return Reading(identityHash, ticketId, nowMs) ?? AbyssCorridorCatalog.FullGrantMs;
    }

    /// <summary>This character's OWN reading for one corridor, carried forward to <paramref name="nowMs"/>, or
    /// <c>null</c> when nothing was heard for it this cycle. Says nothing about whether the side holds the
    /// corridor — <see cref="Standing"/> answers that — so it is only used to put a real number on a corridor
    /// already established as held, in place of the full grant the panel would otherwise assume.</summary>
    public long? Reading(string? identityHash, int ticketId, long nowMs)
    {
        if (Get(identityHash, ticketId) is not { } record
            || !AbyssCorridorCycle.IsWithin(record.ObservedAtMs, BoundaryFor(identityHash, nowMs), nowMs))
        {
            return null;
        }

        return record.Project(nowMs);
    }

    /// <summary>Whether a full 0x610B snapshot has been seen for this character within the current cycle — i.e.
    /// whether the panel may say "어비스 회랑 기록 없음" at all. It reports that a snapshot was WATCHED — never
    /// that the side captured nothing, which one character's zeros cannot establish.</summary>
    public bool HasCycleWitness(string? identityHash, long nowMs)
    {
        long boundary = BoundaryFor(identityHash, nowMs);
        return Get(identityHash, WitnessTicketId) is { } witness
            && AbyssCorridorCycle.IsWithin(witness.ObservedAtMs, boundary, nowMs);
    }

    /// <summary>The moment before which this character's stored corridor data stops being credible.
    /// <para>Evidence first: if ANY record for this character was taken at or after the capture began (Wed/Sat
    /// 22:20 KST), the meter has heard this cycle's answer, and every record older than that is the previous
    /// occupation — retired on the spot rather than at some later hour. Only a character the meter has heard
    /// nothing from since falls back to the clock.</para>
    /// <para>Per character on purpose: an alt that was never logged in during 점령전 has no evidence of its own,
    /// and the main character's fresh readings say nothing about what the alt holds.</para></summary>
    private long BoundaryFor(string? identityHash, long nowMs)
    {
        long newest = identityHash is { Length: > 0 }
            && _byHash.TryGetValue(identityHash, out Dictionary<int, AbyssCorridorRecord>? forCharacter)
            ? Newest(forCharacter)
            : 0;

        // A record stamped in an impossible future must not be mistaken for "we have already heard this
        // cycle's answer" — that would retire every real record beside it. BoundaryFor screens for it.
        return AbyssCorridorCycle.BoundaryFor(newest, nowMs);
    }

    /// <summary>Record a reading. <paramref name="markGranted"/> stamps "the server stated time on this corridor
    /// here" — pass it for any value above zero. Returns false when the arguments are unusable or nothing
    /// changed, so the caller can skip re-serializing: these broadcasts repeat.
    /// <para>⚠️ That stamp is bookkeeping, NOT evidence the side holds the corridor, and nothing may gate a
    /// display on it — see <see cref="AbyssCorridorRecord.GrantedAtMs"/> for the reading that proved it wrong
    /// and <see cref="MarkEntered"/> for what does answer the question.</para></summary>
    /// <param name="tickingSinceMs"><c>null</c> = leave the clock exactly as it is, which is what almost every
    /// caller wants: a login snapshot filed while the character is standing in a corridor must correct the VALUE
    /// without also silently stopping the countdown. Pass 0 to stop it, or a timestamp to start it.</param>
    public bool Upsert(
        string? identityHash,
        int ticketId,
        long remainingMs,
        long observedAtMs,
        bool markGranted,
        long? tickingSinceMs = null)
    {
        if (string.IsNullOrWhiteSpace(identityHash) || ticketId <= 0 || observedAtMs <= 0)
        {
            return false;
        }

        Dictionary<int, AbyssCorridorRecord> forCharacter = ForCharacter(identityHash!);
        forCharacter.TryGetValue(ticketId, out AbyssCorridorRecord existing);

        // A reading older than the one already stored is not news — it is a late-arriving snapshot landing on
        // top of a delta that already superseded it, which would rewind the panel and (worse) restate a clock
        // that has since started. The 0x610B dump can wait up to 30 s for its identity, so this is reachable.
        if (existing.ObservedAtMs > observedAtMs)
        {
            return false;
        }

        // A grant stamp only ever moves forward, and only within the cycle it was taken in: carrying last
        // cycle's stamp over would keep claiming a corridor is occupied after the artifact changed hands.
        long grantedAt = markGranted
            ? observedAtMs
            : AbyssCorridorCycle.IsCurrentCycle(existing.GrantedAtMs, observedAtMs) ? existing.GrantedAtMs : 0;

        var updated = new AbyssCorridorRecord(
            Math.Max(0, remainingMs),
            observedAtMs,
            grantedAt,
            Math.Max(0, tickingSinceMs ?? existing.TickingSinceMs));

        if (existing == updated)
        {
            return false;
        }

        forCharacter[ticketId] = updated;
        Evict();
        return true;
    }

    /// <summary>Record that this character loaded that corridor's instance map — the proof of occupation the
    /// panel hangs everything on. Returns false when the arguments are unusable or the stamp does not move.
    /// <para>Stamped on every entry rather than only the first, so the proof stays inside the current cycle for
    /// as long as the character keeps going back, and expires on its own once it stops.</para></summary>
    public bool MarkEntered(string? identityHash, int ticketId, long atMs)
    {
        if (string.IsNullOrWhiteSpace(identityHash) || ticketId <= 0 || atMs <= 0)
        {
            return false;
        }

        Dictionary<int, AbyssCorridorRecord> forCharacter = ForCharacter(identityHash!);
        int entryId = EntryTicketFor(ticketId);
        if (forCharacter.TryGetValue(entryId, out AbyssCorridorRecord existing) && existing.ObservedAtMs >= atMs)
        {
            return false;
        }

        forCharacter[entryId] = new AbyssCorridorRecord(0, atMs, 0, 0);
        Evict();
        return true;
    }

    /// <summary>Note that a full snapshot was seen for this character. Returns false when it changes nothing.</summary>
    public bool MarkWitness(string? identityHash, long atMs)
    {
        if (string.IsNullOrWhiteSpace(identityHash) || atMs <= 0)
        {
            return false;
        }

        Dictionary<int, AbyssCorridorRecord> forCharacter = ForCharacter(identityHash!);
        if (forCharacter.TryGetValue(WitnessTicketId, out AbyssCorridorRecord existing)
            && existing.ObservedAtMs >= atMs)
        {
            return false;
        }

        forCharacter[WitnessTicketId] = new AbyssCorridorRecord(0, atMs, 0, 0);
        Evict();
        return true;
    }

    /// <summary>Stop the clock on whatever is ticking for this character, freezing the projected value. Called
    /// when the character leaves a corridor — the only moment an early exit can be turned into a real number,
    /// since the server says nothing until the budget is gone.</summary>
    public bool StopTicking(string? identityHash, long atMs)
    {
        if (string.IsNullOrWhiteSpace(identityHash)
            || atMs <= 0
            || !_byHash.TryGetValue(identityHash!, out Dictionary<int, AbyssCorridorRecord>? forCharacter))
        {
            return false;
        }

        bool changed = false;
        foreach (int ticketId in forCharacter.Keys.ToList())
        {
            AbyssCorridorRecord record = forCharacter[ticketId];
            if (record.TickingSinceMs <= 0)
            {
                continue;
            }

            forCharacter[ticketId] = record with
            {
                RemainingMs = record.Project(atMs),
                ObservedAtMs = atMs,
                TickingSinceMs = 0,
            };
            changed = true;
        }

        return changed;
    }

    /// <summary>Drop every record for the given characters. Wired to the panel's per-row ✕ and to the startup
    /// purge, so a forgotten character leaves nothing behind in any store.</summary>
    public bool RemoveAll(IEnumerable<string> identityHashes)
    {
        bool removed = false;
        foreach (string hash in identityHashes)
        {
            removed |= !string.IsNullOrWhiteSpace(hash) && _byHash.Remove(hash);
        }

        return removed;
    }

    /// <summary>Serialize for the settings key. Newest-recorded character first, and each character's corridors
    /// in ticket-id order, so the blob is stable across launches.</summary>
    public string Serialize() => string.Join(';', _byHash
        .OrderByDescending(kv => Newest(kv.Value))
        .SelectMany(kv => kv.Value.OrderBy(r => r.Key).Select(r => string.Join(',',
            kv.Key,
            r.Key.ToString(CultureInfo.InvariantCulture),
            r.Value.RemainingMs.ToString(CultureInfo.InvariantCulture),
            r.Value.ObservedAtMs.ToString(CultureInfo.InvariantCulture),
            r.Value.GrantedAtMs.ToString(CultureInfo.InvariantCulture),
            r.Value.TickingSinceMs.ToString(CultureInfo.InvariantCulture)))));

    private Dictionary<int, AbyssCorridorRecord> ForCharacter(string identityHash)
    {
        if (!_byHash.TryGetValue(identityHash, out Dictionary<int, AbyssCorridorRecord>? forCharacter))
        {
            forCharacter = new Dictionary<int, AbyssCorridorRecord>();
            _byHash[identityHash] = forCharacter;
        }

        return forCharacter;
    }

    private static long Newest(Dictionary<int, AbyssCorridorRecord> forCharacter) =>
        forCharacter.Count == 0 ? 0 : forCharacter.Max(r => r.Value.ObservedAtMs);

    /// <summary>Whether a character has anything worth keeping beyond the fact that it logged in. A witness row
    /// is written for EVERY character that connects, so on an alt-heavy account they would otherwise fill the
    /// cap with rows that say nothing and push out the main character's actual corridor time.</summary>
    private static bool HasTickets(Dictionary<int, AbyssCorridorRecord> forCharacter) =>
        forCharacter.Keys.Any(id => id != WitnessTicketId);

    private void Evict()
    {
        while (_byHash.Count > MaxCharacters)
        {
            string drop = _byHash
                .OrderBy(kv => HasTickets(kv.Value))
                .ThenBy(kv => Newest(kv.Value))
                .First().Key;
            _byHash.Remove(drop);
        }
    }
}

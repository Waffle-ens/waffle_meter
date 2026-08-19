using System.Globalization;

namespace WaffleMeter.App.Core;

/// <summary>One character's standing for one 어비스 회랑.</summary>
/// <param name="RemainingMs">The 이용 시간 left AS OF <paramref name="ObservedAtMs"/> — never a projection.</param>
/// <param name="ObservedAtMs">When the server last stated it.</param>
/// <param name="GrantedAtMs">When this corridor was last seen holding time (or dropping to zero, which proves it
/// held some). 0 = never — and that is the ONLY thing separating "이 캐릭터가 다 썼다" from "우리 서버가 그
/// 아티팩트를 점령하지 못했다", because both arrive on the wire as the same zero.</param>
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
/// that has held time this cycle, or that the panel has been told about by hand. Corridors that were merely
/// listed as zero in a snapshot are NOT stored; instead one witness row per character (ticket id 0) records that
/// a full snapshot was seen, which is what lets the panel say "이 캐릭터는 점령한 회랑이 없다" instead of
/// "모른다". Without that distinction a character the meter has never watched would look identical to one whose
/// faction lost every artifact.</para>
///
/// Pure and cap-bounded so the projection and the staleness rule are unit-testable.
/// </summary>
public sealed class AbyssCorridorStore
{
    /// <summary>Most-recent characters kept, matching <see cref="AetherPerCharacterStore.MaxCharacters"/>.</summary>
    public const int MaxCharacters = 48;

    /// <summary>Reserved ticket id for the per-character "a full snapshot was seen at" row.</summary>
    public const int WitnessTicketId = 0;

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

    /// <summary>What this character can claim about one corridor right now: <c>null</c> when nothing is known
    /// for the current 점령 cycle (never watched, or last watched before the most recent 점령전, after which the
    /// server may have re-stocked it), otherwise the remaining ms carried forward to <paramref name="nowMs"/>.
    /// <para>A record whose <c>GrantedAtMs</c> falls outside the current cycle is treated as unknown too: the
    /// only evidence it carries is a zero, and a zero this meter cannot date says nothing.</para></summary>
    public long? Standing(string? identityHash, int ticketId, long nowMs)
    {
        if (Get(identityHash, ticketId) is not { } record
            || !AbyssCorridorCycle.IsCurrentCycle(record.ObservedAtMs, nowMs)
            || !AbyssCorridorCycle.IsCurrentCycle(record.GrantedAtMs, nowMs))
        {
            return null;
        }

        return record.Project(nowMs);
    }

    /// <summary>Whether a full 0x610B snapshot has been seen for this character within the current cycle — i.e.
    /// whether "이 캐릭터에게 점령된 회랑이 없다" is something we actually know rather than merely have not seen.</summary>
    public bool HasCycleWitness(string? identityHash, long nowMs) =>
        Get(identityHash, WitnessTicketId) is { } witness
        && AbyssCorridorCycle.IsCurrentCycle(witness.ObservedAtMs, nowMs);

    /// <summary>Record a reading. <paramref name="markGranted"/> stamps "this corridor held time" — pass it for
    /// any value above zero, and for a 0x610C drop to zero (which can only follow a grant). Returns false when
    /// the arguments are unusable or nothing changed, so the caller can skip re-serializing: these broadcasts
    /// repeat.</summary>
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

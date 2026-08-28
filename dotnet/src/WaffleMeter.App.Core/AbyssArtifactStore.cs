using System.Globalization;
using WaffleMeter.Capture;

namespace WaffleMeter.App.Core;

/// <summary>One zone's 점령 현황 as the server last stated it, with the window the answer belongs to.</summary>
/// <param name="Owners">Which matchup slot holds each of the zone's three artifacts, in id order
/// (<c>ZoneId</c>, <c>ZoneId + 1</c>, <c>ZoneId + 2</c>). Always 1 or 2.</param>
public readonly record struct AbyssArtifactZoneState(
    int ZoneId,
    IReadOnlyList<int> Owners,
    long ObservedAtMs,
    long CycleStartMs,
    long CycleEndMs)
{
    /// <summary>Whether this reading still describes the 점령 주기 that is running at <paramref name="nowMs"/>.
    /// The window is the SERVER's own, so this needs no schedule arithmetic and no grace period — a record from
    /// the previous cycle fails it the instant that cycle ends.</summary>
    public bool IsCurrent(long nowMs) => CycleEndMs > nowMs && CycleStartMs > 0;

    /// <summary>How many of this zone's artifacts <paramref name="side"/> holds.</summary>
    public int CountFor(int side)
    {
        int n = 0;
        for (int i = 0; i < Owners.Count; i++)
        {
            if (Owners[i] == side)
            {
                n++;
            }
        }

        return n;
    }
}

/// <summary>
/// Remembers the 어비스 아티팩트 점령 현황 — who holds what, per server — plus how many artifacts the local
/// characters' own side holds, which is the only thing that says WHICH of the broadcast's two slots is ours.
///
/// <para><b>Why this exists.</b> Until 2026-08-29 the panel proved occupation by watching the character walk
/// into a corridor's instance map. That proof is sound but answers almost nothing: it names only the corridors
/// this character personally got into, and across the whole of August it fired six times. So 컨텐츠 관리 sat
/// blank for a player who had just walked through the abyss and could see, in the game's own UI, which
/// corridors their side held. The game states that answer outright in 0xE305/0xE307 — this store is where it
/// lives.</para>
///
/// <para><b>Ownership is per SERVER, the side is per CHARACTER.</b> An occupation is a fact about the 진영, so
/// one character's broadcast answers for its siblings — the same reasoning <c>AetherRoster.CapturedByServer</c>
/// already runs on. The count abnormal, on the other hand, is read off whoever was standing in the abyss, so it
/// is filed under that character and the roster spreads it across the server.</para>
///
/// <para><b>⚠️ The slot is not a race and must never be hard-coded.</b> One character on one server read slot 1
/// on 2026-08-23 and slot 2 on 2026-08-28 — the in-game guide's "서버 매칭 변경" on the wire. The side is
/// derived, every time, from <see cref="SideFor"/>.</para>
///
/// <para><b>Its OWN settings key</b> (<c>content.abyssArtifacts</c>), for the same reason
/// <c>AbyssCorridorStore</c> has one: a blob's field count is its format discriminator, an older build DROPS a
/// record it cannot parse, and those blobs are rewritten on every broadcast — so widening one would make a
/// single rollback permanent data loss. A key an old build has never heard of is simply ignored.</para>
///
/// Pure and cap-bounded so the cycle rule and the side derivation are unit-testable.
/// </summary>
public sealed class AbyssArtifactStore
{
    /// <summary>Servers kept. An account reaches a handful; the cap only stops an unbounded blob.</summary>
    public const int MaxServers = 16;

    /// <summary>Characters whose 점령 개수 is kept, matching <see cref="AbyssCorridorStore.MaxCharacters"/>.</summary>
    public const int MaxCharacters = 48;

    private const string OwnershipKind = "o";

    private const string CountKind = "c";

    private readonly Dictionary<int, Dictionary<int, AbyssArtifactZoneState>> _ownership;

    private readonly Dictionary<string, Dictionary<int, (int Count, long ObservedAtMs)>> _counts;

    private AbyssArtifactStore(
        Dictionary<int, Dictionary<int, AbyssArtifactZoneState>> ownership,
        Dictionary<string, Dictionary<int, (int, long)>> counts)
    {
        _ownership = ownership;
        _counts = counts;
    }

    /// <summary>Parse the serialized blob. Never throws — malformed records are skipped.
    /// <para>Every record is <c>kind,key,zoneId,value,observedAtMs,cycleStartMs,cycleEndMs</c> — one uniform
    /// seven-field shape for both kinds, so a reader never has to guess which it is holding. <c>o</c> keys on
    /// the server id and its value is the three owner slots as digits ("212"); <c>c</c> keys on the identity
    /// hash and its value is the count, with the two cycle columns zero because an abnormal carries no
    /// window.</para></summary>
    public static AbyssArtifactStore Parse(string? serialized)
    {
        var ownership = new Dictionary<int, Dictionary<int, AbyssArtifactZoneState>>();
        var counts = new Dictionary<string, Dictionary<int, (int, long)>>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(serialized))
        {
            return new AbyssArtifactStore(ownership, counts);
        }

        foreach (string record in serialized.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] f = record.Split(',');
            if (f.Length != 7
                || string.IsNullOrWhiteSpace(f[1])
                || !int.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int zoneId)
                || !long.TryParse(f[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out long observedAt)
                || !long.TryParse(f[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out long cycleStart)
                || !long.TryParse(f[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out long cycleEnd))
            {
                continue;
            }

            if (f[0] == OwnershipKind)
            {
                if (!int.TryParse(f[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int serverId)
                    || serverId <= 0
                    || !TryReadOwners(f[3], out int[]? owners))
                {
                    continue;
                }

                if (!ownership.TryGetValue(serverId, out Dictionary<int, AbyssArtifactZoneState>? forServer))
                {
                    ownership[serverId] = forServer = [];
                }

                forServer[zoneId] = new AbyssArtifactZoneState(zoneId, owners!, observedAt, cycleStart, cycleEnd);
            }
            else if (f[0] == CountKind)
            {
                if (!int.TryParse(f[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int count)
                    || count is < 1 or > AbyssArtifactParser.ArtifactsPerZone)
                {
                    continue;
                }

                if (!counts.TryGetValue(f[1], out Dictionary<int, (int, long)>? forCharacter))
                {
                    counts[f[1]] = forCharacter = [];
                }

                forCharacter[zoneId] = (count, observedAt);
            }
        }

        return new AbyssArtifactStore(ownership, counts);
    }

    /// <summary>File one zone's 점령 현황 for a server. Returns false when nothing changed, so the caller can
    /// skip re-serializing — the broadcast repeats on every world-map open.</summary>
    public bool UpsertOwnership(
        int serverId,
        int zoneId,
        long cycleStartMs,
        long cycleEndMs,
        IReadOnlyList<AbyssArtifactHolding> holdings,
        long observedAtMs)
    {
        if (serverId <= 0
            || zoneId <= 0
            || observedAtMs <= 0
            || cycleStartMs <= 0
            || cycleEndMs <= cycleStartMs
            || holdings is null
            || holdings.Count != AbyssArtifactParser.ArtifactsPerZone)
        {
            return false;
        }

        var owners = new int[AbyssArtifactParser.ArtifactsPerZone];
        foreach (AbyssArtifactHolding holding in holdings)
        {
            int index = holding.ArtifactId - zoneId;
            if (index < 0 || index >= owners.Length || holding.OwnerSide is < 1 or > 2)
            {
                return false; // an artifact that is not this zone's: the frame was not what we thought
            }

            owners[index] = holding.OwnerSide;
        }

        foreach (int owner in owners)
        {
            if (owner == 0)
            {
                return false; // the three ids did not cover the zone
            }
        }

        if (!_ownership.TryGetValue(serverId, out Dictionary<int, AbyssArtifactZoneState>? forServer))
        {
            _ownership[serverId] = forServer = [];
        }

        // A reading older than the one already stored is not news. The 0xE307 login broadcast can land after a
        // 0xE305 from the zone the character is walking into, and re-filing the older one would rewind nothing
        // useful and reset the stamp.
        if (forServer.TryGetValue(zoneId, out AbyssArtifactZoneState existing)
            && existing.ObservedAtMs > observedAtMs)
        {
            return false;
        }

        var updated = new AbyssArtifactZoneState(zoneId, owners, observedAtMs, cycleStartMs, cycleEndMs);
        if (existing.ZoneId == zoneId
            && existing.CycleStartMs == cycleStartMs
            && existing.CycleEndMs == cycleEndMs
            && existing.Owners.SequenceEqual(owners))
        {
            return false; // same answer, same cycle — only the stamp would move
        }

        forServer[zoneId] = updated;
        EvictServers();
        return true;
    }

    /// <summary>File how many artifacts this character's side holds in one zone.</summary>
    public bool UpsertCount(string? identityHash, int zoneId, int count, long observedAtMs)
    {
        if (string.IsNullOrWhiteSpace(identityHash)
            || zoneId <= 0
            || observedAtMs <= 0
            || count is < 1 or > AbyssArtifactParser.ArtifactsPerZone)
        {
            return false;
        }

        if (!_counts.TryGetValue(identityHash!, out Dictionary<int, (int Count, long ObservedAtMs)>? forCharacter))
        {
            _counts[identityHash!] = forCharacter = [];
        }

        if (forCharacter.TryGetValue(zoneId, out (int Count, long ObservedAtMs) existing)
            && existing.Count == count
            && existing.ObservedAtMs >= observedAtMs)
        {
            return false;
        }

        forCharacter[zoneId] = (count, Math.Max(existing.ObservedAtMs, observedAtMs));
        EvictCharacters();
        return true;
    }

    /// <summary>Whether a 점령 현황 for this server is on file and still inside its cycle. False is what makes
    /// the panel say it has not heard rather than that the side holds nothing.</summary>
    public bool HasOwnership(int serverId, long nowMs) => Zones(serverId, nowMs).Count > 0;

    /// <summary>The zones on file for a server that are still inside their cycle.</summary>
    public IReadOnlyList<AbyssArtifactZoneState> Zones(int serverId, long nowMs)
    {
        if (!_ownership.TryGetValue(serverId, out Dictionary<int, AbyssArtifactZoneState>? forServer))
        {
            return [];
        }

        var live = new List<AbyssArtifactZoneState>(forServer.Count);
        foreach (AbyssArtifactZoneState zone in forServer.Values)
        {
            if (zone.IsCurrent(nowMs))
            {
                live.Add(zone);
            }
        }

        return live;
    }

    /// <summary>
    /// Which matchup slot this character belongs to, or null when it cannot be settled.
    ///
    /// <para>The 점령 개수 abnormal says how many artifacts our side holds in a zone; the broadcast says how many
    /// each slot holds. A zone has three artifacts and the parser only accepts owners 1 and 2, so the two slots
    /// can never hold the same number — one zone with a count is therefore enough, and a second zone is a free
    /// cross-check rather than a requirement.</para>
    ///
    /// <para>A count is only used inside the cycle its zone is in: an abnormal from before the 점령전 describes
    /// an occupation that has since been redealt. And when two zones disagree about the slot, the answer is
    /// null — that means one of the two readings is stale or misparsed, and claiming corridors off the wrong
    /// slot would show the enemy's.</para>
    /// </summary>
    public int? SideFor(int serverId, string? identityHash, long nowMs)
    {
        if (string.IsNullOrWhiteSpace(identityHash)
            || !_counts.TryGetValue(identityHash!, out Dictionary<int, (int Count, long ObservedAtMs)>? forCharacter))
        {
            return null;
        }

        int? resolved = null;
        foreach (AbyssArtifactZoneState zone in Zones(serverId, nowMs))
        {
            if (!forCharacter.TryGetValue(zone.ZoneId, out (int Count, long ObservedAtMs) count)
                || count.ObservedAtMs < zone.CycleStartMs
                || count.ObservedAtMs >= zone.CycleEndMs)
            {
                continue;
            }

            int? here = null;
            for (int side = 1; side <= 2; side++)
            {
                if (zone.CountFor(side) != count.Count)
                {
                    continue;
                }

                if (here is not null)
                {
                    here = null; // both slots hold that many: this zone cannot decide
                    break;
                }

                here = side;
            }

            if (here is null)
            {
                continue;
            }

            if (resolved is not null && resolved != here)
            {
                return null; // the two zones name different slots — trust neither
            }

            resolved = here;
        }

        return resolved;
    }

    /// <summary>The corridor ticket ids <paramref name="side"/> holds on this server right now, in catalog
    /// order. Empty when no live 점령 현황 is on file.</summary>
    public IReadOnlyList<int> HeldTicketIds(int serverId, int side, long nowMs)
    {
        if (side is < 1 or > 2)
        {
            return [];
        }

        var held = new HashSet<int>();
        foreach (AbyssArtifactZoneState zone in Zones(serverId, nowMs))
        {
            for (int i = 0; i < zone.Owners.Count; i++)
            {
                if (zone.Owners[i] != side)
                {
                    continue;
                }

                if (AbyssCorridorCatalog.ByArtifactId(zone.ZoneId + i) is { } corridor)
                {
                    held.Add(corridor.TicketId);
                }
            }
        }

        var ordered = new List<int>(held.Count);
        foreach (AbyssCorridorInfo corridor in AbyssCorridorCatalog.All)
        {
            if (held.Contains(corridor.TicketId))
            {
                ordered.Add(corridor.TicketId);
            }
        }

        return ordered;
    }

    /// <summary>When the 점령 주기 covering this server ends, or 0 when nothing live is on file. Used to bound
    /// a corridor's ticket reading to the occupation it was granted under.</summary>
    public long CycleEndMs(int serverId, long nowMs)
    {
        long end = 0;
        foreach (AbyssArtifactZoneState zone in Zones(serverId, nowMs))
        {
            end = Math.Max(end, zone.CycleEndMs);
        }

        return end;
    }

    /// <summary>When the 점령 주기 covering this server began, or 0. The earliest zone start is used: the two
    /// zones settle seconds apart and a reading taken between them still belongs to this cycle.</summary>
    public long CycleStartMs(int serverId, long nowMs)
    {
        long start = 0;
        foreach (AbyssArtifactZoneState zone in Zones(serverId, nowMs))
        {
            start = start == 0 ? zone.CycleStartMs : Math.Min(start, zone.CycleStartMs);
        }

        return start;
    }

    /// <summary>Drop every count row for the given characters. Wired to the panel's per-row ✕ and the startup
    /// purge, the same as the other per-character stores. Server ownership is not per character and stays.</summary>
    public bool RemoveAll(IEnumerable<string> identityHashes)
    {
        bool removed = false;
        foreach (string hash in identityHashes)
        {
            removed |= !string.IsNullOrWhiteSpace(hash) && _counts.Remove(hash);
        }

        return removed;
    }

    /// <summary>Serialize for the settings key. Ownership first, then counts, each in a stable order so the
    /// blob does not churn between launches.</summary>
    public string Serialize()
    {
        var parts = new List<string>();
        foreach ((int serverId, Dictionary<int, AbyssArtifactZoneState> forServer) in _ownership.OrderBy(kv => kv.Key))
        {
            foreach ((int zoneId, AbyssArtifactZoneState zone) in forServer.OrderBy(kv => kv.Key))
            {
                parts.Add(string.Join(',',
                    OwnershipKind,
                    serverId.ToString(CultureInfo.InvariantCulture),
                    zoneId.ToString(CultureInfo.InvariantCulture),
                    string.Concat(zone.Owners.Select(o => o.ToString(CultureInfo.InvariantCulture))),
                    zone.ObservedAtMs.ToString(CultureInfo.InvariantCulture),
                    zone.CycleStartMs.ToString(CultureInfo.InvariantCulture),
                    zone.CycleEndMs.ToString(CultureInfo.InvariantCulture)));
            }
        }

        foreach ((string hash, Dictionary<int, (int Count, long ObservedAtMs)> forCharacter) in
                 _counts.OrderByDescending(kv => Newest(kv.Value)))
        {
            foreach ((int zoneId, (int count, long observedAt)) in forCharacter.OrderBy(kv => kv.Key))
            {
                parts.Add(string.Join(',',
                    CountKind,
                    hash,
                    zoneId.ToString(CultureInfo.InvariantCulture),
                    count.ToString(CultureInfo.InvariantCulture),
                    observedAt.ToString(CultureInfo.InvariantCulture),
                    "0",
                    "0"));
            }
        }

        return string.Join(';', parts);
    }

    private static bool TryReadOwners(string value, out int[]? owners)
    {
        owners = null;
        if (value.Length != AbyssArtifactParser.ArtifactsPerZone)
        {
            return false;
        }

        var parsed = new int[value.Length];
        for (int i = 0; i < value.Length; i++)
        {
            int side = value[i] - '0';
            if (side is < 1 or > 2)
            {
                return false;
            }

            parsed[i] = side;
        }

        owners = parsed;
        return true;
    }

    private static long Newest(Dictionary<int, (int Count, long ObservedAtMs)> forCharacter) =>
        forCharacter.Count == 0 ? 0 : forCharacter.Max(kv => kv.Value.ObservedAtMs);

    private void EvictServers()
    {
        while (_ownership.Count > MaxServers)
        {
            int drop = _ownership
                .OrderBy(kv => kv.Value.Count == 0 ? 0 : kv.Value.Max(z => z.Value.ObservedAtMs))
                .First().Key;
            _ownership.Remove(drop);
        }
    }

    private void EvictCharacters()
    {
        while (_counts.Count > MaxCharacters)
        {
            string drop = _counts.OrderBy(kv => Newest(kv.Value)).First().Key;
            _counts.Remove(drop);
        }
    }
}

namespace WaffleMeter.App.Core;

/// <summary>One currency on a character row: the remembered balance, or null when this character has never had it
/// stated (a meter started mid-session only learns a balance when it changes).</summary>
public readonly record struct CurrencyCell(CurrencyInfo Currency, long? Count, long ObservedAtMs)
{
    public bool Known => Count.HasValue;
}

/// <summary>One character's share of a server's 총 키나.</summary>
/// <param name="Partial">Only some of the character's kinah balances (각인 · 거래 가능 · 캐릭터 창고) have ever been
/// stated — a meter started mid-session learns them one change at a time — so <paramref name="Kina"/> is what is
/// known, not everything the character holds.</param>
public readonly record struct CharacterKina(
    string IdentityHash, string Label, long Kina, long ObservedAtMs, bool Partial = false);

/// <summary>
/// One 총 키나 line — everything one server holds in kinah, per the owner's definition (2026-10-07): every
/// character's own kinah (키나(각인) + 키나, and 캐릭터 창고 kinah when there is any) plus the 서버 창고 counted
/// ONCE.
/// </summary>
/// <param name="ServerStorage">The 서버 창고 kinah, or null when it has never been stated for this server.</param>
/// <param name="Characters">The characters on this server whose kinah is on file, highest first — including
/// <see cref="CharacterKina.Partial"/> ones, which add only what is known of them.</param>
/// <param name="CharactersWithoutRecord">Characters on this server the panel lists but whose kinah has never been
/// stated — counted as nothing.</param>
public readonly record struct ServerKinaLine(
    int Server,
    string ServerLabel,
    long Total,
    long? ServerStorage,
    long ServerStorageObservedAtMs,
    IReadOnlyList<CharacterKina> Characters,
    int CharactersWithoutRecord,
    bool IsCurrentServer)
{
    /// <summary><see cref="Total"/> is only a lower bound: some listed character's kinah is unknown in whole
    /// (<see cref="CharactersWithoutRecord"/>) or in part (<see cref="CharacterKina.Partial"/>), or the 서버 창고
    /// has never been stated. Every unknown balance counts as nothing, so the real total can only be higher.</summary>
    public bool IsLowerBound =>
        CharactersWithoutRecord > 0 || ServerStorage is null || Characters.Any(c => c.Partial);
}

/// <summary>
/// The currency side of the 컨텐츠 관리 list. Pure (no WPF) so the 총 키나 arithmetic — and above all the rule that
/// the 서버 창고 is counted once per server, not once per character — is unit-testable.
/// </summary>
public static class CurrencyRoster
{
    /// <summary>The character-scope balances for one row, in catalog order (unknown ones carry a null count).</summary>
    public static IReadOnlyList<CurrencyCell> CellsFor(CurrencyStore? currencies, string identityHash)
    {
        if (currencies is null)
        {
            return [];
        }

        var cells = new List<CurrencyCell>();
        foreach (CurrencyInfo info in CurrencyCatalog.All)
        {
            if (info.Scope != CurrencyScope.Character)
            {
                continue;
            }

            CurrencyRecord? record = currencies.Character(identityHash, info.Slug);
            cells.Add(new CurrencyCell(info, record?.Count, record?.ObservedAtMs ?? 0));
        }

        return cells;
    }

    /// <summary>A row's cells on the 재화 관리 tab, in display order (<see cref="CurrencyCatalog.ChipSlugs"/>). 캐릭터
    /// 창고 kinah is not among them — see <see cref="CharacterStorageOf"/>.</summary>
    public static IReadOnlyList<CurrencyCell> ChipCells(AetherRosterRow row) =>
        CurrencyCatalog.ChipSlugs
            .SelectMany(slug => row.CurrencyCells.Where(c => c.Currency.Slug == slug))
            .ToList();

    /// <summary>Whether a cell is one of the character's own kinah balances (<see cref="CurrencyCatalog.CharacterKinaSlugs"/>)
    /// rather than a point currency. The 재화 관리 tab gives kinah a line of its own: it is the only balance that runs
    /// into 억, so a cell holding it needs half the row where a point currency fits a third.</summary>
    public static bool IsKina(CurrencyCell cell) =>
        CurrencyCatalog.CharacterKinaSlugs.Contains(cell.Currency.Slug, StringComparer.Ordinal);

    /// <summary>The row's 캐릭터 창고 kinah, or null when the row carries no such cell. It rides the 키나 cell's
    /// tooltip rather than taking a cell of its own: it is empty for nearly everyone, and a column of permanent
    /// zeros would crowd out the five that matter.</summary>
    public static CurrencyCell? CharacterStorageOf(AetherRosterRow row) =>
        row.CurrencyCells
            .Where(c => c.Currency.Slug == CurrencyCatalog.CharacterStorageKina)
            .Select(c => (CurrencyCell?)c)
            .FirstOrDefault();

    /// <summary>When the server last stated any of this character's balances, or 0 when none ever was — the age
    /// the 재화 관리 row shows beside the name.</summary>
    public static long NewestObservedAtMs(AetherRosterRow row) =>
        row.CurrencyCells.Where(c => c.Known).Select(c => c.ObservedAtMs).DefaultIfEmpty(0).Max();

    /// <summary>Whether the 재화 관리 tab has anything to show: a balance on some listed character, or a 총 키나 line
    /// (which a 서버 창고 record alone can produce). When neither, the tab shows its empty state instead of a list
    /// of rows that read "—" from end to end.</summary>
    public static bool AnyOnFile(IReadOnlyList<AetherRosterRow> rows, IReadOnlyList<ServerKinaLine> serverKina) =>
        serverKina.Count > 0 || rows.Any(r => r.CurrenciesKnown);

    /// <summary>
    /// One 총 키나 line per server the panel lists a character on. The characters are the 컨텐츠 관리 rows
    /// themselves (<paramref name="characters"/>), resolved to a server the same way the rows are; a server shows
    /// a line only when something about its kinah is actually on file.
    /// <para>Current character's server first, then by server id — the order does not move as balances do.</para>
    /// </summary>
    public static IReadOnlyList<ServerKinaLine> ServerKina(
        AetherPerCharacterStore characters,
        IEnumerable<AetherRosterName>? names = null,
        CurrencyStore? currencies = null,
        string? currentHash = null)
    {
        if (currencies is null)
        {
            return [];
        }

        var byHash = new Dictionary<string, AetherRosterName>(StringComparer.Ordinal);
        foreach (AetherRosterName name in names ?? [])
        {
            if (!string.IsNullOrWhiteSpace(name.IdentityHash))
            {
                byHash[name.IdentityHash] = name;
            }
        }

        var perServer = new SortedDictionary<int, (List<CharacterKina> Known, int Unknown)>();
        int currentServer = 0;
        foreach ((string hash, AetherSnapshot snapshot) in characters.All())
        {
            byHash.TryGetValue(hash, out AetherRosterName known);
            int server = snapshot.Server > 0 ? snapshot.Server : known.Server;
            if (server <= 0)
            {
                continue; // a character whose server is unknown cannot be put on any server's line
            }

            if (currentHash != null && string.Equals(hash, currentHash, StringComparison.Ordinal))
            {
                currentServer = server;
            }

            if (!perServer.TryGetValue(server, out (List<CharacterKina> Known, int Unknown) entry))
            {
                entry = (new List<CharacterKina>(), 0);
            }

            if (KinaOf(currencies, hash) is { } kina)
            {
                string label = FirstNonBlank(snapshot.Nickname, known.Nickname) ?? "이름 없는 캐릭터";
                entry.Known.Add(new CharacterKina(hash, label, kina.Amount, kina.ObservedAtMs, kina.Partial));
            }
            else
            {
                entry.Unknown++;
            }

            perServer[server] = entry;
        }

        var lines = new List<ServerKinaLine>();
        foreach ((int server, (List<CharacterKina> known, int unknown)) in perServer)
        {
            CurrencyRecord? storage = currencies.Server(server, CurrencyCatalog.ServerStorageKina);
            if (known.Count == 0 && storage is null)
            {
                continue; // nothing about this server's kinah is on file: no line beats a line of zeros
            }

            long total = (storage?.Count ?? 0) + known.Sum(c => c.Kina);
            lines.Add(new ServerKinaLine(
                server,
                ServerNames.GetServerLabel(server),
                total,
                storage?.Count,
                storage?.ObservedAtMs ?? 0,
                known.OrderByDescending(c => c.Kina).ThenBy(c => c.Label, StringComparer.Ordinal).ToList(),
                unknown,
                IsCurrentServer: server == currentServer));
        }

        return lines.OrderByDescending(l => l.IsCurrentServer).ThenBy(l => l.Server).ToList();
    }

    /// <summary>A character's own kinah — 각인 + 거래 가능 + 캐릭터 창고 — or null when none of the three has ever been
    /// stated for it. A balance never stated counts as nothing; that is what keeps a half-known character from
    /// reading as "has no kinah" while still adding what is known — and <c>Partial</c> says that is what happened,
    /// so the sum is not shown as the character's whole kinah.</summary>
    private static (long Amount, long ObservedAtMs, bool Partial)? KinaOf(CurrencyStore currencies, string hash)
    {
        long amount = 0;
        long newest = 0;
        bool any = false;
        bool missing = false;
        foreach (string slug in CurrencyCatalog.CharacterKinaSlugs)
        {
            if (currencies.Character(hash, slug) is not { } record)
            {
                missing = true;
                continue;
            }

            any = true;
            amount += record.Count;
            newest = Math.Max(newest, record.ObservedAtMs);
        }

        return any ? (amount, newest, missing) : null;
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

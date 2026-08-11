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
    IReadOnlyList<WeeklyContentCell>? Weekly = null)
{
    /// <summary>The weekly raids in catalog order, never null.</summary>
    public IReadOnlyList<WeeklyContentCell> WeeklyCells => Weekly ?? [];

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
        long nowMs = 0)
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

        var rows = new List<AetherRosterRow>();
        foreach ((string hash, AetherSnapshot snapshot) in store.All())
        {
            byHash.TryGetValue(hash, out AetherRosterName known);

            // The record's own nickname wins: it is written from the live executor every broadcast, whereas a
            // consent entry can predate a rename. Fall back to the consent list for records written before the
            // name was stored, then to a stable stub so the row still shows its balance.
            string? nickname = FirstNonBlank(snapshot.Nickname, known.Nickname);
            int server = snapshot.Server > 0 ? snapshot.Server : known.Server;

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
                Weekly: WeeklyFor(weekly, hash, at)));
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

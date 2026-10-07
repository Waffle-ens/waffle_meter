using System.Globalization;
using WaffleMeter.Capture;

namespace WaffleMeter.App.Core;

/// <summary>Whose balance a currency row is. The 서버 창고 is shared by every character of one account on one
/// server, so it is filed once per server — filing it per character would count it once per character the
/// moment the panel sums a server.</summary>
public enum CurrencyScope
{
    Character,
    Server,
}

/// <summary>One balance the 컨텐츠 관리 panel tracks: an item id in one container.</summary>
/// <param name="Slug">Stable ASCII key used in the settings blob. NEVER change one — it is the persisted identity
/// of every character's record, and a rename orphans everyone's history.</param>
/// <param name="ItemId">The client's item id (<see cref="CurrencyItemParser"/>).</param>
/// <param name="Container">Which container's stacks make up this balance (<c>EItemContainerType</c>).</param>
/// <param name="Scope">Character, or the 서버 창고 shared by the server.</param>
/// <param name="Name">The game's own name, for tooltips.</param>
/// <param name="IconFile">File name under <c>Assets/Icons</c> (the client's own currency art).</param>
public readonly record struct CurrencyInfo(
    string Slug,
    int ItemId,
    int Container,
    CurrencyScope Scope,
    string Name,
    string IconFile);

/// <summary>
/// The currencies the 컨텐츠 관리 panel shows. All five travel as inventory ITEMS (the client's Item table types
/// them <c>EItemType::Currency</c>), so a balance is the sum of that item's stacks in one container.
/// <para>Tradeable kinah is the only one that can leave the inventory: the 서버 창고 (container 2, per server) and
/// the 캐릭터 창고 (container 15, per character). The four bound currencies are <c>CanStorage 0</c> and only ever
/// sit in the inventory. 키나(통합) is not listed — the client computes it (각인 + 거래 가능, inventory only) and
/// it never reaches the wire.</para>
/// </summary>
public static class CurrencyCatalog
{
    public const string BoundKina = "kinaBound";

    public const string Kina = "kina";

    public const string CharacterStorageKina = "kinaCharStorage";

    public const string ServerStorageKina = "kinaServerStorage";

    public const string AbyssPoint = "abyssPoint";

    public const string DreamShard = "dreamShard";

    public const string TrialMark = "trialMark";

    /// <summary>The icon for a kinah total — the client's 키나(통합) coin.</summary>
    public const string TotalKinaIcon = "currency_kina_total.png";

    public static IReadOnlyList<CurrencyInfo> All { get; } =
    [
        new(BoundKina, CurrencyItemParser.BoundKinaId, CurrencyItemParser.InventoryContainer,
            CurrencyScope.Character, "키나(각인)", "currency_kina_bound.png"),
        new(Kina, CurrencyItemParser.KinaId, CurrencyItemParser.InventoryContainer,
            CurrencyScope.Character, "키나", "currency_kina.png"),
        new(CharacterStorageKina, CurrencyItemParser.KinaId, CurrencyItemParser.CharacterStorageContainer,
            CurrencyScope.Character, "키나 (캐릭터 창고)", "currency_kina.png"),
        new(AbyssPoint, CurrencyItemParser.AbyssPointId, CurrencyItemParser.InventoryContainer,
            CurrencyScope.Character, "어비스 포인트", "currency_abyss_point.png"),
        new(DreamShard, CurrencyItemParser.DreamShardId, CurrencyItemParser.InventoryContainer,
            CurrencyScope.Character, "몽환의 파편", "currency_dream_shard.png"),
        new(TrialMark, CurrencyItemParser.TrialMarkId, CurrencyItemParser.InventoryContainer,
            CurrencyScope.Character, "극복의 증표", "currency_trial_mark.png"),
        new(ServerStorageKina, CurrencyItemParser.KinaId, CurrencyItemParser.ServerStorageContainer,
            CurrencyScope.Server, "키나 (서버 창고)", "currency_kina.png"),
    ];

    /// <summary>The 재화 관리 tab's cells, in display order. 캐릭터 창고 kinah is not a cell of its own — it rides
    /// the tradeable kinah cell's tooltip.</summary>
    public static IReadOnlyList<string> ChipSlugs { get; } = [BoundKina, Kina, AbyssPoint, DreamShard, TrialMark];

    /// <summary>A character's own kinah, the part of a server's 총 키나 that is per character.</summary>
    public static IReadOnlyList<string> CharacterKinaSlugs { get; } = [BoundKina, Kina, CharacterStorageKina];

    public static CurrencyInfo? BySlug(string? slug)
    {
        foreach (CurrencyInfo info in All)
        {
            if (string.Equals(info.Slug, slug, StringComparison.Ordinal))
            {
                return info;
            }
        }

        return null;
    }

    /// <summary>The balance a stack in <paramref name="container"/> belongs to, or null for one the panel does
    /// not track (큐나 in container 3, a bound currency somewhere unexpected).</summary>
    public static CurrencyInfo? For(int itemId, int container)
    {
        foreach (CurrencyInfo info in All)
        {
            if (info.ItemId == itemId && info.Container == container)
            {
                return info;
            }
        }

        return null;
    }
}

/// <summary>Number formatting for currency balances.</summary>
public static class CurrencyFormat
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>
    /// The chip text: Korean units, two places at most, TRUNCATED rather than rounded (same rule as
    /// <see cref="MeterFormat.FormatAmount"/>) so a chip never shows more than is there.
    /// <para>329,454,882 → "3억 2,945만" · 4,454,882 → "445만" · 300,000,000 → "3억" · 25,611 → "25,611".
    /// Below 100,000 the exact number is already short enough to read whole.</para>
    /// </summary>
    public static string Compact(long amount)
    {
        if (amount < 0)
        {
            return "-" + Compact(amount == long.MinValue ? long.MaxValue : -amount);
        }

        if (amount < 100_000)
        {
            return amount.ToString("N0", Inv);
        }

        const long Man = 10_000;
        const long Eok = 100_000_000;
        const long Jo = 1_000_000_000_000;

        (long major, long minor, string majorUnit, string minorUnit) =
            amount >= Jo ? (amount / Jo, amount % Jo / Eok, "조", "억")
            : amount >= Eok ? (amount / Eok, amount % Eok / Man, "억", "만")
            : (amount / Man, 0L, "만", string.Empty);

        string head = major.ToString("N0", Inv) + majorUnit;
        return minor > 0 ? $"{head} {minor.ToString("N0", Inv)}{minorUnit}" : head;
    }

    /// <summary>The exact number, for tooltips: "329,454,882".</summary>
    public static string Exact(long amount) => amount.ToString("N0", Inv);
}

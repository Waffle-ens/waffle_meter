using System.Globalization;

namespace WaffleMeter.App.Core;

/// <summary>
/// Carries a saved field-boss setting across a <see cref="WaffleMeter.Capture.FieldBossCatalog"/> code change.
/// <para><b>Why (2026-10-07).</b> The 어비스 중층 rows had their wire slots mis-paired: the catalog listed two
/// "분노한 수호신장 나흐마" as 2600150 / 2600156, which are M_AR2 NPCs and not on map 22 at all. The client's
/// <c>WorldMapFieldNamed</c> puts that name on slots 2201/2202 as 2600479 / 2600480, and those are the codes the
/// catalog uses now. A user who unticked a 나흐마 row in the picker holds the old codes in
/// <c>alarms.fieldBossDisabled</c>; left alone they would match no row, so the boss would quietly start alerting
/// again and the picker would show it ticked.</para>
/// <para><b>The pairing is by the name the user saw.</b> Both old rows read "분노한 수호신장 나흐마", so they go to
/// the two rows that read that now, in slot order: 2600150 → 2600479, 2600156 → 2600480. 처형관 드라모스 /
/// 반역자 듀칼 / 파멸자 마라카 (2600520/521/522) keep their codes — the picker showed those names against those
/// codes, and the codes now sit on their correct slots, so what the user turned off is still what is off.</para>
/// <para>Pure and idempotent: the new codes are not in the map, so a migrated value migrates to itself, and a
/// value with no old code comes back as the very same string (the caller writes only on change).</para>
/// </summary>
public static class FieldBossCodeMigration
{
    /// <summary>Retired catalog code → the code that replaced it.</summary>
    public static IReadOnlyDictionary<int, int> Replaced { get; } = new Dictionary<int, int>
    {
        [2600150] = 2600479, // 분노한 수호신장 나흐마 (옛 중층 첫 줄) → 슬롯 2201
        [2600156] = 2600480, // 분노한 수호신장 나흐마 (옛 중층 둘째 줄) → 슬롯 2202
    };

    /// <summary>
    /// <c>alarms.fieldBossDisabled</c> with every retired code replaced in place. Order and every other entry are
    /// kept as they are; if a replacement lands on a code already in the list the later copy is dropped (the
    /// value is a set). Returns <paramref name="csv"/> itself when it holds no retired code.
    /// </summary>
    public static string MigrateDisabledCsv(string csv)
    {
        string[] parts = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (!parts.Any(p => TryCode(p, out int c) && Replaced.ContainsKey(c)))
        {
            return csv;
        }

        var kept = new List<string>(parts.Length);
        var seen = new HashSet<int>();
        foreach (string part in parts)
        {
            if (!TryCode(part, out int code))
            {
                kept.Add(part); // not a code — not this migration's to judge (ParseCodeSet skips it anyway)
                continue;
            }

            if (Replaced.TryGetValue(code, out int current))
            {
                code = current;
            }

            if (seen.Add(code))
            {
                kept.Add(code.ToString(CultureInfo.InvariantCulture));
            }
        }

        return string.Join(",", kept);
    }

    private static bool TryCode(string part, out int code) =>
        int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out code);
}

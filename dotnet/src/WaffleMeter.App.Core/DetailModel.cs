using WaffleMeter.Data;

namespace WaffleMeter.App.Core;

/// <summary>One skill row in the detail table (a direct hit row, a "- 지속" DOT row, or a merged
/// chain group). Pct fields are null for DOT rows (rendered as "-").</summary>
public sealed record DetailSkillRow(
    int Code,
    string Name,
    int Hits,
    long Damage,
    bool IsDot,
    int? CritPct,
    int? StrongPct,
    int? PerfectPct,
    int? BackPct,
    int? ParryPct,
    double Ratio);

/// <summary>A chain group: the merged/displayed skill + its child rows (expandable when >1).</summary>
public sealed record DetailSkillGroup(DetailSkillRow Merged, IReadOnlyList<DetailSkillRow> Children, bool HasChildren);

public sealed record DetailBuffRow(int Code, string Name, double Rate, string Description);

public sealed record DetailBuffSection(string Label, IReadOnlyList<DetailBuffRow> Rows);

/// <summary>
/// Pure computation of a player's detail breakdown — summary stats, chain-grouped skill table, and
/// own-buff / boss-debuff uptime sections — ported from the React useDetails + DetailsPanel +
/// BuffRateSection logic. No WPF deps so it is unit-testable; the window/VM only formats + styles it.
/// </summary>
public sealed record DetailModel(
    long TotalDamage,
    double Contribution,
    double CritPct,
    double StrongPct,
    double PerfectPct,
    double BackPct,
    double ParryPct,
    long CombatMs,
    IReadOnlyList<DetailSkillGroup> Skills,
    IReadOnlyList<DetailBuffSection> Buffs,
    IReadOnlyList<DetailBuffSection> Debuffs)
{
    private static readonly (int Main, int[] Children)[] ChainGroups =
    {
        (11020000, new[] { 11030000, 11040000 }),
        (12010000, new[] { 12040000, 12020000, 12030000 }),
        (13010000, new[] { 13030000, 13040000 }),
        // 궁성 저격(14020000) 연계 = 연사(14030000) + 나선 화살(14040000). 속사(14340000)는 별개 스킬
        // (이전 14020000→14340000 묶음은 dev skillChains.ts/CSV의 오류였음 — 사용자 정정 2026-06-12).
        (14020000, new[] { 14030000, 14040000 }),
        (15210000, new[] { 15040000, 15050000 }),
        (15010000, new[] { 15030000, 15250000 }),
        (16010000, new[] { 16040000, 16020000, 16030000 }),
        (17010000, new[] { 17020000, 17030000 }),
        (18010000, new[] { 18020000, 18030000 }),
    };

    private sealed class Raw
    {
        public int Code;
        public string Name = string.Empty;
        public int Hits;
        public long Damage;
        public bool IsDot;
        public int Crit;
        public int Strong;
        public int Perfect;
        public int Back;
        public int Parry;
    }

    public static DetailModel Compute(
        IReadOnlyDictionary<string, AnalyzedSkill> skills,
        IReadOnlyList<OperatingData> ownBuffs,
        IReadOnlyList<OperatingData> bossDebuffs,
        int uid,
        JobClass? job,
        double contribution,
        long combatMs,
        Func<int, string?> actorName)
    {
        var raws = new List<Raw>();
        foreach (KeyValuePair<string, AnalyzedSkill> entry in skills)
        {
            AnalyzedSkill s = entry.Value;
            if (s.DamageAmount <= 0)
            {
                continue;
            }

            int code = int.TryParse(entry.Key, out int parsed) ? parsed : s.SkillCode;
            string name = string.IsNullOrWhiteSpace(s.Name) ? code.ToString() : s.Name!;

            raws.Add(new Raw
            {
                Code = code,
                Name = name,
                Hits = s.Times,
                Damage = s.DamageAmount,
                IsDot = false,
                Crit = s.CritTimes,
                Strong = s.DoubleTimes,
                Perfect = s.PerfectTimes,
                Back = s.BackTimes,
                Parry = s.ParryTimes,
            });

            if (s.DotDamageAmount > 0)
            {
                raws.Add(new Raw { Code = code, Name = name + " - 지속", Hits = s.DotTimes, Damage = s.DotDamageAmount, IsDot = true });
            }
        }

        // totals: pcts over non-DOT hits; total damage includes DOT.
        long totalDamage = raws.Sum(r => r.Damage);
        int totalHits = raws.Where(r => !r.IsDot).Sum(r => r.Hits);
        double TotalPct(Func<Raw, int> sel) =>
            totalHits > 0 ? Math.Round((double)raws.Where(r => !r.IsDot).Sum(sel) / totalHits * 1000, MidpointRounding.AwayFromZero) / 10.0 : 0.0;

        raws.Sort((a, b) => b.Damage.CompareTo(a.Damage));
        IReadOnlyList<DetailSkillGroup> groups = BuildGroups(raws, totalDamage);

        return new DetailModel(
            totalDamage,
            contribution,
            TotalPct(r => r.Crit),
            TotalPct(r => r.Strong),
            TotalPct(r => r.Perfect),
            TotalPct(r => r.Back),
            TotalPct(r => r.Parry),
            combatMs,
            groups,
            BuildOwnBuffs(ownBuffs, uid, job),
            BuildDebuffs(bossDebuffs, actorName));
    }

    private static IReadOnlyList<DetailSkillGroup> BuildGroups(List<Raw> raws, long totalDamage)
    {
        var used = new bool[raws.Count];
        var groups = new List<DetailSkillGroup>();

        foreach ((int main, int[] children) in ChainGroups)
        {
            var ordered = new List<Raw>();
            foreach (int code in new[] { main }.Concat(children))
            {
                for (int i = 0; i < raws.Count; i++)
                {
                    if (!used[i] && Normalize(raws[i].Code) == code)
                    {
                        ordered.Add(raws[i]);
                        used[i] = true;
                    }
                }
            }

            if (ordered.Count >= 2)
            {
                Raw merged = Merge(ordered);
                groups.Add(new DetailSkillGroup(
                    ToRow(merged, totalDamage),
                    ordered.Select(r => ToRow(r, totalDamage)).ToList(),
                    HasChildren: true));
            }
            else
            {
                // <2 rows matched — release them back to singleton handling.
                foreach (Raw r in ordered)
                {
                    used[raws.IndexOf(r)] = false;
                }
            }
        }

        for (int i = 0; i < raws.Count; i++)
        {
            if (!used[i])
            {
                DetailSkillRow row = ToRow(raws[i], totalDamage);
                groups.Add(new DetailSkillGroup(row, new[] { row }, HasChildren: false));
            }
        }

        return groups.OrderByDescending(g => g.Merged.Damage).ToList();
    }

    private static Raw Merge(List<Raw> rows)
    {
        var m = new Raw { Code = rows[0].Code, Name = rows[0].Name };
        foreach (Raw r in rows)
        {
            m.Hits += r.Hits;
            m.Damage += r.Damage;
            m.Crit += r.Crit;
            m.Strong += r.Strong;
            m.Perfect += r.Perfect;
            m.Back += r.Back;
            m.Parry += r.Parry;
        }

        return m;
    }

    private static DetailSkillRow ToRow(Raw r, long totalDamage) => new(
        r.Code,
        r.Name,
        r.Hits,
        r.Damage,
        r.IsDot,
        r.IsDot ? null : PctInt(r.Crit, r.Hits),
        r.IsDot ? null : PctInt(r.Strong, r.Hits),
        r.IsDot ? null : PctInt(r.Perfect, r.Hits),
        r.IsDot ? null : PctInt(r.Back, r.Hits),
        r.IsDot ? null : PctInt(r.Parry, r.Hits),
        totalDamage > 0 ? r.Damage / (double)totalDamage : 0.0);

    private static IReadOnlyList<DetailBuffSection> BuildOwnBuffs(IReadOnlyList<OperatingData> buffs, int uid, JobClass? job)
    {
        int jobPrefix = job != null ? JobClassInfo.BasicSkillCode(job.Value) / 1_000_000 : -1;
        var mine = new List<DetailBuffRow>();
        var party = new List<DetailBuffRow>();
        var other = new List<DetailBuffRow>();

        foreach (OperatingData b in buffs)
        {
            DetailBuffRow row = ToBuffRow(b);
            if (b.ActorId != uid)
            {
                party.Add(row);
            }
            else if (b.Code / 10_000_000 == jobPrefix)
            {
                mine.Add(row);
            }
            else
            {
                other.Add(row);
            }
        }

        var sections = new List<DetailBuffSection>();
        AddSection(sections, "내 버프", mine);
        AddSection(sections, "파티원 버프", party);
        AddSection(sections, "그 외", other);
        return sections;
    }

    private static IReadOnlyList<DetailBuffSection> BuildDebuffs(IReadOnlyList<OperatingData> debuffs, Func<int, string?> actorName)
    {
        return debuffs
            .GroupBy(d => d.ActorId)
            .Select(g => new DetailBuffSection(
                actorName(g.Key) is { Length: > 0 } n ? n : $"플레이어 {g.Key}",
                g.Select(ToBuffRow).OrderByDescending(r => r.Rate).ToList()))
            .OrderByDescending(s => s.Rows.Count > 0 ? s.Rows[0].Rate : 0.0)
            .ToList();
    }

    private static void AddSection(List<DetailBuffSection> sections, string label, List<DetailBuffRow> rows)
    {
        if (rows.Count == 0)
        {
            return;
        }

        sections.Add(new DetailBuffSection(label, rows.OrderByDescending(r => r.Rate).ToList()));
    }

    private static DetailBuffRow ToBuffRow(OperatingData b) =>
        new(b.Code, b.Name, Math.Clamp(b.OperatingRate, 0.0, 100.0), ReadableBuffText(b.Effect, b.Summary));

    private static int Normalize(int code) =>
        code is >= 11_000_000 and <= 19_999_999 ? code / 10_000 * 10_000 : code;

    private static int PctInt(int num, int den) =>
        den > 0 ? (int)Math.Round((double)num / den * 100, MidpointRounding.AwayFromZero) : 0;

    // The datamined buff `effect` is the most informative (has the actual +N% values) but frequently
    // carries game markup — <desc_point>/<chat_combat> tags wrapping {abe:..}/{se:..} value references
    // that only the client can resolve. The `summary` is the human-readable version ("이동 속도 증가").
    // So: prefer a markup-free effect (keeps the numbers), else the clean summary (clear intent), else
    // the effect with markup stripped. Makes every buff/debuff tooltip readable — old and new alike.
    private static string ReadableBuffText(string? effect, string? summary)
    {
        if (!string.IsNullOrWhiteSpace(effect) && !HasMarkup(effect)) return effect.Trim();
        if (!string.IsNullOrWhiteSpace(summary) && !HasMarkup(summary)) return summary.Trim();
        return StripMarkup(effect ?? summary);
    }

    private static bool HasMarkup(string s) => s.Contains('<') || s.Contains('{');

    private static string StripMarkup(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        string s = System.Text.RegularExpressions.Regex.Replace(text, "<[^>]*>", "");       // <desc_point>, <chat_combat>, </>
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\{[^}]*\}\s*%?", "");          // {abe:..}/{se:..} value refs (+ trailing %)
        s = System.Text.RegularExpressions.Regex.Replace(s, "[ \t]{2,}", " ");
        s = System.Text.RegularExpressions.Regex.Replace(s, " +(?=[,.\n])", "");             // stray space before punctuation
        return s.Trim();
    }
}

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
    int HitCount,
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
        public int Flagged; // direct hits that carried a special-flag region (sw6) — the back/강타/완벽/페리 denominator
    }

    public static DetailModel Compute(
        IReadOnlyDictionary<string, AnalyzedSkill> skills,
        IReadOnlyList<OperatingData> ownBuffs,
        IReadOnlyList<OperatingData> bossDebuffs,
        int uid,
        JobClass? job,
        double contribution,
        long combatMs)
    {
        var raws = new List<Raw>();
        foreach (KeyValuePair<string, AnalyzedSkill> entry in skills)
        {
            AnalyzedSkill s = entry.Value;
            // Keep a skill that dealt EITHER direct or DoT damage. A DoT-ONLY skill (DamageAmount 0,
            // DotDamageAmount > 0) is real: 대지의 징벌's DoT normalizes to a different rank code (…40) than its
            // direct hits (…50/57 → base …00), so its DoT arrives as its own DoT-only AnalyzedSkill. The old
            // `DamageAmount <= 0` skip dropped it, which is why the 대지의 징벌 지속피해 row was missing.
            if (s.DamageAmount <= 0 && s.DotDamageAmount <= 0)
            {
                continue;
            }

            int code = int.TryParse(entry.Key, out int parsed) ? parsed : s.SkillCode;
            string name = string.IsNullOrWhiteSpace(s.Name) ? code.ToString() : s.Name!;

            if (s.DamageAmount > 0)
            {
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
                    Flagged = s.FlaggedTimes,
                });
            }

            if (s.DotDamageAmount > 0)
            {
                raws.Add(new Raw { Code = code, Name = name + " - 지속", Hits = s.DotTimes, Damage = s.DotDamageAmount, IsDot = true });
            }
        }

        // totals: total damage includes DOT. Crit is a per-hit field present on EVERY hit, so its rate is over
        // all non-DOT hits. Back/강타(double)/완벽(perfect)/페리(parry) come from the special-flag byte, which
        // exists ONLY on flag-bearing (sw6) hits — non-directional hits (heals/buffs/passives, sw4) have no
        // flag byte, so those judgments are unmeasurable on them and must NOT dilute the denominator.
        long totalDamage = raws.Sum(r => r.Damage);
        int totalHits = raws.Where(r => !r.IsDot).Sum(r => r.Hits);
        int totalFlagged = raws.Where(r => !r.IsDot).Sum(r => r.Flagged);
        double TotalPct(Func<Raw, int> sel, int den) =>
            den > 0 ? Math.Round((double)raws.Where(r => !r.IsDot).Sum(sel) / den * 1000, MidpointRounding.AwayFromZero) / 10.0 : 0.0;

        raws.Sort((a, b) => b.Damage.CompareTo(a.Damage));
        IReadOnlyList<DetailSkillGroup> groups = BuildGroups(raws, totalDamage);

        return new DetailModel(
            totalDamage,
            contribution,
            TotalPct(r => r.Crit, totalHits),
            TotalPct(r => r.Strong, totalFlagged),
            TotalPct(r => r.Perfect, totalFlagged),
            TotalPct(r => r.Back, totalFlagged),
            TotalPct(r => r.Parry, totalFlagged),
            totalHits,
            combatMs,
            groups,
            BuildOwnBuffs(ownBuffs, uid, job),
            BuildDebuffs(bossDebuffs, uid));
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

        // Merge remaining rows that resolve to the SAME display name. The game splits some skills into two
        // damage codes — e.g. 심판의 번개's direct cast (17041250) and its field ticks (17040250) — that both
        // resolve to 심판의 번개 yet don't share a normalized code, so they showed as two rows. Merge by NAME,
        // NOT by base code, so two DISTINCT skills that share a slot (대지의 징벌 vs 대지의 축복, both 17_40) stay
        // separate. DoT ("- 지속") rows carry their own name, so they remain their own rows (as before).
        var remaining = new List<Raw>();
        for (int i = 0; i < raws.Count; i++)
        {
            if (!used[i])
            {
                remaining.Add(raws[i]);
            }
        }

        foreach (IGrouping<string, Raw> byName in remaining.Where(r => !r.IsDot).GroupBy(r => r.Name))
        {
            List<Raw> sameName = byName.ToList();
            if (sameName.Count >= 2)
            {
                Raw merged = Merge(sameName);
                groups.Add(new DetailSkillGroup(
                    ToRow(merged, totalDamage),
                    sameName.Select(r => ToRow(r, totalDamage)).ToList(),
                    HasChildren: true));
            }
            else
            {
                DetailSkillRow row = ToRow(sameName[0], totalDamage);
                groups.Add(new DetailSkillGroup(row, new[] { row }, HasChildren: false));
            }
        }

        foreach (Raw dot in remaining.Where(r => r.IsDot))
        {
            DetailSkillRow row = ToRow(dot, totalDamage);
            groups.Add(new DetailSkillGroup(row, new[] { row }, HasChildren: false));
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
            m.Flagged += r.Flagged;
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

    /// <summary>
    /// The buffs this player put up themselves — their own class buffs, then consumables (scrolls/potions).
    /// Buffs another player cast on them are excluded: the window answers "what did I keep running?", and a
    /// chanter's 진언 sitting at 90% on everyone tells the reader nothing about this player.
    /// </summary>
    private static IReadOnlyList<DetailBuffSection> BuildOwnBuffs(IReadOnlyList<OperatingData> buffs, int uid, JobClass? job)
    {
        int jobPrefix = job != null ? JobClassInfo.BasicSkillCode(job.Value) / 1_000_000 : -1;
        var mine = new List<DetailBuffRow>();
        var other = new List<DetailBuffRow>();

        // Every row here landed on this player, so every row is a buff or a self-state — a player skill's debuff
        // goes on its target, never on its caster. Debuffs live in the boss's list (BuildDebuffs).
        foreach (OperatingData b in buffs)
        {
            if (b.ActorId != uid)
            {
                continue;
            }

            DetailBuffRow row = ToBuffRow(b);
            if (jobPrefix > 0 && b.EffectiveJobPrefix == jobPrefix)
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
        AddSection(sections, "그 외", other);
        return sections;
    }

    /// <summary>The boss debuffs THIS player applied. One unlabelled section — every row has the same caster.</summary>
    private static IReadOnlyList<DetailBuffSection> BuildDebuffs(IReadOnlyList<OperatingData> debuffs, int uid)
    {
        var rows = debuffs
            .Where(d => d.ActorId == uid)
            .Select(ToBuffRow)
            .OrderByDescending(r => r.Rate)
            .ToList();

        var sections = new List<DetailBuffSection>();
        AddSection(sections, string.Empty, rows);
        return sections;
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

using WaffleMeter.Capture;

namespace WaffleMeter.Data;

/// <summary>Confidence of the source that set <see cref="User.Job"/>. A higher source overrides a lower
/// one and then can't be flipped back by a lower one; within a tier the FIRST write wins. OwnSkill ranks
/// ABOVE Authoritative on purpose: in AION2 damage skills are job-locked, so a player's OWN un-folded
/// damage packet is direct, live proof of their job — more reliable than the fragile byte-after-nickname
/// jobByte parse OR a short-name official lookup that can resolve a DIFFERENT same-name character. (Summon-
/// folded foreign skills never reach OwnSkill: the caller gates on actor == packet.ActorId.)</summary>
public enum JobProvenance
{
    None = 0,
    // The snapshot jobByte (ConvertFromCode) and the official pcId lookup — both external, both first-write-
    // wins relative to each other (the live snapshot byte is not overwritten by a later name lookup).
    Authoritative = 1,
    OwnSkill = 2, // the player's own un-folded job-locked damage skill — live ground truth, corrects the above
}

/// <summary>Player. Verbatim port of Kotlin <c>entity/User.kt</c>; equality/hash by id only
/// (so a contributor set is keyed by id).</summary>
public sealed class User
{
    public int Id { get; }
    public string? Nickname { get; set; }
    public int Server { get; set; }
    public JobClass? Job { get; set; }

    /// <summary>Confidence of the source that last set <see cref="Job"/>; see <see cref="JobProvenance"/>.</summary>
    public JobProvenance JobSource { get; set; }

    /// <summary>True once <see cref="Job"/> came from something stronger than a missing value — kept as a
    /// computed alias so existing callers read the same intent (the job shouldn't be re-filled blindly).</summary>
    public bool JobAuthoritative => JobSource >= JobProvenance.Authoritative;

    /// <summary>Set <see cref="Job"/> only if <paramref name="source"/> is STRICTLY higher-confidence than
    /// the current source (so equal/lower sources can't overwrite — first-write-wins within a tier, and a
    /// player's own job-locked skill (OwnSkill) corrects a wrong jobByte/official label). Returns whether it
    /// changed. Null jobs are ignored (a neutral/unknown skill code never clears a known job).</summary>
    public bool TrySetJob(JobClass? job, JobProvenance source)
    {
        if (job == null || source <= JobSource)
        {
            return false;
        }

        Job = job;
        JobSource = source;
        return true;
    }

    public bool IsExecutor { get; set; }
    public int Power { get; set; }

    public User(int id, string? nickname = null, int server = -1, JobClass? job = null, bool isExecutor = false, int power = 0)
    {
        Id = id;
        Nickname = nickname;
        Server = server;
        Job = job;
        IsExecutor = isExecutor;
        Power = power;
    }

    public override int GetHashCode() => Id;
    public override bool Equals(object? obj) => obj is User u && u.Id == Id;

    /// <summary>Kotlin data-class <c>copy()</c> — a mutable snapshot (used by the stats builder).</summary>
    public User Copy() => new(Id, Nickname, Server, Job, IsExecutor, Power) { JobSource = JobSource };
}

/// <summary>Per-player aggregate (Kotlin DpsInformation). Doubles, mutable.</summary>
public sealed class DpsInformation
{
    public double Amount { get; set; }
    public double Dps { get; set; }
    public double Contribution { get; set; }
    public double EntireContribution { get; set; }

    public DpsInformation() { }

    public DpsInformation(double amount, double dps, double contribution, double entireContribution)
    {
        Amount = amount;
        Dps = dps;
        Contribution = contribution;
        EntireContribution = entireContribution;
    }

    public void AddDamage(double damage) => Amount += damage;
}

/// <summary>Per-skill breakdown (Kotlin AnalyzedSkill). SkillCode is @Transient (not serialized).</summary>
public sealed class AnalyzedSkill
{
    public int SkillCode { get; init; }

    /// <summary>A representative FULL wire skill code (pre-normalization) for hits of this skill — its last
    /// four decimal digits carry the caster's specialization (특화), decoded by
    /// <c>SkillSpecialization.Decode</c>. Constant per actor per battle (a player does not respec mid-fight),
    /// so the last-seen raw code is representative. 0 when never set (e.g. rebuilt from an old snapshot).</summary>
    public int RawSkillCode { get; set; }

    public int DamageAmount { get; set; }
    public int DotDamageAmount { get; set; }
    public int DotTimes { get; set; }
    public int CritTimes { get; set; }
    public int Times { get; set; }
    public int BackTimes { get; set; }

    /// <summary>Front attacks (post-2026-07-01 position byte == 2). Mirror of <see cref="BackTimes"/>.</summary>
    public int FrontTimes { get; set; }

    public int PerfectTimes { get; set; }
    public int DoubleTimes { get; set; }
    public int ParryTimes { get; set; }
    public int ShardTimes { get; set; }
    public int MultiHitTimes { get; set; }

    /// <summary>Direct hits that carried a special-flag region (switch-type 6, region size ≥ 10). Only these
    /// hits can encode a back/강타/완벽/페리 판정 — switch-type-4 hits (heals/buffs/passives) have no flag byte,
    /// so back etc. are structurally unmeasurable on them. Used as the denominator for those rates so
    /// non-directional hits don't dilute them (crit stays over <see cref="Times"/> — it's a separate field
    /// present on every hit).</summary>
    public int FlaggedTimes { get; set; }
    public string? Name { get; set; }

    public AnalyzedSkill Copy() => new()
    {
        SkillCode = SkillCode,
        RawSkillCode = RawSkillCode,
        DamageAmount = DamageAmount,
        DotDamageAmount = DotDamageAmount,
        DotTimes = DotTimes,
        CritTimes = CritTimes,
        Times = Times,
        BackTimes = BackTimes,
        FrontTimes = FrontTimes,
        PerfectTimes = PerfectTimes,
        DoubleTimes = DoubleTimes,
        ParryTimes = ParryTimes,
        ShardTimes = ShardTimes,
        MultiHitTimes = MultiHitTimes,
        FlaggedTimes = FlaggedTimes,
        Name = Name,
    };
}

/// <summary>
/// Buff/debuff uptime entry (Kotlin OperatingData). One row per (base skill, name, caster) on one entity:
/// the rank/aspect variants a skill emits are merged upstream, so <see cref="OperatingRate"/> is the union of
/// every variant's applied intervals.
/// </summary>
/// <remarks>
/// Whether a row is a buff or a debuff is decided by WHO it landed on, not by buff.json's <c>type</c> field.
/// A player skill's debuff always goes on its target; nothing a player casts debuffs the caster. So a row in a
/// participant's list is a buff/self-state and a row in the boss's list is a debuff — and the datamined type is
/// not consulted, because it is demonstrably wrong (권성 폭주's codes are half-typed DeBuff while their text is a
/// pure buff, and 검성 살기 파열's DeBuff-typed codes land on the caster 100% of the time).
/// </remarks>
/// <param name="Code">A representative runtime code, preferred from buff.json so icon lookups resolve.</param>
/// <param name="BaseCode">The 8-digit base skill code the group collapsed to (see DataManager.BuffBaseCode).</param>
/// <param name="JobPrefix">
/// 11(검성)..19(권성) when the RAW runtime code sat in the 9-digit job-buff band, else 0. Derived from the raw
/// code — not from <see cref="Code"/> or <see cref="BaseCode"/>, because 8-digit mob/consumable codes like
/// 12000101(중독) would otherwise read as 수호성 and be mistaken for a self-buff.
/// </param>
public sealed record OperatingData(
    int Code,
    string Name,
    string? Summary,
    string? Effect,
    double OperatingRate,
    int ActorId,
    int BaseCode = 0,
    int JobPrefix = 0)
{
    public OperatingData(int code, Buff? buff, double rate, int actorId)
        : this(code, buff?.Name ?? code.ToString(), buff?.Summary, buff?.Effect, rate, actorId) { }

    /// <summary>
    /// <see cref="JobPrefix"/>, or — for rows built without one — the prefix implied by a 9-digit job-buff
    /// <see cref="Code"/>. 0 means "not a job buff", which callers must read as "cannot be a self-buff".
    /// </summary>
    public int EffectiveJobPrefix => JobPrefix != 0
        ? JobPrefix
        : Code is >= 110_000_000 and <= 199_999_999 ? Code / 10_000_000 : 0;
}

/// <summary>Target/boss info (Kotlin MobInfo).</summary>
public sealed class MobInfo
{
    public int Id { get; }
    public Mob Mob { get; }
    public int RemainHp { get; set; }
    public int MaxHp { get; set; }

    public MobInfo(int id, Mob mob, int remainHp = 0, int maxHp = 0)
    {
        Id = id;
        Mob = mob;
        RemainHp = remainHp;
        MaxHp = maxHp;
    }

    public MobInfo Copy() => new(Id, Mob, RemainHp, MaxHp);
}

/// <summary>Skill catalog entry (Kotlin Skill).</summary>
public sealed record Skill(long Code, string? Name);

/// <summary>Buff catalog entry (Kotlin Buff). buff.json's <c>type</c> field is deliberately not loaded — see
/// <see cref="OperatingData"/> for why it can't be trusted.</summary>
public sealed record Buff(int Code, string Name, string Summary, string Effect);

/// <summary>An applied buff/debuff interval (Kotlin UseBuff).</summary>
public sealed record UseBuff(int SkillCode, long BuffStart, long BuffEnd, long Duration, int ActorId);

/// <summary>
/// One buff/debuff's merged applied intervals on a single entity, for the combat-detail DPS-graph timeline
/// lane. Same grouping as <see cref="OperatingData"/> (base skill + name + caster) but keeps the merged
/// <see cref="Spans"/> instead of collapsing them to a single rate — the graph draws a band per span and an
/// icon so you can read which buffs were up when the DPS spiked. <see cref="Code"/> is the representative
/// display code (feed it to the icon resolver). Spans are absolute capture-clock ms, already window-clamped
/// and unioned via <see cref="BuffUptime.MergeIntervals"/>.
/// </summary>
public sealed record BuffTimeline(
    int Code,
    string Name,
    int ActorId,
    int BaseCode,
    int JobPrefix,
    IReadOnlyList<(long Start, long End)> Spans)
{
    /// <summary><see cref="JobPrefix"/>, or the prefix implied by a 9-digit job-buff <see cref="Code"/> when the
    /// raw code carried none — mirrors <see cref="OperatingData.EffectiveJobPrefix"/> so the DPS graph's
    /// class-buff (내 버프) filter matches the buff-uptime tab. 0 = not a class buff (e.g. a consumable).</summary>
    public int EffectiveJobPrefix => JobPrefix != 0
        ? JobPrefix
        : Code is >= 110_000_000 and <= 199_999_999 ? Code / 10_000_000 : 0;
}

/// <summary>One raw packet kept for replay (Kotlin RawPacket).</summary>
public sealed record RawPacket(byte[] Data, long Timestamp);

/// <summary>
/// A DPS report for one battle (Kotlin DpsReport). Contributors keep insertion order (the Kotlin
/// MutableSet is a LinkedHashSet; the move-to-end-on-readd behavior is handled where it is mutated).
/// fakeTimeFlag and packets are @Transient (not serialized).
/// </summary>
public sealed class DpsReport
{
    public List<User> Contributors { get; set; } = [];
    public long BattleStart { get; set; }
    public long BattleEnd { get; set; }
    public Dictionary<int, DpsInformation> Information { get; set; } = new();
    public MobInfo? Target { get; set; }
    public bool FakeTimeFlag { get; set; }
    public List<ParsedDamagePacket>? Packets { get; set; }

    /// <summary>Frozen buff-uptime snapshot (uid -&gt; rates), populated when the battle is saved so the
    /// detail window shows the SAME rates the stats payload/web uses. The live buff repository is pruned
    /// after a battle is saved (<see cref="UseBuffRepository.PruneBefore"/>), so recomputing post-battle
    /// would under-count; this stays empty while the battle is in progress (the detail recomputes live
    /// against the intact repo then).</summary>
    public Dictionary<int, List<OperatingData>> BuffRates { get; set; } = new();

    /// <summary>Frozen boss-debuff-uptime snapshot, populated alongside <see cref="BuffRates"/>.</summary>
    public List<OperatingData> BossBuffRates { get; set; } = [];

    /// <summary>Frozen per-second damage series (uid -&gt; dense <c>long[]</c>, index = whole-second offset from
    /// <see cref="BattleStart"/>, value = damage dealt in that second), the source for the combat-detail DPS
    /// graph. Frozen at save time because a SAVED report carries <see cref="Packets"/>=null and the live
    /// per-second accumulator is cleared when the next battle starts — without this a history-replayed graph
    /// would be empty (same bug class as <see cref="SkillDetailsSnapshot"/>). Stays empty for the in-progress
    /// report, where the detail rebuilds the series live from <see cref="DpsCalculator.GetDpsSeries"/>.</summary>
    public Dictionary<int, long[]> DpsSeries { get; set; } = new();

    /// <summary>Frozen per-recipient buff/debuff timeline (uid -&gt; merged applied spans), the icon-lane source
    /// for the DPS graph. Populated alongside <see cref="BuffRates"/> and, like it, frozen BEFORE the live buff
    /// repository is pruned (<see cref="UseBuffRepository.PruneBefore"/>) so a post-battle or history-replayed
    /// graph keeps the intervals. Empty while the battle is in progress (the detail recomputes live via
    /// <see cref="DpsCalculator.GetBuffIntervals"/> against the intact repo).</summary>
    public Dictionary<int, List<BuffTimeline>> BuffIntervals { get; set; } = new();

    /// <summary>Frozen per-actor skill-breakdown snapshot (uid -&gt; skillCode -&gt; analyzed skill),
    /// populated when the battle is saved. A SAVED report carries <see cref="Packets"/>=null, so
    /// <see cref="DpsCalculator.BattleDetails"/> could otherwise only rebuild from packets and would return
    /// an EMPTY skill table for any history-replayed battle (which also zeroed 누적 피해량 + every hit-rate%,
    /// since the detail's summary derives from the skill rows). Preferred by BattleDetails when non-empty;
    /// stays empty for the in-progress/live report, where BattleDetails uses the live cache instead.</summary>
    public Dictionary<int, Dictionary<string, AnalyzedSkill>> SkillDetailsSnapshot { get; set; } = new();

    /// <summary>The 본인(executor) uid, frozen into a SAVED report at save time (like <see cref="BuffRates"/>
    /// and <see cref="SkillDetailsSnapshot"/>). A saved report's per-row <see cref="User.IsExecutor"/> is
    /// frozen by <c>DataManager.CopyUser</c> — usually <c>false</c>, since a battle is often saved before the
    /// own character is recognized — so nothing in the report itself would otherwise mark the local player,
    /// and a history replay's "내 캐릭터" color (직업 강조 mode) would leak back to the job color. This lets a
    /// saved battle self-identify its executor regardless of the LIVE recognition state at view time. 0 = a
    /// live/in-progress report (the overlay then uses the live recognized uid / per-row IsExecutor instead).</summary>
    public int ExecutorId { get; set; }

    /// <summary>True when the current target is a classified instanced (원정/초월/성역) boss. Set live in
    /// <c>DpsCalculator.GetDps</c>; scopes the opt-in "던전 강제 집계" display bypass to these bosses only.</summary>
    public bool TargetInstanced { get; set; }

    /// <summary>Frozen party/raid sub-group slots (uid -&gt; slot 1-8 from the 0x9702 roster), populated at
    /// save time like <see cref="ExecutorId"/>. Lets the stats upload tag each participant's sub-party for an
    /// 8-인 공대 — slots 1-4 = party 1, 5-8 = party 2; empty for a non-raid / unmatched battle. Frozen because
    /// the live roster is replaced on party change, so a delayed upload stays faithful to the battle.</summary>
    public Dictionary<int, int> PartySlots { get; set; } = new();

    public bool IsEmpty() => Information.Count == 0;

    public void CompareBattleTime(long time)
    {
        if (BattleStart == 0L)
        {
            BattleStart = time;
            FakeTimeFlag = true;
        }

        if (BattleStart > time && FakeTimeFlag)
        {
            BattleStart = time;
        }

        if (BattleEnd < time)
        {
            BattleEnd = time;
        }
    }
}

/// <summary>A saved battle log (Kotlin DpsLog).</summary>
public sealed class DpsLog
{
    public required DpsReport Report { get; init; }
    public Dictionary<int, int> SummonMap { get; init; } = new();
    public List<RawPacket> Packets { get; init; } = [];
    public Dictionary<int, Dictionary<string, AnalyzedSkill>> SkillDetails { get; init; } = new();
    public Dictionary<int, List<OperatingData>> BuffRates { get; init; } = new();
    public List<OperatingData> BossBuffRates { get; init; } = [];
}

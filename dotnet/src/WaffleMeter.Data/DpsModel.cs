using WaffleMeter.Capture;

namespace WaffleMeter.Data;

/// <summary>Player. Verbatim port of Kotlin <c>entity/User.kt</c>; equality/hash by id only
/// (so a contributor set is keyed by id).</summary>
public sealed class User
{
    public int Id { get; }
    public string? Nickname { get; set; }
    public int Server { get; set; }
    public JobClass? Job { get; set; }

    /// <summary>True once <see cref="Job"/> came from an AUTHORITATIVE source (packet jobByte via
    /// ConvertFromCode, or the official-API pcId) rather than skill-code inference. An authoritative job
    /// overrides an inferred one and then locks, so an early wrong inference (e.g. a summon-folded foreign
    /// skill code) is corrected when the real byte arrives and can't be flipped back.</summary>
    public bool JobAuthoritative { get; set; }

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
    public User Copy() => new(Id, Nickname, Server, Job, IsExecutor, Power) { JobAuthoritative = JobAuthoritative };
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
    public int DamageAmount { get; set; }
    public int DotDamageAmount { get; set; }
    public int DotTimes { get; set; }
    public int CritTimes { get; set; }
    public int Times { get; set; }
    public int BackTimes { get; set; }
    public int PerfectTimes { get; set; }
    public int DoubleTimes { get; set; }
    public int ParryTimes { get; set; }
    public int ShardTimes { get; set; }
    public int MultiHitTimes { get; set; }
    public string? Name { get; set; }

    public AnalyzedSkill Copy() => new()
    {
        SkillCode = SkillCode,
        DamageAmount = DamageAmount,
        DotDamageAmount = DotDamageAmount,
        DotTimes = DotTimes,
        CritTimes = CritTimes,
        Times = Times,
        BackTimes = BackTimes,
        PerfectTimes = PerfectTimes,
        DoubleTimes = DoubleTimes,
        ParryTimes = ParryTimes,
        ShardTimes = ShardTimes,
        MultiHitTimes = MultiHitTimes,
        Name = Name,
    };
}

/// <summary>Buff uptime entry (Kotlin OperatingData).</summary>
public sealed record OperatingData(int Code, string Name, string? Summary, string? Effect, double OperatingRate, int ActorId)
{
    public OperatingData(int code, Buff? buff, double rate, int actorId)
        : this(code, buff?.Name ?? code.ToString(), buff?.Summary, buff?.Effect, rate, actorId) { }
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

/// <summary>Buff catalog entry (Kotlin Buff).</summary>
public sealed record Buff(int Code, string Name, string Summary, string Effect);

/// <summary>An applied buff/debuff interval (Kotlin UseBuff).</summary>
public sealed record UseBuff(int SkillCode, long BuffStart, long BuffEnd, long Duration, int ActorId);

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

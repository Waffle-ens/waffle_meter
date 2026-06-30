using System.Globalization;
using WaffleMeter.Capture;
using WaffleMeter.Data;

namespace WaffleMeter.Stats;

/// <summary>Result of building an upload payload: either a payload or a skip reason.</summary>
public abstract record BuildResult
{
    private BuildResult()
    {
    }

    public sealed record Payload(StatsUploadPayload Value) : BuildResult;

    public sealed record Skip(string Reason) : BuildResult;
}

/// <summary>
/// Verbatim port of Kotlin <c>stats.StatsPayloadBuilder</c>: turns a finished <see cref="DpsLog"/>
/// into the anonymized upload payload (own character + participants + skills + buffs/debuffs), or a
/// skip reason. Resolves each contributor's combat power (live snapshot -&gt; name/server match -&gt;
/// official lookup) and tags buffs with self/other/party source. The "public" flag and clock are
/// injected to break the cycle with the consent manager and keep it testable.
/// </summary>
public sealed class StatsPayloadBuilder
{
    private static readonly HashSet<JobClass> SynergyJobs = new()
    {
        JobClass.TEMPLAR, JobClass.GLADIATOR, JobClass.CHANTER, JobClass.CLERIC,
    };

    private readonly DataManager _data;
    private readonly Func<bool> _publicCharacter;
    private readonly Func<long> _clock;

    public StatsPayloadBuilder(DataManager data, Func<bool> publicCharacterProvider, Func<long>? clock = null)
    {
        _data = data;
        _publicCharacter = publicCharacterProvider;
        _clock = clock ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    public StatsOwnCharacter OwnCharacter()
    {
        int id = _data.ExecutorId();
        User? user = _data.User(id);
        if (user == null)
        {
            return new StatsOwnCharacter(false);
        }

        User resolved = ResolveUserSnapshot(user);
        return new StatsOwnCharacter(
            Detected: !string.IsNullOrWhiteSpace(resolved.Nickname),
            Id: resolved.Id,
            Nickname: resolved.Nickname,
            Server: resolved.Server,
            Job: resolved.Job?.ClassName(),
            Power: resolved.Power);
    }

    public BuildResult Build(DpsLog log, string clientVersion, bool killConfirmed)
    {
        DpsReport report = log.Report;
        MobInfo? target = report.Target;
        if (target == null)
        {
            return new BuildResult.Skip("target_missing");
        }

        Mob mob = target.Mob;
        if (!mob.Boss || mob.IsDummy)
        {
            return new BuildResult.Skip("not_uploadable_boss");
        }

        if (!killConfirmed)
        {
            return new BuildResult.Skip("not_kill");
        }

        int ownId = _data.ExecutorId();
        if (ownId == 0)
        {
            return new BuildResult.Skip("executor_missing");
        }

        User? own = report.Contributors.FirstOrDefault(u => u.Id == ownId) ?? _data.User(ownId);
        if (own == null)
        {
            return new BuildResult.Skip("own_character_missing");
        }

        string? ownNickname = NonBlank(own.Nickname);
        if (ownNickname == null)
        {
            return new BuildResult.Skip("own_nickname_missing");
        }

        if (!report.Information.ContainsKey(own.Id))
        {
            return new BuildResult.Skip("own_result_missing");
        }

        DpsInformation ownInfo = report.Information[own.Id];
        if (ownInfo.Amount <= 0.0)
        {
            return new BuildResult.Skip("own_damage_empty");
        }

        long duration = report.BattleEnd - report.BattleStart;
        if (duration <= 0)
        {
            return new BuildResult.Skip("invalid_duration");
        }

        List<User> contributors = ResolveContributors(report.Contributors);
        User resolvedOwn = contributors.FirstOrDefault(u => u.Id == own.Id) ?? own;

        var identityHashCache = new Dictionary<int, string?>();
        string? ActorIdentity(int actorId)
        {
            if (identityHashCache.TryGetValue(actorId, out string? cached))
            {
                return cached;
            }

            User? actor = contributors.FirstOrDefault(u => u.Id == actorId) ?? _data.User(actorId);
            string? nickname = NonBlank(actor?.Nickname);
            string? resolved = actor != null && nickname != null
                ? StatsIdentity.CharacterIdentityHash(actor.Server, nickname)
                : null;
            identityHashCache[actorId] = resolved;
            return resolved;
        }

        long totalDamage = RoundToLong(ownInfo.Amount);
        Dictionary<string, AnalyzedSkill> ownSkills = log.SkillDetails.GetValueOrDefault(own.Id) ?? new Dictionary<string, AnalyzedSkill>();
        List<StatsSkillPayload> skillPayloads = BuildSkillPayloads(ownSkills, totalDamage);
        RateSummary resultRates = SummarizeRates(ownSkills.Values);
        List<User> participantUsers = SortedParticipantUsers(log, contributors);

        var participantIndexById = new Dictionary<int, int>();
        for (int i = 0; i < participantUsers.Count; i++)
        {
            participantIndexById[participantUsers[i].Id] = i;
        }

        List<StatsParticipantPayload> participantPayloads = BuildParticipantPayloads(log, own.Id, participantUsers, participantIndexById, ActorIdentity);
        if (resolvedOwn.Power <= 0)
        {
            return new BuildResult.Skip("own_power_unresolved");
        }

        if (participantPayloads.Any(p => p.Power <= 0))
        {
            return new BuildResult.Skip("participant_power_unresolved");
        }

        string? ownIdentityHash = StatsIdentity.CharacterIdentityHash(own.Server, ownNickname);
        if (ownIdentityHash == null)
        {
            return new BuildResult.Skip("own_identity_missing");
        }

        Dictionary<string, int> jobCounts = contributors
            .Where(u => u.Job != null)
            .Select(u => u.Job!.Value.ClassName())
            .GroupBy(name => name)
            .ToDictionary(g => g.Key, g => g.Count());
        StatsSynergyPayload synergy = BuildSynergy(contributors);
        string battleHash = BattleHash(own.Server, ownNickname, mob.Code, report.BattleStart, report.BattleEnd, totalDamage, duration);

        var payload = new StatsUploadPayload(
            SchemaVersion: 4,
            ClientVersion: clientVersion,
            BattleHash: battleHash,
            IdentityHashVersion: StatsIdentity.IdentityHashVersion,
            ConsentVersion: StatsConsentManager.ConsentVersion,
            UploadedAt: _clock(),
            // character.public is informational only: the server IGNORES it on /reports (§2.3, fail-closed) —
            // an upload never makes a character public. Going public happens solely through the consent accept
            // path with a valid grant (§2.4). We send the current local flag for parity but never rely on it.
            Character: new StatsCharacterPayload(
                ownIdentityHash,
                ownNickname,
                own.Server,
                resolvedOwn.Job?.ClassName(),
                resolvedOwn.Power,
                _publicCharacter()),
            Encounter: new StatsEncounterPayload(mob.Code, mob.Name),
            Battle: new StatsBattlePayload(report.BattleStart, report.BattleEnd, duration, report.Contributors.Count),
            PartyComposition: new StatsPartyCompositionPayload(jobCounts, synergy),
            Participants: participantPayloads,
            Result: BuildResultPayload(ownInfo, resultRates),
            Skills: skillPayloads,
            Buffs: (log.BuffRates.GetValueOrDefault(own.Id) ?? new List<OperatingData>())
                .Select(v => ToBuffPayload(
                    v, "participant", "buff", own.Id, resolvedOwn.Job,
                    IndexOrNull(participantIndexById, own.Id),
                    IndexOrNull(participantIndexById, v.ActorId),
                    ActorIdentity(v.ActorId)))
                .ToList(),
            BossDebuffs: log.BossBuffRates
                .Select(v => ToBuffPayload(
                    v, "boss", "debuff", null, null, null,
                    IndexOrNull(participantIndexById, v.ActorId),
                    ActorIdentity(v.ActorId)))
                .ToList());

        return new BuildResult.Payload(payload);
    }

    private List<User> SortedParticipantUsers(DpsLog log, IEnumerable<User> contributors)
    {
        return contributors
            .Where(u => AmountOf(log, u.Id) > 0.0)
            .OrderByDescending(u => AmountOf(log, u.Id))
            .ToList();
    }

    private List<StatsParticipantPayload> BuildParticipantPayloads(
        DpsLog log,
        int ownId,
        List<User> participants,
        Dictionary<int, int> participantIndexById,
        Func<int, string?> actorIdentity)
    {
        var result = new List<StatsParticipantPayload>();
        // 8-인 공대: a slot 5-8 means a 2nd party exists. Tag each participant's sub-party (slots 1-4 → party 1
        // = uploader's party, 5-8 → party 2); for a non-raid roster the tags stay null and are omitted on send.
        bool isRaid = log.Report.PartySlots.Values.Any(s => s > 4);
        foreach (User user in participants)
        {
            if (!log.Report.Information.TryGetValue(user.Id, out DpsInformation? info))
            {
                continue;
            }

            long totalDamage = RoundToLong(info.Amount);
            int? partySlot = isRaid && log.Report.PartySlots.TryGetValue(user.Id, out int slot) ? slot : null;
            int? partyNumber = partySlot is int s ? (s - 1) / 4 + 1 : null;
            Dictionary<string, AnalyzedSkill> skills = log.SkillDetails.GetValueOrDefault(user.Id) ?? new Dictionary<string, AnalyzedSkill>();
            RateSummary rates = SummarizeRates(skills.Values);
            string? identityHash = NonBlank(user.Nickname) is { } nickname
                ? StatsIdentity.CharacterIdentityHash(user.Server, nickname)
                : null;

            result.Add(new StatsParticipantPayload(
                IdentityHash: identityHash,
                IsUploader: user.Id == ownId,
                PartyNumber: partyNumber,
                PartySlot: partySlot,
                Job: user.Job?.ClassName(),
                Power: user.Power,
                Result: BuildResultPayload(info, rates),
                Skills: BuildSkillPayloads(skills, totalDamage),
                Buffs: (log.BuffRates.GetValueOrDefault(user.Id) ?? new List<OperatingData>())
                    .Select(v => ToBuffPayload(
                        v, "participant", "buff", user.Id, user.Job,
                        IndexOrNull(participantIndexById, user.Id),
                        IndexOrNull(participantIndexById, v.ActorId),
                        actorIdentity(v.ActorId)))
                    .ToList()));
        }

        return result;
    }

    private List<User> ResolveContributors(IEnumerable<User> contributors) =>
        contributors.Select(ResolveUserSnapshot).ToList();

    private User ResolveUserSnapshot(User user)
    {
        User resolved = user.Copy();
        MergeUserInfo(resolved, _data.User(user.Id));
        string? nickname = NonBlank(resolved.Nickname);
        int server = resolved.Server;

        if (resolved.Power <= 0 && nickname != null && server > 0)
        {
            MergeUserInfo(resolved, _data.FindUserByNicknameAndServer(nickname, server));
        }

        if (resolved.Power <= 0 && nickname != null && server > 0)
        {
            OfficialCharacterInfo? info = _data.ResolveOfficialCharacterInfo(resolved.Id, nickname, server, resolved.Job);
            if (info != null)
            {
                if (string.IsNullOrWhiteSpace(resolved.Nickname))
                {
                    resolved.Nickname = info.Nickname;
                }

                if (resolved.Server <= 0)
                {
                    resolved.Server = info.Server;
                }

                if (resolved.Job == null && info.Job != null)
                {
                    resolved.Job = info.Job;
                }

                if (info.Power > 0)
                {
                    resolved.Power = info.Power;
                }
            }
        }

        return resolved;
    }

    private static void MergeUserInfo(User target, User? source)
    {
        if (source == null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(target.Nickname) && !string.IsNullOrWhiteSpace(source.Nickname))
        {
            target.Nickname = source.Nickname;
        }

        if (target.Server <= 0 && source.Server > 0)
        {
            target.Server = source.Server;
        }

        if (target.Job == null && source.Job != null)
        {
            target.Job = source.Job;
        }

        if (target.Power <= 0 && source.Power > 0)
        {
            target.Power = source.Power;
        }
    }

    private static StatsSynergyPayload BuildSynergy(IEnumerable<User> contributors)
    {
        HashSet<JobClass> jobs = contributors.Where(u => u.Job != null).Select(u => u.Job!.Value).ToHashSet();
        int count = Math.Min(jobs.Count(j => SynergyJobs.Contains(j)), 3);
        return new StatsSynergyPayload(
            HasGuardian: jobs.Contains(JobClass.TEMPLAR),
            HasGladiator: jobs.Contains(JobClass.GLADIATOR),
            HasChanter: jobs.Contains(JobClass.CHANTER),
            HasCleric: jobs.Contains(JobClass.CLERIC),
            SynergyCount: count);
    }

    private List<StatsSkillPayload> BuildSkillPayloads(Dictionary<string, AnalyzedSkill> skills, long totalDamage)
    {
        double Share(long amount) => totalDamage > 0 ? OneDecimal((double)amount / totalDamage * 100.0) : 0.0;

        var entries = new List<StatsSkillPayload>();
        foreach (KeyValuePair<string, AnalyzedSkill> entry in skills)
        {
            if (!int.TryParse(entry.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out int code))
            {
                continue;
            }

            AnalyzedSkill skill = entry.Value;
            string name = NonBlank(skill.Name) ?? code.ToString(CultureInfo.InvariantCulture);

            long directDamage = Math.Max((long)skill.DamageAmount, 0);
            if (directDamage > 0)
            {
                RateSummary rate = SummarizeRates(new[] { skill });
                entries.Add(new StatsSkillPayload(
                    SkillCode: code,
                    SkillName: name,
                    DamageType: "direct",
                    Damage: directDamage,
                    HitCount: skill.Times,
                    CritRate: rate.CritRate,
                    StrongRate: rate.StrongRate,
                    PerfectRate: rate.PerfectRate,
                    Share: Share(directDamage)));
            }

            long dotDamage = Math.Max((long)skill.DotDamageAmount, 0);
            if (dotDamage > 0)
            {
                entries.Add(new StatsSkillPayload(
                    SkillCode: code,
                    SkillName: name,
                    DamageType: "dot",
                    Damage: dotDamage,
                    HitCount: skill.DotTimes,
                    CritRate: 0.0,
                    StrongRate: 0.0,
                    PerfectRate: 0.0,
                    Share: Share(dotDamage)));
            }
        }

        return entries.OrderByDescending(e => e.Damage).ToList();
    }

    private static StatsResultPayload BuildResultPayload(DpsInformation info, RateSummary rates) => new(
        TotalDamage: RoundToLong(info.Amount),
        Dps: RoundToLong(info.Dps),
        PartyContribution: OneDecimal(info.Contribution),
        BossHpContribution: OneDecimal(info.EntireContribution),
        HitCount: rates.HitCount,
        CritRate: rates.CritRate,
        StrongRate: rates.StrongRate,
        PerfectRate: rates.PerfectRate,
        BackRate: rates.BackRate,
        ParryRate: rates.ParryRate,
        BossBlockRate: 0.0);

    private static StatsBuffPayload ToBuffPayload(
        OperatingData value,
        string scope,
        string category,
        int? ownerId,
        JobClass? ownerJob,
        int? ownerParticipantIndex,
        int? actorParticipantIndex,
        string? actorIdentityHash)
    {
        string? source = scope == "participant" && ownerId != null
            ? BuffSource(ownerId.Value, ownerJob, value.ActorId, value.Code)
            : null;
        return new StatsBuffPayload(
            BuffCode: value.Code,
            BuffName: value.Name,
            OperatingRate: OneDecimal(value.OperatingRate),
            Scope: scope,
            Category: category,
            Source: source,
            ActorIdentityHash: actorIdentityHash,
            OwnerParticipantIndex: ownerParticipantIndex,
            ActorParticipantIndex: actorParticipantIndex);
    }

    // Same classification as the local meter's BuffRateSection.categorize():
    //   self  = caster == owner AND buff-code job prefix == owner job
    //   other = caster == owner but prefix mismatch (consumable/scroll/other-job self-buff)
    //   party = caster != owner (another player applied it; same-job dupes split by actorIdentityHash)
    private static string BuffSource(int ownerId, JobClass? ownerJob, int actorId, int buffCode)
    {
        if (actorId != ownerId)
        {
            return "party";
        }

        int? ownerPrefix = ownerJob != null ? ownerJob.Value.BasicSkillCode() / 1_000_000 : null;
        int codePrefix = buffCode / 10_000_000;
        return ownerPrefix != null && codePrefix == ownerPrefix ? "self" : "other";
    }

    private sealed record RateSummary(
        int HitCount,
        double CritRate,
        double StrongRate,
        double PerfectRate,
        double BackRate,
        double ParryRate);

    private static RateSummary SummarizeRates(IEnumerable<AnalyzedSkill> skillsEnumerable)
    {
        List<AnalyzedSkill> skills = skillsEnumerable as List<AnalyzedSkill> ?? skillsEnumerable.ToList();
        int directHits = Math.Max(skills.Sum(s => s.Times), 0);
        int allHits = directHits + Math.Max(skills.Sum(s => s.DotTimes), 0);
        double Rate(int count) => directHits > 0 ? OneDecimal((double)count / directHits * 100.0) : 0.0;

        return new RateSummary(
            HitCount: allHits,
            CritRate: Rate(skills.Sum(s => s.CritTimes)),
            StrongRate: Rate(skills.Sum(s => s.DoubleTimes)),
            PerfectRate: Rate(skills.Sum(s => s.PerfectTimes)),
            BackRate: Rate(skills.Sum(s => s.BackTimes)),
            ParryRate: Rate(skills.Sum(s => s.ParryTimes)));
    }

    private static string BattleHash(int server, string nickname, int mobCode, long startedAt, long endedAt, long totalDamage, long durationMs)
    {
        long roundedStart = startedAt / 10_000L * 10_000L;
        long roundedEnd = endedAt / 10_000L * 10_000L;
        string raw = string.Join("|", new object[] { server, nickname, mobCode, roundedStart, roundedEnd, totalDamage, durationMs });
        return StatsIdentity.Sha256(raw);
    }

    private static double AmountOf(DpsLog log, int uid) =>
        log.Report.Information.TryGetValue(uid, out DpsInformation? info) ? info.Amount : 0.0;

    // Kotlin Double.roundToLong(): nearest, ties toward +infinity (= floor(x + 0.5)).
    private static long RoundToLong(double value) => (long)Math.Floor(value + 0.5);

    // Kotlin kotlin.math.round(): nearest, ties to even (banker's) — System.Text default.
    private static double OneDecimal(double value) => Math.Round(value * 10.0, MidpointRounding.ToEven) / 10.0;

    private static string? NonBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static int? IndexOrNull(Dictionary<int, int> map, int key) => map.TryGetValue(key, out int value) ? value : null;
}

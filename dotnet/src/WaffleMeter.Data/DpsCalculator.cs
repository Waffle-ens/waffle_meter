using WaffleMeter.Capture;

namespace WaffleMeter.Data;

/// <summary>
/// Verbatim port of Kotlin <c>DpsCalculator</c> (DpsCalculator.kt). Incremental, stateful: getDps()
/// is a battle state machine driven by DataManager's current-target/battle-revision and the
/// per-target packet ring buffer; it accumulates damage into cached per-player info + skill details,
/// and on a battle transition saves a frozen DpsLog (report + skill details + buff rates) to history.
///
/// Contributors keep insertion order with move-to-end on re-add (Kotlin LinkedHashSet semantics),
/// implemented here with a List + remove-by-id + append.
/// </summary>
public sealed class DpsCalculator
{
    private const long PreemptivePacketWindowMs = 1000L;

    private readonly DataManager _dm;
    private readonly Action? _streamResetCallback;

    private int _currentTarget;
    private long _currentBattleRevision;
    private bool _recentTargetWasDummy;

    private DpsReport _recentData = new();
    private bool _recentDataSaved;
    private Dictionary<int, Dictionary<string, AnalyzedSkill>> _recentSkillDetails = new();
    private Dictionary<int, List<OperatingData>> _recentBuffRates = new();
    private List<OperatingData> _recentBossBuffRates = [];

    private long _lastProcessedSequence;
    private readonly Dictionary<int, DpsInformation> _cachedInfo = new();
    private readonly List<User> _cachedContributors = []; // ordered, move-to-end on re-add
    private long _cachedBattleEnd;
    private long _cachedBattleStart;
    private bool _isCachedBattleStartFake;
    private readonly Dictionary<int, Dictionary<string, AnalyzedSkill>> _cachedSkillDetails = new();

    private static readonly string[] SummonDamageSkillPrefixes =
    [
        "불의 정령:",
        "물의 정령:",
        "바람의 정령:",
        "땅의 정령:",
        "고대의 정령:",
    ];

    public DpsCalculator(DataManager dm, Action? streamResetCallback = null)
    {
        _dm = dm;
        _streamResetCallback = streamResetCallback;
    }

    public DpsReport GetRecentData() => _recentData;

    private void ResetCache()
    {
        _lastProcessedSequence = 0L;
        _cachedInfo.Clear();
        _cachedContributors.Clear();
        _cachedSkillDetails.Clear();
        _cachedBattleEnd = 0L;
        _cachedBattleStart = 0L;
        _isCachedBattleStartFake = false;
    }

    private bool IsSummonDamageSkill(int skillCode)
    {
        string? skillName = _dm.Skill(skillCode)?.Name;
        if (skillName == null) return false;
        return SummonDamageSkillPrefixes.Any(p => skillName.StartsWith(p, StringComparison.Ordinal));
    }

    private int? ResolveActor(ParsedDamagePacket packet, ICollection<User> candidates)
    {
        // Fold a summon's damage into its summoner — BUT never when the actor is itself a known player.
        // The summon map persists across battles (only HardReset flushes it) and entity ids get reused,
        // so a stale summonId->owner mapping could otherwise steal a FOREIGN player's skill (e.g. a 검성's
        // 흡혈의 검) into another player's breakdown. A real player is never a summon, so attribute a known
        // user's damage to that user directly. (Hardening over the original Kotlin's unconditional fold.)
        int? summoner = _dm.SummonerId(packet.ActorId);
        if (summoner != null && _dm.User(packet.ActorId) == null) return summoner;

        if (IsSummonDamageSkill(packet.SkillCode))
        {
            List<int> elementalists = candidates
                .Where(u => u.Job == JobClass.ELEMENTALIST)
                .Select(u => u.Id)
                .Distinct()
                .ToList();
            return elementalists.Count == 1 ? elementalists[0] : null;
        }

        if (_dm.IsMobInstance(packet.ActorId)) return null;
        return packet.ActorId;
    }

    private static Dictionary<int, Dictionary<string, AnalyzedSkill>> CloneSkillDetails(
        Dictionary<int, Dictionary<string, AnalyzedSkill>> source)
    {
        var result = new Dictionary<int, Dictionary<string, AnalyzedSkill>>();
        foreach (KeyValuePair<int, Dictionary<string, AnalyzedSkill>> kv in source)
        {
            var inner = new Dictionary<string, AnalyzedSkill>();
            foreach (KeyValuePair<string, AnalyzedSkill> sk in kv.Value)
            {
                inner[sk.Key] = sk.Value.Copy();
            }

            result[kv.Key] = inner;
        }

        return result;
    }

    private void AccumulateSkillDetail(
        Dictionary<int, Dictionary<string, AnalyzedSkill>> target, ParsedDamagePacket packet, int actor)
    {
        string skillCode = packet.SkillCode.ToString();
        if (!target.TryGetValue(actor, out Dictionary<string, AnalyzedSkill>? actorSkills))
        {
            actorSkills = new Dictionary<string, AnalyzedSkill>();
            target[actor] = actorSkills;
        }

        if (!actorSkills.ContainsKey(skillCode))
        {
            Skill? skill = _dm.Skill(packet.SkillCode);
            var analyzed = new AnalyzedSkill { SkillCode = packet.SkillCode, Name = skill?.Name ?? skillCode };
            actorSkills[skillCode] = analyzed;
        }

        AnalyzedSkill analyzedSkill = actorSkills[skillCode];
        if (packet.Dot)
        {
            analyzedSkill.DotTimes++;
            analyzedSkill.DotDamageAmount += packet.Damage;
        }
        else
        {
            analyzedSkill.Times++;
            analyzedSkill.DamageAmount += packet.Damage;
            if (packet.IsCrit) analyzedSkill.CritTimes++;
            if (packet.Specials.Contains(SpecialDamage.BACK)) analyzedSkill.BackTimes++;
            if (packet.Specials.Contains(SpecialDamage.PARRY)) analyzedSkill.ParryTimes++;
            if (packet.Specials.Contains(SpecialDamage.DOUBLE)) analyzedSkill.DoubleTimes++;
            if (packet.Specials.Contains(SpecialDamage.PERFECT)) analyzedSkill.PerfectTimes++;
            if (packet.Specials.Contains(SpecialDamage.POWER_SHARD)) analyzedSkill.ShardTimes++;
            if (packet.Loop != 0) analyzedSkill.MultiHitTimes++;
        }
    }

    private void AccumulatePacket(ParsedDamagePacket packet)
    {
        int? actorN = ResolveActor(packet, _cachedContributors);
        if (actorN == null) return;
        int actor = actorN.Value;
        User? user = _dm.User(actor);
        if (user == null) return;

        _cachedContributors.RemoveAll(u => u.Id == user.Id);
        _cachedContributors.Add(user);

        if (user.Job == null)
        {
            user.Job = JobClassInfo.ConvertFromSkill(packet.SkillCode);
        }

        if (!string.IsNullOrWhiteSpace(user.Nickname) && user.Server > 0 && user.Power <= 0)
        {
            _dm.RequestOfficialCharacterLookup(user.Id);
        }

        if (!_cachedInfo.TryGetValue(user.Id, out DpsInformation? info))
        {
            info = new DpsInformation();
            _cachedInfo[user.Id] = info;
        }

        info.AddDamage(packet.Damage);
        AccumulateSkillDetail(_cachedSkillDetails, packet, user.Id);

        long ts = packet.Timestamp;
        if (_cachedBattleStart == 0L)
        {
            _cachedBattleStart = ts;
            _isCachedBattleStartFake = true;
        }

        if (_isCachedBattleStartFake && _cachedBattleStart > ts) _cachedBattleStart = ts;
        if (ts > _cachedBattleEnd) _cachedBattleEnd = ts;
    }

    private long ActivePacketCutoff()
    {
        long start = _dm.CurrentBattleStart();
        return start > PreemptivePacketWindowMs ? start - PreemptivePacketWindowMs : 0L;
    }

    private void ProcessPendingPacketsBefore(int targetId, long before)
    {
        if (targetId <= 0) return;
        PacketWindow window = _dm.BattleDataSince(targetId, _lastProcessedSequence);
        if (window.DroppedBeforeStart)
        {
            ResetCache();
        }

        foreach (ParsedDamagePacket p in window.Packets)
        {
            if (before <= 0L || p.Timestamp < before)
            {
                AccumulatePacket(p);
            }
        }

        _lastProcessedSequence = window.NextSequence;
    }

    private void RefreshRecentReportFromCache(int targetId, MobInfo? fixedTarget)
    {
        long battleStart = _cachedBattleStart > 0L ? _cachedBattleStart : _recentData.BattleStart;
        long battleEnd = Math.Max(_cachedBattleEnd > 0L ? _cachedBattleEnd : _recentData.BattleEnd, battleStart);

        MobInfo? targetInfo;
        if (fixedTarget != null)
        {
            targetInfo = new MobInfo(fixedTarget.Id, fixedTarget.Mob, fixedTarget.RemainHp, fixedTarget.MaxHp);
        }
        else
        {
            int? mobCode = _dm.GetMobId(targetId);
            Mob? mob = mobCode != null ? _dm.Mob(mobCode.Value) : null;
            targetInfo = mob != null
                ? new MobInfo(targetId, mob, _dm.MobHp(targetId) ?? 0, _dm.MobMaxHp(targetId) ?? 0)
                : null;
        }

        var report = new DpsReport
        {
            Contributors = CopyContributors(),
            BattleStart = battleStart,
            BattleEnd = battleEnd,
            Packets = null,
            Target = targetInfo,
        };

        double totalDamage = _cachedInfo.Values.Sum(i => i.Amount);
        long duration = report.BattleEnd - report.BattleStart;
        double? fixedMax = targetInfo != null && targetInfo.MaxHp > 0 ? (double?)targetInfo.MaxHp : null;
        double mobMaxHp = fixedMax ?? _dm.MobMaxHp(targetId) ?? 0.0;

        foreach (KeyValuePair<int, DpsInformation> kv in _cachedInfo)
        {
            report.Information[kv.Key] = new DpsInformation(
                kv.Value.Amount,
                duration > 0 ? kv.Value.Amount / duration * 1000 : 0.0,
                totalDamage > 0 ? kv.Value.Amount / totalDamage * 100 : 0.0,
                mobMaxHp > 0 ? kv.Value.Amount / mobMaxHp * 100 : 0.0);
        }

        _recentData = report;
    }

    public DpsReport GetLiveReport()
    {
        int storageTarget = _dm.CurrentTarget();
        if (storageTarget == -1) return _recentData;
        if (!_recentData.IsEmpty() && _recentData.Target?.Id == storageTarget) return _recentData;
        return new DpsReport
        {
            BattleStart = _dm.CurrentBattleStart(),
            BattleEnd = _dm.CurrentBattleEnd(),
            Packets = _dm.BattleData(storageTarget),
        };
    }

    public DpsReport GetDps()
    {
        int storageTarget = _dm.CurrentTarget();
        long storageBattleRevision = _dm.CurrentBattleRevision();
        int previousTarget = _currentTarget;
        bool prevTargetDummy = _dm.IsCurrentTargetDummy();
        bool targetChanged = storageTarget != previousTarget;
        bool battleRestartedWithSameTarget =
            storageTarget > 0 && previousTarget > 0 && storageBattleRevision != _currentBattleRevision;
        bool isNewBattleEnd = storageTarget == -1 && storageTarget != previousTarget;

        if ((targetChanged || battleRestartedWithSameTarget) && !prevTargetDummy
            && storageTarget != -1 && previousTarget > 0 && !_recentData.IsEmpty())
        {
            ProcessPendingPacketsBefore(previousTarget, ActivePacketCutoff());
            RefreshRecentReportFromCache(previousTarget, _recentData.Target);
            SaveRecentBattleLog();
            _recentDataSaved = true;
        }

        if (targetChanged || battleRestartedWithSameTarget)
        {
            ResetCache();
        }

        _currentTarget = storageTarget;
        _currentBattleRevision = storageBattleRevision;
        _recentTargetWasDummy = prevTargetDummy;

        if (_currentTarget == -1)
        {
            long battleEnd = _dm.CurrentBattleEnd();
            if (isNewBattleEnd)
            {
                _recentData.BattleEnd = battleEnd;
                if (previousTarget > 0)
                {
                    ProcessPendingPacketsBefore(previousTarget, 0L);
                    RefreshRecentReportFromCache(previousTarget, _recentData.Target);
                    if (_recentData.Target != null)
                    {
                        _recentData.Target.RemainHp = _dm.MobHp(previousTarget) ?? _recentData.Target.RemainHp;
                        _recentData.Target.MaxHp = _dm.MobMaxHp(previousTarget) ?? _recentData.Target.MaxHp;
                    }
                }
            }

            _dm.FlushPacket();
            if (isNewBattleEnd && !_recentData.IsEmpty() && !_recentTargetWasDummy)
            {
                SaveRecentBattleLog();
                _recentDataSaved = true;
            }

            return _recentData;
        }

        if (_currentTarget == 0)
        {
            return new DpsReport();
        }

        List<ParsedDamagePacket>? reportPackets;
        if (_currentTarget > 0)
        {
            PacketWindow window = _dm.BattleDataSince(_currentTarget, _lastProcessedSequence);
            if (window.DroppedBeforeStart)
            {
                ResetCache();
            }

            long packetCutoff = ActivePacketCutoff();
            foreach (ParsedDamagePacket p in window.Packets)
            {
                if (p.Timestamp >= packetCutoff)
                {
                    AccumulatePacket(p);
                }
            }

            _lastProcessedSequence = window.NextSequence;
            reportPackets = null;
        }
        else
        {
            List<ParsedDamagePacket>? data = _dm.BattleData(_currentTarget);
            int currentCount = data?.Count ?? 0;
            if (currentCount < _lastProcessedSequence)
            {
                ResetCache();
            }

            int fromIndex = Math.Clamp((int)_lastProcessedSequence, 0, currentCount);
            if (data != null && currentCount > fromIndex)
            {
                for (int i = fromIndex; i < currentCount; i++)
                {
                    AccumulatePacket(data[i]);
                }

                _lastProcessedSequence = currentCount;
            }

            reportPackets = data;
        }

        long dmStart = _dm.CurrentBattleStart();
        long dmEnd = _dm.CurrentBattleEnd();
        var report = new DpsReport
        {
            Contributors = CopyContributors(),
            BattleStart = dmStart != 0L && _cachedBattleStart != 0L ? Math.Min(dmStart, _cachedBattleStart)
                : dmStart != 0L ? dmStart : _cachedBattleStart,
            BattleEnd = Math.Max(dmEnd, _cachedBattleEnd),
            Packets = reportPackets,
        };

        if (_currentTarget > 0)
        {
            int? mobCode = _dm.GetMobId(_currentTarget);
            Mob mob = _dm.Mob(mobCode!.Value)!;
            report.Target = new MobInfo(_currentTarget, mob)
            {
                RemainHp = _dm.MobHp(_currentTarget) ?? 0,
                MaxHp = _dm.MobMaxHp(_currentTarget) ?? 0,
            };
        }

        double totalDamage = _cachedInfo.Values.Sum(i => i.Amount);
        long duration = report.BattleEnd - report.BattleStart;
        double mobMaxHp = _dm.MobMaxHp(_currentTarget) ?? 0.0;
        foreach (KeyValuePair<int, DpsInformation> kv in _cachedInfo)
        {
            report.Information[kv.Key] = new DpsInformation(
                kv.Value.Amount,
                duration > 0 ? kv.Value.Amount / duration * 1000 : 0.0,
                totalDamage > 0 ? kv.Value.Amount / totalDamage * 100 : 0.0,
                mobMaxHp > 0 ? kv.Value.Amount / mobMaxHp * 100 : 0.0);
        }

        if (_dm.IsCurrentTargetDummy())
        {
            int executorId = _dm.ExecutorId();
            if (executorId != 0 && !report.Contributors.Any(u => u.IsExecutor))
            {
                return _recentData;
            }
        }

        _recentData = report;
        _recentSkillDetails = new Dictionary<int, Dictionary<string, AnalyzedSkill>>();
        _recentBuffRates = new Dictionary<int, List<OperatingData>>();
        _recentBossBuffRates = [];
        _recentDataSaved = false;
        return report;
    }

    private List<User> CopyContributors() => [.. _cachedContributors];

    private Dictionary<int, Dictionary<string, AnalyzedSkill>> BuildSkillDetails(DpsReport data)
    {
        var analyzedByActor = new Dictionary<int, Dictionary<string, AnalyzedSkill>>();
        var contributorIds = data.Contributors.Select(u => u.Id).ToHashSet();
        if (data.Packets != null)
        {
            foreach (ParsedDamagePacket p in data.Packets)
            {
                int? realActor = ResolveActor(p, data.Contributors);
                if (realActor == null || !contributorIds.Contains(realActor.Value)) continue;
                AccumulateSkillDetail(analyzedByActor, p, realActor.Value);
            }
        }

        return analyzedByActor;
    }

    public Dictionary<string, AnalyzedSkill> BattleDetails(DpsReport? data, int uid)
    {
        if (data == null) return new Dictionary<string, AnalyzedSkill>();
        if (ReferenceEquals(data, _recentData) && data.Packets == null)
        {
            Dictionary<string, AnalyzedSkill> details =
                _cachedSkillDetails.GetValueOrDefault(uid) ?? _recentSkillDetails.GetValueOrDefault(uid) ?? new();
            return details.ToDictionary(kv => kv.Key, kv => kv.Value.Copy());
        }

        Dictionary<string, AnalyzedSkill> built = BuildSkillDetails(data).GetValueOrDefault(uid) ?? new();
        return new Dictionary<string, AnalyzedSkill>(built);
    }

    private readonly record struct BuffDisplay(int Code, string Name, string? Summary, string? Effect);

    private static bool IsPlaceholderBuff(Buff buff) =>
        string.IsNullOrWhiteSpace(buff.Name) || buff.Name.Equals("None", StringComparison.OrdinalIgnoreCase);

    private int? NormalizeBuffSkillCode(int code)
    {
        var candidates = new List<int>();

        void Add(int value)
        {
            if (value <= 0) return;
            if (!candidates.Contains(value)) candidates.Add(value);
            int rounded = (value / 10) * 10;
            if (!candidates.Contains(rounded)) candidates.Add(rounded);
        }

        Add(code);
        if (code is >= 110_000_000 and <= 190_999_999)
        {
            Add((code / 100_000) * 10_000);
            Add((code / 10_000) * 1_000);
        }

        Add(code / 10);
        Add(code / 100);

        foreach (int c in candidates)
        {
            if (_dm.Skill(c) != null) return c;
        }

        return null;
    }

    private BuffDisplay? ResolveBuffDisplay(int code)
    {
        Buff? buff = _dm.Buff(code);
        if (buff != null)
        {
            if (IsPlaceholderBuff(buff)) return null;
            return new BuffDisplay(code, buff.Name, buff.Summary, buff.Effect);
        }

        int? skillCode = NormalizeBuffSkillCode(code);
        if (skillCode == null) return null;
        Skill? skill = _dm.Skill(skillCode.Value);
        if (skill == null) return null;
        string? name = skill.Name;
        if (string.IsNullOrWhiteSpace(name) || name.Equals("None", StringComparison.OrdinalIgnoreCase)) return null;
        return new BuffDisplay(skillCode.Value, name, null, null);
    }

    public List<OperatingData> GetBuffOperatingRate(int uid, long start, long end)
    {
        long totalDuration = end - start;
        if (totalDuration <= 0) return [];

        var groups = _dm.BattleBuff(uid, start, end)
            .Where(b => !_dm.IsBuffBlacklisted(b.SkillCode))
            .Select(useBuff => (Display: ResolveBuffDisplay(useBuff.SkillCode), UseBuff: useBuff))
            .Where(x => x.Display != null)
            .GroupBy(x => (x.Display!.Value.Code, x.UseBuff.ActorId));

        var result = new List<OperatingData>();
        foreach (var group in groups)
        {
            int actorId = group.Key.ActorId;
            BuffDisplay display = group.First().Display!.Value;
            var clamped = group
                .Select(x => (First: Math.Max(x.UseBuff.BuffStart, start), Second: Math.Min(x.UseBuff.BuffEnd, end)))
                .OrderBy(iv => iv.First)
                .ToList();

            var merged = new List<(long First, long Second)>();
            foreach ((long First, long Second) interval in clamped)
            {
                if (merged.Count == 0 || interval.First > merged[^1].Second)
                {
                    merged.Add(interval);
                }
                else
                {
                    (long First, long Second) last = merged[^1];
                    merged[^1] = (last.First, Math.Max(last.Second, interval.Second));
                }
            }

            double rate = (double)merged.Sum(iv => iv.Second - iv.First) / totalDuration * 100.0;
            result.Add(new OperatingData(display.Code, display.Name, display.Summary, display.Effect, rate, actorId));
        }

        return result;
    }

    private Dictionary<int, List<OperatingData>> BuildBuffRates(DpsReport data)
    {
        if (data.BattleEnd <= data.BattleStart) return new();
        var result = new Dictionary<int, List<OperatingData>>();
        foreach (User user in data.Contributors)
        {
            result[user.Id] = GetBuffOperatingRate(user.Id, data.BattleStart, data.BattleEnd);
        }

        return result;
    }

    private List<OperatingData> BuildBossBuffRates(DpsReport data)
    {
        int? targetId = data.Target?.Id;
        if (targetId == null) return [];
        if (data.BattleEnd <= data.BattleStart) return [];
        return GetBuffOperatingRate(targetId.Value, data.BattleStart, data.BattleEnd);
    }

    /// <summary>
    /// Invoked with the frozen DpsLog each time a battle is saved (Kotlin called StatsUploadQueue
    /// here directly). Left null in replay/headless-without-upload so the DPS golden is unaffected;
    /// the live app wires it to the stats upload queue.
    /// </summary>
    public Action<DpsLog>? OnBattleLogged { get; set; }

    private void SaveRecentBattleLog()
    {
        Dictionary<int, Dictionary<string, AnalyzedSkill>> skillDetails =
            _cachedSkillDetails.Count > 0 ? CloneSkillDetails(_cachedSkillDetails) : BuildSkillDetails(_recentData);
        Dictionary<int, List<OperatingData>> buffRates = BuildBuffRates(_recentData);
        List<OperatingData> bossBuffRates = BuildBossBuffRates(_recentData);

        _recentSkillDetails = skillDetails;
        _recentBuffRates = buffRates;
        _recentBossBuffRates = bossBuffRates;

        DpsLog log = _dm.SaveBattleLog(_recentData, skillDetails, buffRates, bossBuffRates);
        OnBattleLogged?.Invoke(log);
        _recentData.Packets = null;
    }

    public void ResetDataStorage()
    {
        if (!_recentData.IsEmpty() && !_recentDataSaved && !_dm.IsCurrentTargetDummy())
        {
            SaveRecentBattleLog();
            _recentDataSaved = true;
        }

        _dm.FlushPacket();
        _currentTarget = -1;
        _currentBattleRevision = _dm.CurrentBattleRevision();
        _recentData = new DpsReport();
        _recentSkillDetails = new();
        _recentBuffRates = new();
        _recentBossBuffRates = [];
        _recentDataSaved = false;
        ResetCache();
    }

    public void HardReset()
    {
        _dm.HardReset();
        _streamResetCallback?.Invoke();
        _currentTarget = -1;
        _currentBattleRevision = 0;
        _recentData = new DpsReport();
        _recentSkillDetails = new();
        _recentBuffRates = new();
        _recentBossBuffRates = [];
        _recentDataSaved = false;
        ResetCache();
    }
}

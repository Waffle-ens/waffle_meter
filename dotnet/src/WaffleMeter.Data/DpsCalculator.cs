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
    private long _cachedBattleStartToggle; // the combat-enter toggle wall-clock at this battle's first damage
    private readonly Dictionary<int, Dictionary<string, AnalyzedSkill>> _cachedSkillDetails = new();
    // Per-second damage buckets (uid -> whole-second key (Timestamp/1000) -> damage that second), the raw
    // source for the combat-detail DPS graph. Keyed by ABSOLUTE second so it survives the reported BattleStart
    // being re-anchored (StartAnchor caps back-dating) — the dense per-report array is re-based at build time.
    private readonly Dictionary<int, Dictionary<long, long>> _cachedDpsBuckets = new();

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
        _cachedDpsBuckets.Clear();
        _cachedBattleEnd = 0L;
        _cachedBattleStart = 0L;
        _isCachedBattleStartFake = false;
        _cachedBattleStartToggle = 0L;
    }

    /// <summary>How far the reported battle start may sit BEFORE the combat-enter toggle. Pre-toggle openers
    /// are still counted (<see cref="ActivePacketCutoff"/> admits ~1 s of them); this only caps how far the
    /// clock is back-dated, which was the dominant source of the combat-time gap vs the in-game meter — our
    /// start could sit a full second before the toggle. Keep small: on a re-pull the first resumed damage is
    /// AFTER the toggle, so the floor never binds and the re-pull deflation fix is untouched.</summary>
    private const long StartBackdateLimitMs = 250L;

    /// <summary>The battle start to report: the first-damage timestamp, but never earlier than
    /// <see cref="StartBackdateLimitMs"/> before the combat-enter toggle. 0 when no damage yet.</summary>
    private long StartAnchor()
    {
        if (_cachedBattleStart == 0L)
        {
            return 0L;
        }

        long floor = _cachedBattleStartToggle > 0L ? _cachedBattleStartToggle - StartBackdateLimitMs : 0L;

        // The floor may only TRIM pre-toggle openers (the capture admits ~PreemptivePacketWindowMs of them),
        // never AMPUTATE the battle. On the end-of-battle full-ring rebuild the toggle snapshot can be a LATE
        // re-toggle (a corpse/phase restart overwrote CurrentBattleStart), and letting that floor bind collapsed a
        // whole fight's window to [lateToggle-250ms, lastHit] while Amount kept the full fight — the 191M-DPS
        // uploads of 2026-07-17. A legitimate opener floor sits at most ~750ms above the first counted hit, so a
        // floor beyond first-hit + the preemptive window can only mean the toggle does not belong to this window's
        // start: ignore it.
        if (floor > _cachedBattleStart + PreemptivePacketWindowMs)
        {
            return _cachedBattleStart;
        }

        return Math.Max(_cachedBattleStart, floor);
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
            // The full wire code carries the caster's specialization (특화) in its last four digits; keep a
            // representative one (constant per actor/battle) for the detail panel to decode.
            if (packet.RawSkillCode != 0) analyzedSkill.RawSkillCode = packet.RawSkillCode;
            // A special-flag region exists only for switch-type 5/6/7 (region size ≥ 10); switch-type 4 hits
            // (heals/buffs/passives) have no flag byte, so back/전방/강타/완벽/페리 are unmeasurable on them.
            // Count the flag-bearing hits so those rates use them as the denominator instead of every hit.
            if ((packet.SwitchVariable & 0x0F) is 5 or 6 or 7) analyzedSkill.FlaggedTimes++;
            if (packet.IsCrit) analyzedSkill.CritTimes++;
            // Attack direction comes from the position byte the 07-01 patch added (IsBack/IsFront), NOT the
            // special-flag byte: its raw bit 0x80 (which the old back count used) is a ~45%-incidence proc
            // statistically independent of facing — measured across 151,937 hits — so it mis-counted back
            // attacks. Front and back are mutually exclusive.
            if (packet.IsBack) analyzedSkill.BackTimes++;
            if (packet.IsFront) analyzedSkill.FrontTimes++;
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
        if (user == null)
        {
            // 난입(mid-join) / late identity: the executor's own-nickname (0x3633) — the only packet that
            // registers "this uid is me" — can arrive AFTER the first boss fight, so the executor's own
            // damage (often the top dealer) was being silently dropped, losing its DPS row entirely. Instead
            // of dropping, provisionally register an un-folded actor that is dealing a JOB-LOCKED damage skill
            // (direct evidence it is a player — AION2 damage skills are job-locked), so the row shows with the
            // correct DPS now and is enriched (nickname/server/job/isExecutor) IN PLACE when 0x3633/0x3645
            // arrives. The un-folded + job-locked-skill gate keeps non-player entities (NPC allies, unmapped
            // pets) out; summons/mobs are already folded or dropped by ResolveActor.
            if (actor != packet.ActorId || JobClassInfo.ConvertFromSkill(packet.SkillCode) is null)
            {
                return;
            }

            user = _dm.EnsureUser(actor);
        }

        _cachedContributors.RemoveAll(u => u.Id == user.Id);
        _cachedContributors.Add(user);

        // Infer job from the skill code ONLY for the actor's OWN (un-folded) packet. When ResolveActor
        // folded this packet onto another uid (a summon owner, or the lone elementalist), the skill code
        // belongs to a DIFFERENT player, so inferring from it would mislabel the resolved user — the
        // reported wrong-class-icon bug (e.g. a CLERIC/CHANTER summon's 17xx/18xx code folded onto a
        // TEMPLAR). The fold guard (actor == packet.ActorId) keeps a folded foreign code from ever reaching
        // here. An OWN job-locked skill is the highest-confidence source, so it CORRECTS a wrong jobByte /
        // official label (e.g. a 살성's 13xxxxxx skills overriding a mis-read 궁성 jobByte), not just fills
        // a missing job — see User.TrySetJob / JobProvenance.
        if (actor == packet.ActorId && JobClassInfo.ConvertFromSkill(packet.SkillCode) is { } inferredJob)
        {
            user.TrySetJob(inferredJob, JobProvenance.OwnSkill);
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

        // Bucket this hit into its absolute whole-second for the DPS graph. Absolute (not BattleStart-relative)
        // so it is immune to the reported start being re-anchored below; the dense array is re-based at build.
        if (ts > 0L)
        {
            if (!_cachedDpsBuckets.TryGetValue(user.Id, out Dictionary<long, long>? buckets))
            {
                buckets = new Dictionary<long, long>();
                _cachedDpsBuckets[user.Id] = buckets;
            }

            long second = ts / 1000L;
            buckets[second] = buckets.GetValueOrDefault(second) + packet.Damage;
        }

        if (_cachedBattleStart == 0L)
        {
            _cachedBattleStart = ts;
            _isCachedBattleStartFake = true;
            // Snapshot the combat-enter toggle for this battle (reliable here — the first damage is only
            // accumulated during live processing of the started battle). Used to cap start back-dating.
            _cachedBattleStartToggle = _dm.CurrentBattleStart();
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
        PurgeResolvedNonPlayers();
        TryRecoverExecutorFromRoster();
        long battleStart = _cachedBattleStart > 0L ? StartAnchor() : _recentData.BattleStart;
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
            Contributors = FreezeContributors(),
            BattleStart = battleStart,
            BattleEnd = battleEnd,
            Packets = null,
            Target = targetInfo,
            ExecutorId = _dm.ExecutorId(), // freeze 본인 uid so the post-combat idle ("대기 중") view self-colors the
                                           // own row in 직업 강조 mode — this is the report returned in the idle state,
                                           // so without it self-id falls to the transient _selfId and reverts to job color
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
        _dm.TickDummyBattle(); // enforce the dummy duration hard-cut / idle-end / mode-off before reading state
        int storageTarget = _dm.CurrentTarget();
        long storageBattleRevision = _dm.CurrentBattleRevision();
        int previousTarget = _currentTarget;
        // Key off the PREVIOUS target: at an end-of-battle transition CurrentTarget is already -1, so reading the
        // LIVE target here would report false and the "don't save a dummy run to history" guard (below) would not
        // fire. IsMobDummy(previousTarget) is what makes the mid-battle and end-of-battle save-skips actually work.
        bool prevTargetDummy = _dm.IsMobDummy(previousTarget);
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

        PurgeResolvedNonPlayers();
        TryRecoverExecutorFromRoster();

        long dmStart = _dm.CurrentBattleStart();
        long dmEnd = _dm.CurrentBattleEnd();
        var report = new DpsReport
        {
            Contributors = CopyContributors(),
            // Duration basis MUST match the FINAL/saved report (RefreshRecentReportFromCache, which uses
            // _cachedBattleStart only): once any damage exists, count from the first DAMAGE timestamp
            // (_cachedBattleStart), falling back to the battle-start toggle wall-clock (CurrentBattleStart)
            // only before the first hit. This previously took Math.Min(dmStart, _cachedBattleStart); on a
            // re-pull (notably after a 전멸) the start toggle fires at re-aggro/run-back WELL BEFORE damage
            // resumes, so Math.Min picked the earlier toggle and the live duration absorbed the whole
            // pre-damage gap — deflating the in-progress DPS until the battle ended and the toggle start was
            // dropped (the "전투 중엔 낮게 표시되다 끝나면 정상" report). The end report is unchanged.
            // StartAnchor() additionally caps back-dating to 250ms before the toggle (openers still counted).
            BattleStart = _cachedBattleStart != 0L ? StartAnchor() : dmStart,
            BattleEnd = Math.Max(dmEnd, _cachedBattleEnd),
            Packets = reportPackets,
            ExecutorId = _dm.ExecutorId(), // carry 본인 uid into the live report so self-coloring survives the
                                           // transition into the post-combat idle state (matches the saved snapshot)
        };

        if (_currentTarget > 0)
        {
            int? mobCode = _dm.GetMobId(_currentTarget);
            // An engaged-but-UNRECOGNIZED target (mobCode never registered, or a code not in the catalog — e.g.
            // the intermittent first-boss miss) must NOT build a MobInfo with a null Mob: that null-derefs later
            // as Target.Mob.Name in OverlayViewModel.Update and, dispatched via Dispatcher.Invoke, crashes the
            // consumer thread (bypassing DispatcherUnhandledException). Mirror the null-safe path in GetDps
            // (~L296): leave Target null so the overlay shows "타겟 인식 실패" instead of crashing.
            Mob? mob = mobCode != null ? _dm.Mob(mobCode.Value) : null;
            if (mob != null)
            {
                report.Target = new MobInfo(_currentTarget, mob)
                {
                    RemainHp = _dm.MobHp(_currentTarget) ?? 0,
                    MaxHp = _dm.MobMaxHp(_currentTarget) ?? 0,
                };
            }
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

    /// <summary>How old the party roster may be and still identify us (the same window the replay scopes by).</summary>
    private const long RosterRecoveryWindowMs = 30 * 60 * 1000L;

    /// <summary>
    /// Recover "which uid is me" when the own-nickname packet (0x3633) never arrived.
    /// <para>
    /// 0x3633 is the ONLY packet that says "this uid is you", and the server sends it on a character/zone
    /// load — so a meter started (or restarted) while you are already inside a dungeon never sees it. Your
    /// damage still lands (AccumulatePacket registers a nickname-less provisional row), but nothing knows it
    /// is yours: no self colour, no self row name, and the replay drops you, because scoping a recording to
    /// the party works by name and yours is blank.
    /// </para>
    /// <para>
    /// The party roster (0x9702) DOES include us. So when exactly one roster identity is unaccounted for —
    /// nobody in the repository carries that name — and exactly one damaging contributor is nameless, the two
    /// are each other. Both "exactly one"s are the guard: a party member who has not damaged (or not been
    /// named) yet leaves two loose ends, and we wait rather than guess.
    /// </para>
    /// </summary>
    private void TryRecoverExecutorFromRoster()
    {
        if (_dm.ExecutorId() != 0 || _cachedContributors.Count == 0)
        {
            return;
        }

        IReadOnlyList<(string Nickname, int Server)> roster = _dm.PartyMemberIdentities(RosterRecoveryWindowMs);
        if (roster.Count == 0)
        {
            return;
        }

        List<(string Nickname, int Server)> unclaimed = roster
            .Where(r => _dm.FindUserByNicknameAndServer(r.Nickname, r.Server) is null)
            .ToList();

        List<User> nameless = _cachedContributors
            .Where(u => string.IsNullOrWhiteSpace(u.Nickname))
            .ToList();

        if (unclaimed.Count != 1 || nameless.Count != 1)
        {
            return;
        }

        // jobByte 0 = "unknown": the job is already being inferred from our own job-locked damage skills,
        // which outrank a snapshot byte anyway.
        _dm.SaveNickname(nameless[0].Id, unclaimed[0].Nickname, isExecutor: true, unclaimed[0].Server, jobByte: 0);
    }

    private List<User> CopyContributors() => [.. _cachedContributors];

    // A summon whose owner-map (0x3641 summon_map) or mob registration lands AFTER it has already dealt damage
    // gets provisionally registered as a player by AccumulatePacket: its damage skill is job-locked (a summon
    // casts its owner's class skill), it is un-folded (no summon→owner mapping yet), and it is not yet a known
    // mob instance, so ResolveActor returns the raw entity id and the job-locked-skill gate lets EnsureUser
    // create a nickname-less row. Once the game reveals that entity as a summon (SummonerId) or mob
    // (IsMobInstance), retract the phantom so it never lingers as a non-party contributor. Only nickname-less
    // provisional rows are eligible — a real player (0x3633/0x3645 nickname) and the executor are never a
    // summon/mob, so they are never removed. Root cause of the 그리오사 "파티원 아닌 사람" phantom: a healer-summon
    // (0x3641 tagged 07 02 01, missed by the fixed 07 02 06 owner scan — see StreamProcessor) that also raced
    // its own spawn packet.
    private void PurgeResolvedNonPlayers()
    {
        for (int i = _cachedContributors.Count - 1; i >= 0; i--)
        {
            User u = _cachedContributors[i];
            if (!string.IsNullOrWhiteSpace(u.Nickname) || u.IsExecutor) continue;
            if (_dm.SummonerId(u.Id) == null && !_dm.IsMobInstance(u.Id)) continue;
            _cachedContributors.RemoveAt(i);
            _cachedInfo.Remove(u.Id);
            _cachedSkillDetails.Remove(u.Id);
        }
    }

    // Deep-copy the contributors so a FINISHED/standby battle's displayed identity (nickname/server/job/power)
    // is FROZEN at battle end — mirroring SaveBattleLog's CopyUser freeze. Without this, the standby report
    // (built by RefreshRecentReportFromCache and returned by GetDps in the idle state) shares the live
    // repository User objects; when the game later reissues a RETIRED entity uid to a DIFFERENT nearby player
    // (minutes after the fight) and DataManager.SaveNickname mutates that User in place, the still-displayed
    // finished-battle row repaints to the wrong player. The IN-COMBAT report (CopyContributors on the
    // live-target path) deliberately keeps the shared refs so identity correction still flows during the fight.
    private List<User> FreezeContributors() => [.. _cachedContributors.Select(u => u.Copy())];

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

        // A SAVED/history-replayed report carries a frozen per-actor skill snapshot (and Packets==null), so
        // prefer it — otherwise the rebuild-from-packets path below would return an empty table for every
        // replayed battle. Live/in-progress reports leave this empty and fall through to the cache/packets.
        if (data.SkillDetailsSnapshot.Count > 0)
        {
            Dictionary<string, AnalyzedSkill> snapshot = data.SkillDetailsSnapshot.GetValueOrDefault(uid) ?? new();
            return snapshot.ToDictionary(kv => kv.Key, kv => kv.Value.Copy());
        }

        if (ReferenceEquals(data, _recentData) && data.Packets == null)
        {
            Dictionary<string, AnalyzedSkill> details =
                _cachedSkillDetails.GetValueOrDefault(uid) ?? _recentSkillDetails.GetValueOrDefault(uid) ?? new();
            return details.ToDictionary(kv => kv.Key, kv => kv.Value.Copy());
        }

        Dictionary<string, AnalyzedSkill> built = BuildSkillDetails(data).GetValueOrDefault(uid) ?? new();
        return new Dictionary<string, AnalyzedSkill>(built);
    }

    /// <param name="Catalogued">True when the runtime code itself is in buff.json (so its code resolves an icon
    /// and a buff-value); false when only the skill NAME could be recovered and <see cref="Code"/> is the base.</param>
    private readonly record struct BuffDisplay(int Code, string Name, string? Summary, string? Effect, bool Catalogued);

    private static bool IsPlaceholderBuff(Buff buff) => DataManager.IsPlaceholderBuffName(buff.Name);

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
        // Job-buff codes 11xxxxxxx(검성)..19xxxxxxx(권성, 2026-07-01 패치). Upper bound raised from
        // 190_999_999 to 199_999_999 so 권성 buff codes floor to their base skill code (else 권성
        // buffs at higher ranks fail to resolve a name and get dropped from the buff/debuff view).
        if (code is >= 110_000_000 and <= 199_999_999)
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
            return new BuffDisplay(code, buff.Name, buff.Summary, buff.Effect, Catalogued: true);
        }

        int? skillCode = NormalizeBuffSkillCode(code);
        if (skillCode == null) return null;
        Skill? skill = _dm.Skill(skillCode.Value);
        if (skill == null) return null;
        string? name = skill.Name;
        if (DataManager.IsPlaceholderBuffName(name)) return null;
        return new BuffDisplay(skillCode.Value, name!, null, null, Catalogued: false);
    }

    /// <summary>
    /// Buff/debuff uptime for one entity, one row per (base skill, name, caster).
    /// </summary>
    /// <remarks>
    /// A skill fires several runtime codes at once — the buff it puts on the caster and the debuff it puts on the
    /// target, each with its own rank variants. The ENTITY already separates buff from debuff (a player's list is
    /// what landed on them; the boss's list is what landed on it), so this collapses everything one skill did to
    /// one entity into a single row. Grouping is deliberately NOT by:
    /// <list type="bullet">
    /// <item>runtime code — 격노 폭발 puts 113900071 + 113900072 on its target, so each rank became its own row.</item>
    /// <item>name alone — 지연 피해 is five unrelated skills across five bases (궁성 14170000, 마도성 15050000/15320000,
    /// 정령성 16300000/16330000) that merely share a display name.</item>
    /// <item>base alone — one base carries differently-named effects (16300000 holds 4원소, 피해 내성 감소, 지연 피해).</item>
    /// </list>
    /// The rate is the union of every member interval, not a sum or a max — applications overlap.
    /// </remarks>
    public List<OperatingData> GetBuffOperatingRate(int uid, long start, long end)
    {
        long totalDuration = end - start;
        if (totalDuration <= 0) return [];

        var groups = _dm.BattleBuff(uid, start, end)
            .Where(b => !_dm.IsBuffBlacklisted(b.SkillCode))
            .Select(useBuff => (Display: ResolveBuffDisplay(useBuff.SkillCode), UseBuff: useBuff))
            .Where(x => x.Display != null)
            .Select(x => (D: x.Display!.Value, U: x.UseBuff, Base: DataManager.BuffBaseCode(x.UseBuff.SkillCode)))
            .GroupBy(x => (x.Base, x.D.Name, x.U.ActorId));

        var result = new List<OperatingData>();
        foreach (var group in groups)
        {
            (int baseCode, string name, int actorId) = group.Key;
            long covered = BuffUptime.CoveredMs(group.Select(x => (x.U.BuffStart, x.U.BuffEnd)), start, end);
            double rate = (double)covered / totalDuration * 100.0;

            BuffDisplay display = RepresentativeOf(group, start, end);
            // The raw packet code, not the display code: the fallback path collapses a 9-digit job buff to its
            // 8-digit base, which would then read as "not a job buff" and misclassify a self-buff as 그 외.
            int jobPrefix = group
                .Select(x => DataManager.IsJobBuffCode(x.U.SkillCode) ? x.U.SkillCode / 10_000_000 : 0)
                .FirstOrDefault(p => p != 0);

            result.Add(new OperatingData(
                display.Code, name, display.Summary, display.Effect, rate, actorId, baseCode, jobPrefix));
        }

        return result;
    }

    /// <summary>The member that best stands for the merged group: catalogued codes first (they resolve an icon and
    /// a stats-web buff value), then the longest-running one, then the lowest code so the choice is stable.</summary>
    private static BuffDisplay RepresentativeOf(
        IEnumerable<(BuffDisplay D, UseBuff U, int Base)> group, long start, long end) =>
        group
            .GroupBy(x => x.D.Code)
            .OrderByDescending(g => g.First().D.Catalogued)
            .ThenByDescending(g => BuffUptime.CoveredMs(g.Select(x => (x.U.BuffStart, x.U.BuffEnd)), start, end))
            .ThenBy(g => g.Key)
            .First()
            .First().D;

    /// <summary>The buff/debuff timeline for one entity: the same grouping as
    /// <see cref="GetBuffOperatingRate"/> (base skill + name + caster, blacklist- and placeholder-filtered), but
    /// each group keeps its MERGED applied spans instead of a single rate — the icon-lane source for the DPS
    /// graph. Blacklist/placeholder handling is kept identical to <see cref="GetBuffOperatingRate"/> so the lane
    /// never shows an icon the uptime tab wouldn't.</summary>
    public List<BuffTimeline> GetBuffIntervals(int uid, long start, long end)
    {
        if (end <= start) return [];

        var groups = _dm.BattleBuff(uid, start, end)
            .Where(b => !_dm.IsBuffBlacklisted(b.SkillCode))
            .Select(useBuff => (Display: ResolveBuffDisplay(useBuff.SkillCode), UseBuff: useBuff))
            .Where(x => x.Display != null)
            .Select(x => (D: x.Display!.Value, U: x.UseBuff, Base: DataManager.BuffBaseCode(x.UseBuff.SkillCode)))
            .GroupBy(x => (x.Base, x.D.Name, x.U.ActorId));

        var result = new List<BuffTimeline>();
        foreach (var group in groups)
        {
            (int baseCode, string name, int actorId) = group.Key;
            List<(long Start, long End)> spans =
                BuffUptime.MergeIntervals(group.Select(x => (x.U.BuffStart, x.U.BuffEnd)), start, end);
            if (spans.Count == 0) continue;

            BuffDisplay display = RepresentativeOf(group, start, end);
            // Same raw-code job prefix as GetBuffOperatingRate: the display code may have collapsed a 9-digit
            // job buff to its 8-digit base, which would read as "not a job buff".
            int jobPrefix = group
                .Select(x => DataManager.IsJobBuffCode(x.U.SkillCode) ? x.U.SkillCode / 10_000_000 : 0)
                .FirstOrDefault(p => p != 0);

            result.Add(new BuffTimeline(display.Code, name, actorId, baseCode, jobPrefix, spans));
        }

        return result;
    }

    /// <summary>The per-second damage series for one entity over <c>[start, end]</c>: a dense <c>long[]</c>
    /// indexed by whole-second offset from <paramref name="start"/> (element i = damage in the i-th second),
    /// re-based from the absolute-second accumulator. Buckets outside the window fold into the nearest edge so
    /// the series always sums to the entity's total damage. Empty when the window is non-positive or the entity
    /// dealt no damage.</summary>
    public long[] GetDpsSeries(int uid, long start, long end)
    {
        if (end <= start) return [];
        if (!_cachedDpsBuckets.TryGetValue(uid, out Dictionary<long, long>? buckets) || buckets.Count == 0)
        {
            return [];
        }

        long startSecond = start / 1000L;
        long endSecond = end / 1000L;
        int length = (int)(endSecond - startSecond) + 1;
        if (length <= 0) return [];

        var series = new long[length];
        foreach (KeyValuePair<long, long> bucket in buckets)
        {
            int idx = (int)Math.Clamp(bucket.Key - startSecond, 0, length - 1);
            series[idx] += bucket.Value;
        }

        return series;
    }

    private Dictionary<int, long[]> BuildDpsSeries(DpsReport data)
    {
        var result = new Dictionary<int, long[]>();
        if (data.BattleEnd <= data.BattleStart) return result;
        foreach (User user in data.Contributors)
        {
            long[] series = GetDpsSeries(user.Id, data.BattleStart, data.BattleEnd);
            if (series.Length > 0) result[user.Id] = series;
        }

        return result;
    }

    private Dictionary<int, List<BuffTimeline>> BuildBuffIntervals(DpsReport data)
    {
        var result = new Dictionary<int, List<BuffTimeline>>();
        if (data.BattleEnd <= data.BattleStart) return result;
        foreach (User user in data.Contributors)
        {
            result[user.Id] = GetBuffIntervals(user.Id, data.BattleStart, data.BattleEnd);
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
        // Built here, BEFORE the SaveBattleLog call below prunes the buff repository (DataManager.PruneBefore):
        // GetBuffIntervals reads that same repo, so a post-prune snapshot would under-count. GetDpsSeries reads
        // the still-live _cachedDpsBuckets (cleared only when the next battle starts).
        Dictionary<int, long[]> dpsSeries = BuildDpsSeries(_recentData);
        Dictionary<int, List<BuffTimeline>> buffIntervals = BuildBuffIntervals(_recentData);

        _recentSkillDetails = skillDetails;
        _recentBuffRates = buffRates;
        _recentBossBuffRates = bossBuffRates;

        // Freeze the rates onto the live report too, so the post-battle detail (which holds this same
        // _recentData) reads the snapshot instead of recomputing against the now-pruned buff repository.
        _recentData.BuffRates = buffRates;
        _recentData.BossBuffRates = bossBuffRates;
        _recentData.DpsSeries = dpsSeries;
        _recentData.BuffIntervals = buffIntervals;

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

    /// <summary>허수아비 DPS 초기화: clear ONLY the live dummy report so the next hit re-tests immediately. Unlike
    /// <see cref="ResetDataStorage"/> it never saves the current run (a dummy run is not history) and unlike the
    /// soft reset (<see cref="DataManager.ResetBattleRecords"/>) it does NOT flush saved history, recognized
    /// users, the mob map, or the party roster — only the in-flight packets + live report are dropped, and the
    /// duration cutoff latch is re-armed. Runs on the consumer thread (via MeterEngine.RequestDummyReset).</summary>
    public void ResetDummyBattle()
    {
        _dm.FlushPacket();          // drop in-flight packets, set CurrentTarget = -1 (FlushPacket does NOT bump revision)
        _dm.ResetDummyCutoff();     // re-arm: the next dummy hit opens a fresh window
        _currentTarget = -1;
        _currentBattleRevision = _dm.CurrentBattleRevision(); // stay in lockstep (do NOT zero — a mismatch reads as a restart)
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

    /// <summary>
    /// Soft reset for the user "초기화" button: clears the live report + cached battle and the saved history
    /// (via <see cref="DataManager.ResetBattleRecords"/>) but KEEPS recognized characters/executor, the
    /// mob-instance map, and the party roster, so combat info still appears on the next pull inside a dungeon
    /// with no zone reload. Mirrors <see cref="HardReset"/> exactly except it does not wipe users. The
    /// in-progress battle is intentionally NOT saved (the reset is a deliberate full-ledger wipe).
    /// </summary>
    public void ResetKeepingCharacters()
    {
        _dm.ResetBattleRecords();
        _streamResetCallback?.Invoke();
        _currentTarget = -1;
        _currentBattleRevision = 0; // lockstep with DataManager._battleRevision = 0 (else GetDps mis-detects a restart)
        _recentData = new DpsReport();
        _recentSkillDetails = new();
        _recentBuffRates = new();
        _recentBossBuffRates = [];
        _recentDataSaved = false;
        ResetCache();
    }
}

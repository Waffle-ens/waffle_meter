using WaffleMeter.Capture;

namespace WaffleMeter.Data;

/// <summary>
/// Verbatim port of the parts of Kotlin <c>DataManager</c> that the DPS pipeline needs: the
/// reference catalogs (mob/skill/buff/blacklist), the runtime repositories, the battle state
/// machine (start/end/dummy), and the packet store. Implements <see cref="ICaptureGameData"/> so
/// the capture parser can drive it directly.
///
/// Kotlin's DataManager is a singleton <c>object</c>; here it is an instance (one per replay/app).
/// Time is read through <see cref="Clock"/> (default wall clock) — set a simulated clock to replay a
/// recorded corpus deterministically, exactly like the Kotlin clock seam.
///
/// Not ported (irrelevant to DPS numbers): the raw-packet logging buffer, the official character API
/// (network) — <see cref="RequestOfficialCharacterLookup"/> is a no-op, matching a no-network run.
/// </summary>
public sealed class DataManager : ICaptureGameData
{
    // Death-rattle window: after a boss dies the game may emit a residual battle-start toggle (0x8D21) on the
    // corpse — swallow only that brief tail. A genuine re-pull happens well after this, so it is never blocked
    // here; and a re-pull whose toggle DOES land inside the window is recovered by _pendingStart (see below).
    // (Was 30 min — far longer than any death rattle — which froze the meter on the previous battle when a
    // re-pull's start-toggle arrived before the boss's fresh HP packet. Upstream Kotlin has no such guard.)
    private const long EndedBattleStartIgnoreMs = 3_000L;

    // A swallowed re-pull start (see _pendingStart) is replayed only if the boss's first HP>0 packet arrives
    // within this window of the suppressed toggle — long enough to cover any realistic in-combat HP delay, short
    // enough that a much-later HP broadcast on the same instance id can't trigger a spurious empty battle.
    private const long PendingStartTtlMs = 60_000L;
    private const long DummyTimeoutMs = 5000L;

    private readonly record struct EndedBattle(int? MobCode, long EndedAt);

    private readonly Dictionary<int, Mob> _mobs = new();
    private readonly HashSet<int> _buffBlacklist = new();

    private readonly PacketRepository _packetRepository = new();
    private readonly UserRepository _userRepository = new();
    private readonly MobIdRepository _mobIdRepository = new();
    private readonly MobHpRepository _mobHpRepository = new();
    private readonly SummonRepository _summonRepository = new();
    private readonly UseBuffRepository _useBuffRepository = new();
    private readonly BattleLogRepository _battleLogRepository = new();
    private readonly SkillRepository _skillRepository = new();
    private readonly BuffRepository _buffRepository = new();

    private long _resetEpoch;
    private long _battleRevision;
    private readonly Dictionary<int, EndedBattle> _recentlyEndedBattles = new();
    private int? _activeBattleMobCode;
    // A StartBattle the corpse-guard suppressed (a re-pull whose start-toggle beat the boss's fresh HP packet).
    // Replayed the instant the boss next reports HP>0 (within PendingStartTtlMs), so a genuine re-pull never
    // stays frozen on the previous battle even when the game emits no second start-toggle (see StartBattle +
    // MobHp). At = when it was suppressed, so a stale pending can't fire a spurious battle much later.
    private (int MobId, int? MobCode, long At)? _pendingStart;
    private long _lastDummyHitTime;
    private readonly Dictionary<int, long> _officialLookupAttempts = new();
    // Latest full party/raid roster snapshot (0x9702 packet): each member's (nickname, server) + when it
    // arrived. Matched to known uids on demand for the pre-combat party preview (see PartyRoster).
    private readonly List<(string Nickname, int Server, int Slot)> _partyRoster = new();
    private long _partyRosterAtMs;

    /// <summary>Injectable clock (default wall clock; app behavior unchanged). Mirrors the Kotlin seam.</summary>
    public Func<long> Clock { get; set; } = () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>
    /// Official-site lookup (Kotlin used a global object). Left null offline/in replay so enrichment
    /// is a no-op and the DPS golden is unchanged; the live app injects WaffleMeter.Services.OfficialCharacterLookup.
    /// </summary>
    public IOfficialCharacterLookup? OfficialLookup { get; set; }

    public long CurrentEpoch() => _resetEpoch;
    public long CurrentBattleRevision() => _battleRevision;

    // ---- reference catalogs ----

    public void LoadMobs(IReadOnlyDictionary<int, Mob> mobs)
    {
        foreach (KeyValuePair<int, Mob> kv in mobs)
        {
            _mobs[kv.Key] = kv.Value;
        }
    }

    public void LoadSkills(IEnumerable<Skill> skills)
    {
        foreach (Skill s in skills)
        {
            _skillRepository.Save(s.Code, s);
        }
    }

    public void LoadBuffs(IEnumerable<Buff> buffs)
    {
        foreach (Buff b in buffs)
        {
            _buffRepository.Save(b);
        }
    }

    public void LoadBuffBlacklist(IEnumerable<int> codes)
    {
        foreach (int c in codes)
        {
            _buffBlacklist.Add(c);
        }
    }

    public bool IsBuffBlacklisted(int code) => _buffBlacklist.Contains(code);

    // ---- ICaptureGameData (parser-facing) ----

    public Mob? GetMob(int code) => _mobs.GetValueOrDefault(code);
    public int? GetMobId(int instanceId) => _mobIdRepository.Get(instanceId)?.Code;

    public void SaveMobId(int mid, int code)
    {
        int? previous = GetMobId(mid);
        if (previous != null && previous != code)
        {
            _recentlyEndedBattles.Remove(mid);
            if (_pendingStart?.MobId == mid)
            {
                _pendingStart = null; // this instance id was recycled to a different mob — drop the stale retry
            }
        }

        _mobIdRepository.Save(mid, code);
    }

    public bool SkillExists(long code) => _skillRepository.Exist(code);

    // ---- mob / hp ----

    public Mob? Mob(int mobCode) => _mobs.GetValueOrDefault(mobCode);
    public Skill? Skill(long code) => _skillRepository.Get(code);
    public Buff? Buff(int code) => _buffRepository.Get(code);

    public int? MobHp(int mobId) => _mobHpRepository.Get(mobId);

    public void MobHp(int mobId, int mobHp)
    {
        _mobHpRepository.Set(mobId, mobHp);
        if (mobHp > 0)
        {
            _recentlyEndedBattles.Remove(mobId);
            SaveMobMaxHp(mobId, mobHp);

            // A re-pull whose start-toggle we swallowed as a death-rattle: the boss now shows HP, so honor that
            // start (the game may not re-send the toggle). The recently-ended entry was just removed above, so
            // StartBattle no longer suppresses; the CurrentTarget<=0 guard keeps it from stomping a live battle.
            if (_pendingStart is { } ps && ps.MobId == mobId && ps.MobCode == GetMobId(mobId) && CurrentTarget() <= 0)
            {
                _pendingStart = null; // consumed either way, so a stale pending can't linger and fire later
                if (Clock() - ps.At <= PendingStartTtlMs)
                {
                    StartBattle(mobId);
                }
            }
        }
    }

    public int? MobMaxHp(int mobId)
    {
        int? maxHp = _mobIdRepository.Get(mobId)?.MaxHp;
        return maxHp is > 0 ? maxHp : null;
    }

    public void SaveMobMaxHp(int mid, int maxHp) => _mobIdRepository.SaveMaxHp(mid, maxHp);

    public bool IsMobInstance(int id) => _mobIdRepository.Exist(id);

    // ---- summon ----

    public void SaveSummon(int summonId, int summonerId) => _summonRepository.Save(summonId, summonerId);
    public int? SummonerId(int summonId) => _summonRepository.Get(summonId);

    // ---- user ----

    public User? User(int uid) => _userRepository.Get(uid);
    public int ExecutorId() => _userRepository.Executor();

    /// <summary>Raised when the connected character is switched to a DIFFERENT character (a real char
    /// switch — different nickname, or a different known server — NOT the same character re-instancing
    /// under a fresh uid on a zone load). Lets the UI drop its own per-character derived preview state
    /// (the recent-combat party tracker) so the previous character doesn't linger as a stale idle row.
    /// Fires on the packet-consumer thread.</summary>
    public event Action? ExecutorIdentityChanged;

    // ---- aether (오드) resource, the local player's balance shown next to the recognized character ----
    // Written on the packet-consumer thread, read (composite) on the UI thread → guard so a read can't
    // observe a torn base/bonus/total mid-update.
    private readonly object _aetherGate = new();
    private int _aetherBase;
    private int _aetherBonus;
    private int _aetherTotal;
    private bool _aetherHasValue;

    /// <summary>Raised (packet-consumer thread) when the aether balance changes, so the overlay can refresh.</summary>
    public event Action? AetherStatusChanged;

    /// <summary>The local player's current aether balance, or (0,0,false) until one has been seen.</summary>
    public (int Base, int Bonus, int Total, bool HasValue) CurrentAether
    {
        get { lock (_aetherGate) { return (_aetherBase, _aetherBonus, _aetherTotal, _aetherHasValue); } }
    }

    public void SaveAetherStatus(bool split, int baseVal, int bonus, int total)
    {
        lock (_aetherGate)
        {
        if (split)
        {
            _aetherBase = baseVal;
            _aetherBonus = bonus;
        }
        else
        {
            // Total-only: back-compute base/bonus from the previous split by absorbing the delta into base
            // first, then bonus (matching the game's spend order). The total is always authoritative.
            int delta = total - _aetherTotal;
            if (delta >= 0)
            {
                _aetherBase += delta;
            }
            else
            {
                int drop = -delta;
                int fromBase = Math.Min(_aetherBase, drop);
                _aetherBase -= fromBase;
                _aetherBonus = Math.Max(0, _aetherBonus - (drop - fromBase));
            }
        }

        _aetherTotal = total;
        _aetherHasValue = true;
        } // _aetherGate

        AetherStatusChanged?.Invoke(); // outside the lock (avoid holding it during event dispatch)
    }

    private void ClearAetherStatus()
    {
        lock (_aetherGate)
        {
            if (!_aetherHasValue && _aetherBase == 0 && _aetherBonus == 0 && _aetherTotal == 0)
            {
                return; // nothing to clear — skip the change event
            }

            _aetherBase = _aetherBonus = _aetherTotal = 0;
            _aetherHasValue = false;
        }

        AetherStatusChanged?.Invoke();
    }

    // ---- field-boss respawn timers (boss code -> target Unix-ms), from the 0x9101 broadcast ----
    // Written on the packet-consumer thread, read (snapshot) on the UI thread → guard with a lock.
    private readonly Dictionary<int, long> _fieldBossTimers = new();
    private readonly object _fieldBossGate = new();

    /// <summary>Raised (packet-consumer thread) when the field-boss timer table changes.</summary>
    public event Action? FieldBossTimersChanged;

    /// <summary>A thread-safe snapshot of the current field-boss respawn timers (code -> target Unix-ms).</summary>
    public IReadOnlyDictionary<int, long> CurrentFieldBossTimers
    {
        get { lock (_fieldBossGate) { return new Dictionary<int, long>(_fieldBossTimers); } }
    }

    public void SaveFieldBossTimers(IReadOnlyList<(int Code, long TargetMs)> timers)
    {
        bool changed = false;
        lock (_fieldBossGate)
        {
            foreach ((int code, long targetMs) in timers)
            {
                if (!_fieldBossTimers.TryGetValue(code, out long existing) || existing != targetMs)
                {
                    _fieldBossTimers[code] = targetMs;
                    changed = true;
                }
            }
        }

        if (changed)
        {
            FieldBossTimersChanged?.Invoke();
        }
    }

    public User? FindUserByNicknameAndServer(string nickname, int server) =>
        _userRepository.FindByNicknameAndServer(nickname, server);

    public void SavePartyRoster(IReadOnlyList<(string Nickname, int Server, int Slot)> members)
    {
        _partyRoster.Clear();
        _partyRoster.AddRange(members);
        _partyRosterAtMs = Clock();
    }

    /// <summary>Known Users for the current party/raid roster — the 0x9702 snapshot matched to uids by
    /// name+server — executor first then power desc. Empty when no roster is known, or when the last
    /// snapshot is older than <paramref name="withinMs"/> (the party was left / it is stale). This is the
    /// authoritative pre-combat party source (the roster packet fires on party formation, before combat).</summary>
    public IReadOnlyList<User> PartyRoster(long withinMs)
    {
        if (_partyRoster.Count == 0 || Clock() - _partyRosterAtMs > withinMs)
        {
            return Array.Empty<User>();
        }

        int exec = _userRepository.Executor();
        User? execUser = exec > 0 ? _userRepository.Get(exec) : null;
        var result = new List<User>();
        foreach ((string nickname, int server, int _) in _partyRoster)
        {
            // Prefer the LIVE executor for the self's roster entry: the self re-registers under a fresh uid each
            // zone load (0x3633) leaving stale name+server duplicates, so FindByNicknameAndServer (FirstOrDefault)
            // would otherwise return a stale self uid (Id != exec, IsExecutor=false) and the preview's own row
            // would fail self-recognition. Mirrors ResolveRosterMemberUid so the data layer is self-consistent.
            User? user = execUser != null
                         && string.Equals(execUser.Nickname, nickname, StringComparison.Ordinal)
                         && execUser.Server == server
                ? execUser
                : _userRepository.FindByNicknameAndServer(nickname, server);
            if (user != null && !string.IsNullOrWhiteSpace(user.Nickname))
            {
                result.Add(user);
            }
        }

        return result
            .OrderByDescending(u => u.Id == exec)
            .ThenByDescending(u => u.Power)
            .ToList();
    }

    public void SaveUserPower(int uid, int power)
    {
        if (power <= 0) return;
        User? user = _userRepository.Get(uid);
        if (user == null) return;
        if (user.Power != power)
        {
            user.Power = power;
            _userRepository.Save(uid, user);
        }
    }

    /// <summary>Returns the User for <paramref name="uid"/>, creating and persisting a bare one (no
    /// nickname/server/job/power) if none exists yet. Lets a damaging actor whose identity packet hasn't
    /// arrived — notably the executor on 난입 (mid-join), whose own-nickname 0x3633 comes late — still get a
    /// row instead of being dropped; the SAME object is enriched in place when SaveNickname / the official
    /// lookup later arrives, so naming, self-color, and upload reconcile automatically.</summary>
    public User EnsureUser(int uid)
    {
        User? existing = _userRepository.Get(uid);
        if (existing != null)
        {
            return existing;
        }

        var user = new User(uid);
        _userRepository.Save(uid, user);
        return user;
    }

    public void SaveNickname(int uid, string nickname, bool isExecutor, int server, int jobByte)
    {
        JobClass? job = JobClassInfo.ConvertFromCode(jobByte);
        User? user = _userRepository.Get(uid);
        if (user == null)
        {
            user = new User(uid, nickname, server, null, isExecutor);
            _userRepository.Save(uid, user);
        }
        else if (!string.IsNullOrWhiteSpace(user.Nickname)
                 && !string.IsNullOrWhiteSpace(nickname)
                 && !string.Equals(user.Nickname, nickname, StringComparison.Ordinal))
        {
            // Entity ids are reused across pulls (DpsCalculator.ResolveActor relies on it). When a reused
            // id is taken over by a DIFFERENT player (its stored non-blank nickname changes), the prior
            // player's job is still locked on this object and TrySetJob's monotonic first-write-wins would
            // keep it, mislabeling the new occupant with the old class icon. Reset job/power provenance so
            // the new player's jobByte / own skill / official lookup can set the correct values. Gated
            // strictly on a nickname change, so the normal repeated-probe (same name -> same player) path
            // that own-skill correction depends on is untouched.
            user.Job = null;
            user.JobSource = JobProvenance.None;
            user.Power = 0;
            _officialLookupAttempts.Remove(uid);
        }

        user.Nickname = nickname;
        if (server > 0)
        {
            user.Server = server;
        }

        // Snapshot jobByte (ConvertFromCode) is an Authoritative source (the byte right after a probed
        // nickname): it fills a missing job and isn't overwritten by a later same-tier source (e.g. the
        // official lookup), but the player's own job-locked damage skills (OwnSkill) outrank it and can
        // correct a mis-read byte. First write wins within the tier.
        user.TrySetJob(job, JobProvenance.Authoritative);

        _userRepository.Save(uid, user);
        if (isExecutor)
        {
            SaveExecutorId(uid);
        }
    }

    private void SaveExecutorId(int uid)
    {
        int executor = _userRepository.Executor();
        if (executor != uid)
        {
            // Capture both identities BEFORE flipping the flag so we can tell a real character SWITCH (a
            // different character connects) from the same character RE-INSTANCING under a fresh uid on a
            // zone/instance load. The new executor's nickname is already set (SaveNickname writes it before
            // calling here); the prior executor User is still present (the 3-cap eviction never removes it).
            User? oldExec = executor != 0 ? _userRepository.Get(executor) : null;
            User? newExec = _userRepository.Get(uid);

            if (executor != 0)
            {
                oldExec!.IsExecutor = false;
            }

            _userRepository.Executor(uid);
            newExec!.IsExecutor = true;

            // A character switch (콘팡 -> 마이농) must drop the previous character's pre-combat preview state
            // — the 0x9702 party snapshot here, and the UI-side recent-combat tracker via the event below — so
            // the previous character doesn't linger as a stale idle 0/s row under the new character. A
            // same-character re-instance (same name+server, fresh uid on a zone load) KEEPS it: the party about
            // to form in the new zone is still ours. Both nicknames must be non-blank (an unknown identity never
            // triggers a clear), and the server is compared ONLY when both are known (>0): a truncated 0x3633
            // leaves Server=-1, which must not read as a cross-server switch (that would false-clear a
            // legitimate dungeon party preview on every truncated re-instance).
            bool identityChanged = false;
            if (oldExec != null && newExec != null
                && !string.IsNullOrWhiteSpace(oldExec.Nickname)
                && !string.IsNullOrWhiteSpace(newExec.Nickname))
            {
                bool nameChanged = !string.Equals(oldExec.Nickname, newExec.Nickname, StringComparison.Ordinal);
                bool serverChanged = oldExec.Server > 0 && newExec.Server > 0 && oldExec.Server != newExec.Server;
                identityChanged = nameChanged || serverChanged;
            }

            if (identityChanged)
            {
                _partyRoster.Clear();
                _partyRosterAtMs = 0;
                ClearAetherStatus(); // the aether balance is the previous character's — drop it on a real switch
                ClearOwnerBuffs();   // the previous character's buffs, likewise
                ExecutorIdentityChanged?.Invoke();
            }
        }
    }

    public void RequestOfficialCharacterLookup(int uid)
    {
        User? user = _userRepository.Get(uid);
        if (user == null)
        {
            return;
        }

        RequestOfficialCharacterLookup(uid, user.Nickname, user.Server, user.Job);
    }

    public void RequestOfficialCharacterLookup(
        int uid,
        string? nickname,
        int server,
        JobClass? job,
        Action<OfficialCharacterInfo>? onResult = null)
    {
        if (OfficialLookup == null)
        {
            return; // no network (replay / headless without enrichment)
        }

        if (string.IsNullOrWhiteSpace(nickname) || server <= 0)
        {
            return;
        }

        long now = Clock();
        if (_officialLookupAttempts.TryGetValue(uid, out long previous) && now - previous < 10 * 60 * 1000L)
        {
            return;
        }

        if (uid > 0)
        {
            _officialLookupAttempts[uid] = now;
        }

        OfficialLookup.LookupAsync(nickname, server, job, info =>
        {
            ApplyOfficialCharacterInfo(uid, info);
            onResult?.Invoke(info);
        });
    }

    public OfficialCharacterInfo? ResolveOfficialCharacterInfo(int uid, string? nickname, int server, JobClass? job)
    {
        if (OfficialLookup == null)
        {
            return null;
        }

        OfficialCharacterInfo? info = OfficialLookup.LookupBlocking(nickname, server, job);
        if (info == null)
        {
            return null;
        }

        ApplyOfficialCharacterInfo(uid, info);
        return info;
    }

    private void ApplyOfficialCharacterInfo(int uid, OfficialCharacterInfo info)
    {
        User? existing = uid > 0 ? _userRepository.Get(uid) : null;
        if (existing != null)
        {
            if (string.IsNullOrWhiteSpace(existing.Nickname))
            {
                existing.Nickname = info.Nickname;
            }

            if (existing.Server <= 0)
            {
                existing.Server = info.Server;
            }

            // Official pcId is Authoritative (same tier as the snapshot jobByte; first write wins, so it
            // doesn't clobber a job the live snapshot already set). The player's own job-locked skills
            // (OwnSkill) still win — a short-name lookup can resolve a DIFFERENT same-name character, so live
            // combat evidence is the final arbiter.
            existing.TrySetJob(info.Job, JobProvenance.Authoritative);

            if (existing.Power <= 0 && info.Power > 0)
            {
                existing.Power = info.Power;
            }

            _userRepository.Save(uid, existing);
            return;
        }

        var pending = new User(uid, info.Nickname, info.Server, info.Job, power: info.Power)
        {
            JobSource = info.Job != null ? JobProvenance.Authoritative : JobProvenance.None,
        };
        _userRepository.SavePending(pending);
    }

    // ---- buff ----

    public void SaveUseBuff(int uid, UseBuff useBuff) => _useBuffRepository.Save(uid, useBuff);

    public void SaveUseBuff(int uid, int skillCode, long buffStart, long buffEnd, long duration, int actorId)
    {
        SaveUseBuff(uid, new UseBuff(skillCode, buffStart, buffEnd, duration, actorId));

        // Live combat-assist overlay: track buffs currently ON the local player (recipient == executor), so
        // the overlay can show what's active + how long is left. Kept separate from the uptime repository.
        int owner = _userRepository.Executor();
        if (owner != 0 && uid == owner && !IsBuffBlacklisted(skillCode))
        {
            lock (_ownerBuffGate)
            {
                _ownerBuffs[skillCode] = (buffEnd, actorId);
            }

            LiveBuffsChanged?.Invoke();
        }
    }

    // ---- live owner-buff store (for the combat-assist overlay) ----
    private readonly Dictionary<int, (long End, int Actor)> _ownerBuffs = new(); // skill code -> (expiry, applier)
    private readonly object _ownerBuffGate = new();

    /// <summary>Raised when a buff on the local player is applied/refreshed.</summary>
    public event Action? LiveBuffsChanged;

    /// <summary>The buffs currently active on the local player at <paramref name="nowMs"/>, longest remaining
    /// first. <c>ByOther</c> = applied by someone else (not the local player).</summary>
    public IReadOnlyList<(int Code, string Name, long RemainingMs, bool ByOther)> ActiveOwnerBuffs(long nowMs)
    {
        int owner = _userRepository.Executor();
        var result = new List<(int, string, long, bool)>();
        lock (_ownerBuffGate)
        {
            foreach (KeyValuePair<int, (long End, int Actor)> kv in _ownerBuffs)
            {
                if (kv.Value.End <= nowMs)
                {
                    continue; // expired
                }

                string name = Buff(kv.Key)?.Name ?? Skill(kv.Key)?.Name ?? $"버프 {kv.Key}";
                result.Add((kv.Key, name, kv.Value.End - nowMs, owner != 0 && kv.Value.Actor != owner));
            }
        }

        return result.OrderByDescending(r => r.Item3).ToList();
    }

    private void ClearOwnerBuffs()
    {
        lock (_ownerBuffGate)
        {
            _ownerBuffs.Clear();
        }
    }

    public void SaveMobHp(int instanceId, int hp) => MobHp(instanceId, hp);

    public List<UseBuff> BattleBuff(int uid, long start, long end) => _useBuffRepository.FindOverlapping(uid, start, end);

    // ---- packet store ----

    public List<ParsedDamagePacket>? BattleData(int targetId) => targetId <= 0 ? null : _packetRepository.Get(targetId);

    public PacketWindow BattleDataSince(int targetId, long sequence) =>
        targetId <= 0 ? new PacketWindow([], sequence, false, 0) : _packetRepository.GetWindow(targetId, sequence);

    public void FlushPacket()
    {
        _packetRepository.Flush();
        _packetRepository.CurrentTarget(-1);
        _packetRepository.FlushBattleTime();
        _activeBattleMobCode = null;
        _lastDummyHitTime = 0;
    }

    public void SaveDamage(ParsedDamagePacket pdp, long epoch)
    {
        if (_resetEpoch != epoch) return;
        _packetRepository.Save(pdp);
    }

    // ---- battle state machine ----

    public int CurrentTarget() => _packetRepository.CurrentTarget();
    private void SaveCurrentTarget(int targetId) => _packetRepository.CurrentTarget(targetId);
    public long CurrentBattleStart() => _packetRepository.CurrentBattleStart();
    public long CurrentBattleEnd() => _packetRepository.CurrentBattleEnd();
    private void SaveCurrentBattleStart() => _packetRepository.SaveCurrentBattleStart(Clock());
    private void SaveCurrentBattleEnd(long time) => _packetRepository.SaveCurrentBattleEnd(time);

    public bool IsCurrentTargetDummy()
    {
        int current = CurrentTarget();
        if (current <= 0) return false;
        int? mobCode = GetMobId(current);
        return mobCode != null && Mob(mobCode.Value)?.IsDummy == true;
    }

    public void TouchDummyBattle(int mobId, long epoch)
    {
        if (_resetEpoch != epoch) return;
        _lastDummyHitTime = Clock();
        if (CurrentTarget() <= 0)
        {
            SaveCurrentBattleStart();
            SaveCurrentTarget(mobId);
        }
    }

    public void CheckDummyTimeout()
    {
        int current = CurrentTarget();
        if (current <= 0) return;
        if (!IsCurrentTargetDummy()) return;
        if (Clock() - _lastDummyHitTime > DummyTimeoutMs)
        {
            SaveCurrentBattleEnd(_lastDummyHitTime);
            SaveCurrentTarget(-1);
            _lastDummyHitTime = 0;
        }
    }

    public void StartBattle(int mobId)
    {
        int? mobCode = GetMobId(mobId);
        long now = Clock();
        EndedBattle? endedBattle = _recentlyEndedBattles.TryGetValue(mobId, out EndedBattle eb) ? eb : null;
        if (CurrentTarget() <= 0
            && endedBattle != null
            && endedBattle.Value.MobCode == mobCode
            && MobHp(mobId) == 0
            && now - endedBattle.Value.EndedAt <= EndedBattleStartIgnoreMs)
        {
            // Likely a residual post-kill toggle on the corpse — don't restart now. But remember the intent: if
            // the boss next reports HP>0 (a real re-pull/respawn), MobHp replays this start so we never freeze.
            _pendingStart = (mobId, mobCode, now);
            return;
        }

        if (CurrentTarget() == mobId
            && CurrentBattleStart() > 0L
            && CurrentBattleEnd() == 0L
            && _activeBattleMobCode == mobCode)
        {
            return;
        }

        _pendingStart = null;
        _recentlyEndedBattles.Remove(mobId);
        _battleRevision++;
        SaveCurrentBattleStart();
        SaveCurrentTarget(mobId);
        _activeBattleMobCode = mobCode;
    }

    public void EndBattle(int mobId)
    {
        if (CurrentTarget() != mobId) return;
        int? mobCode = _activeBattleMobCode ?? GetMobId(mobId);
        SaveCurrentBattleEnd(Clock());
        SaveCurrentTarget(-1);
        _recentlyEndedBattles[mobId] = new EndedBattle(mobCode, Clock());
        _activeBattleMobCode = null;
    }

    // ---- battle log ----

    public DpsLog SaveBattleLog(
        DpsReport data,
        Dictionary<int, Dictionary<string, AnalyzedSkill>> skillDetails,
        Dictionary<int, List<OperatingData>> buffRates,
        List<OperatingData> bossBuffRates)
    {
        var snapshot = new DpsReport
        {
            Contributors = data.Contributors.Select(CopyUser).ToList(),
            BattleStart = data.BattleStart,
            BattleEnd = data.BattleEnd,
            Information = data.Information.ToDictionary(kv => kv.Key, kv => CopyInfo(kv.Value)),
            Target = data.Target is { } t ? new MobInfo(t.Id, t.Mob, t.RemainHp, t.MaxHp) : null,
            Packets = null,
            ExecutorId = ExecutorId(),     // freeze the 본인 uid so a history replay self-colors the own row (CopyUser froze IsExecutor — usually false)
            BuffRates = buffRates,         // frozen so the detail (history replay) matches the web
            BossBuffRates = bossBuffRates,
            SkillDetailsSnapshot = skillDetails, // frozen so the replayed detail's skill table + summary aren't empty
            PartySlots = CurrentPartySlots(data.Contributors), // frozen 0x9702 sub-party slots (1-4/5-8), keyed to the actual battle uids
        };

        var log = new DpsLog
        {
            Report = snapshot,
            SummonMap = new Dictionary<int, int>(_summonRepository.GetAll()),
            Packets = [],
            SkillDetails = skillDetails,
            BuffRates = buffRates,
            BossBuffRates = bossBuffRates,
        };

        _battleLogRepository.Save(log);
        _useBuffRepository.PruneBefore(data.BattleEnd + 1);
        return log;
    }

    public List<(int Index, DpsReport Report)> RecentBattleList()
    {
        var list = new List<(int, DpsReport)>();
        IReadOnlyList<DpsLog> logs = _battleLogRepository.GetAll();
        for (int i = 0; i < logs.Count; i++)
        {
            list.Add((i, logs[i].Report));
        }

        return list;
    }

    public DpsLog? BattleLog(int idx) => _battleLogRepository.Get(idx);

    public void HardReset()
    {
        _resetEpoch++;
        _battleRevision = 0;
        _battleLogRepository.Flush();
        _mobHpRepository.Flush();
        _mobIdRepository.Flush();
        _userRepository.Flush();
        _summonRepository.Flush();
        _useBuffRepository.Flush();
        _packetRepository.Flush();
        _recentlyEndedBattles.Clear();
        _activeBattleMobCode = null;
        _pendingStart = null;
        _lastDummyHitTime = 0;
        _partyRoster.Clear();
        _partyRosterAtMs = 0;
        ClearAetherStatus();
        ClearOwnerBuffs();
    }

    /// <summary>
    /// Soft reset for the user "초기화" button: clears the battle LEDGER (saved history + the in-flight damage
    /// packets) and all battle-lifecycle transients, but PRESERVES every piece of runtime reference state that
    /// the game only re-broadcasts on a zone load — recognized users (incl. the executor), the mob-instance map,
    /// mob HP, the summon map, buff intervals, the party roster, official-lookup throttles, and the catalogs.
    /// This is what makes reset usable inside a dungeon with no map transition: the executor stays recognized
    /// (0x3633 won't re-fire) AND already-spawned bosses keep their instance→code mapping (0x3640 won't re-fire),
    /// so the very next pull still starts a battle and attributes the local player's DPS. Use <see cref="HardReset"/>
    /// only for a true full wipe.
    /// </summary>
    public void ResetBattleRecords()
    {
        _resetEpoch++;            // reject in-flight SaveDamage(pdp, oldEpoch) captured before this reset
        _battleRevision = 0;      // assign 0 (mirror HardReset); DpsCalculator zeroes _currentBattleRevision in lockstep
        _battleLogRepository.Flush(); // clear saved battle history (the 전투 기록 panel)
        _packetRepository.Flush();    // drop the in-flight/old battle's damage packets
        _recentlyEndedBattles.Clear();
        _activeBattleMobCode = null;
        _pendingStart = null;
        _lastDummyHitTime = 0;
        _partyRoster.Clear();     // drop the 0x9702 party snapshot — a stale party (e.g. after leaving the dungeon
        _partyRosterAtMs = 0;     // and returning to town) must not preview on reset; it re-fills on party formation
        // PRESERVE (do NOT flush): _userRepository (recognized chars + executor), _mobIdRepository (boss
        // instance→code, needed for the next StartBattle in a no-respawn dungeon), _mobHpRepository,
        // _summonRepository, _useBuffRepository, _officialLookupAttempts, and the load-once catalogs
        // (_mobs/_skillRepository/_buffRepository/_buffBlacklist).
    }

    /// <summary>Current 0x9702 roster mapped to the uids the stats payload tags (uid -&gt; slot 1-8), frozen into
    /// a saved report (<see cref="SaveBattleLog"/>) so the stats upload can tag each participant's sub-party for
    /// an 8-인 공대 — slots 1-4 = party 1, 5-8 = party 2. Members with slot 0 (header unmatched) or no recognized
    /// uid are skipped; empty for a non-raid / unknown roster (the upload then omits party tags).</summary>
    private Dictionary<int, int> CurrentPartySlots(IReadOnlyList<User> contributors)
    {
        int executorId = _userRepository.Executor();
        User? executor = executorId > 0 ? _userRepository.Get(executorId) : null;

        var slots = new Dictionary<int, int>();
        foreach ((string nickname, int server, int slot) in _partyRoster)
        {
            if (slot <= 0)
            {
                continue;
            }

            int? uid = ResolveRosterMemberUid(nickname, server, executor, contributors);
            if (uid != null)
            {
                slots[uid.Value] = slot;
            }
        }

        return slots;
    }

    /// <summary>Resolve a 0x9702 roster member (name+server) to the uid the stats payload actually tags. The
    /// executor re-registers under a FRESH uid on every zone/instance load (0x3633), but its prior User objects
    /// linger in the repository, so a plain name+server lookup (<see cref="UserRepository.FindByNicknameAndServer"/>
    /// returns FirstOrDefault) often returns a STALE self uid — the slot then keys to a non-participant and the
    /// uploader's own row never gets its slot (the 8-인 공대 sub-party split stays off). The same hazard hits any
    /// party member seen under more than one uid. So resolve against the uids the payload actually tags: first a
    /// battle contributor (the recognized+damaging self and every dealer match here, by their live combat uid),
    /// then the live executor (a recognized self that dealt no damage — keeps its slot for isRaid even if it isn't
    /// among the contributors, and never a stale repository uid), and only then fall back to the repository for a
    /// roster member who didn't deal damage (keeps the party-2 slots present so the sub-party detection still
    /// fires). Contributor-first means a same-name dealer always wins over a possibly-lagging executor pointer.</summary>
    private int? ResolveRosterMemberUid(string nickname, int server, User? executor, IReadOnlyList<User> contributors)
    {
        foreach (User contributor in contributors)
        {
            if (string.Equals(contributor.Nickname, nickname, StringComparison.Ordinal) && contributor.Server == server)
            {
                return contributor.Id;
            }
        }

        if (executor != null
            && string.Equals(executor.Nickname, nickname, StringComparison.Ordinal)
            && executor.Server == server)
        {
            return executor.Id;
        }

        return _userRepository.FindByNicknameAndServer(nickname, server)?.Id;
    }

    private static User CopyUser(User u) => new(u.Id, u.Nickname, u.Server, u.Job, u.IsExecutor, u.Power) { JobSource = u.JobSource };

    private static DpsInformation CopyInfo(DpsInformation i) =>
        new(i.Amount, i.Dps, i.Contribution, i.EntireContribution);
}

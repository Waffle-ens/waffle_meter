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
    private const long EndedBattleStartIgnoreMs = 30 * 60 * 1000L;
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
    private long _lastDummyHitTime;
    private readonly Dictionary<int, long> _officialLookupAttempts = new();

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

    public User? FindUserByNicknameAndServer(string nickname, int server) =>
        _userRepository.FindByNicknameAndServer(nickname, server);

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

    public void SaveNickname(int uid, string nickname, bool isExecutor, int server, int jobByte)
    {
        JobClass? job = JobClassInfo.ConvertFromCode(jobByte);
        User? user = _userRepository.Get(uid);
        if (user == null)
        {
            user = new User(uid, nickname, server, null, isExecutor);
            _userRepository.Save(uid, user);
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
            if (executor != 0)
            {
                _userRepository.Get(executor)!.IsExecutor = false;
            }

            _userRepository.Executor(uid);
            _userRepository.Get(uid)!.IsExecutor = true;
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

    public void SaveUseBuff(int uid, int skillCode, long buffStart, long buffEnd, long duration, int actorId) =>
        SaveUseBuff(uid, new UseBuff(skillCode, buffStart, buffEnd, duration, actorId));

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
            return;
        }

        if (CurrentTarget() == mobId
            && CurrentBattleStart() > 0L
            && CurrentBattleEnd() == 0L
            && _activeBattleMobCode == mobCode)
        {
            return;
        }

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
            BuffRates = buffRates,         // frozen so the detail (history replay) matches the web
            BossBuffRates = bossBuffRates,
            SkillDetailsSnapshot = skillDetails, // frozen so the replayed detail's skill table + summary aren't empty
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
        _lastDummyHitTime = 0;
    }

    private static User CopyUser(User u) => new(u.Id, u.Nickname, u.Server, u.Job, u.IsExecutor, u.Power) { JobSource = u.JobSource };

    private static DpsInformation CopyInfo(DpsInformation i) =>
        new(i.Amount, i.Dps, i.Contribution, i.EntireContribution);
}

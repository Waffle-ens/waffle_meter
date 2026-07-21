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
    // Instanced-content (원정/초월/성역) boss mobCode -> category. Loaded from content-types.json; empty until then.
    // Scopes the opt-in "던전 강제 집계" toggle so its bare-actor display bypass fires ONLY on these bosses.
    private readonly Dictionary<int, string> _contentTypes = new();
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

    // 시작 토글은 왔는데 그 엔티티의 mobCode가 아직 없어서 전투를 못 연 건들(entityId -> 토글 시각).
    // 보스 스폰(0x3641)은 교전당 1회뿐이고 전투 중 재방송이 없어, 스폰을 놓치거나 늦게 받으면 그 판은 끝까지
    // 안 열렸다. SaveMobId가 도착하면 여기서 되살린다. 플레이어 엔티티도 이 토글을 쏘지만 플레이어에겐
    // SaveMobId가 오지 않으므로 자연히 만료된다. 무한 증식 방지용으로 개수를 제한한다.
    private readonly Dictionary<int, long> _unresolvedStarts = new();
    private const int UnresolvedStartsCap = 64;

    private long _lastDummyHitTime;
    // Training-dummy (허수아비) test mode. Written by the UI / hotkey thread, read by the consumer thread —
    // volatile is enough (a one-tick staleness is harmless). When OFF, a dummy hit never starts/continues a
    // battle so the meter shows NO combat for it; when ON, a dummy hit drives a live battle exactly like a boss
    // until the chosen duration elapses, at which point _dummyCutoff latches and further hits are ignored until
    // a reset clears it. Mode + duration survive resets; only the cutoff latch is cleared.
    private volatile bool _dummyTestMode;
    private volatile int _dummyDurationSec = 60;
    private bool _dummyCutoff; // consumer-thread only: latched once the duration hard cut has fired
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

    /// <summary>허수아비 test mode: when on, hitting a training dummy (<see cref="Mob.IsDummy"/>) drives a live
    /// battle; when off, dummy hits register no combat. Set live from the UI/hotkey; read on the consumer thread.</summary>
    public bool DummyTestMode { get => _dummyTestMode; set => _dummyTestMode = value; }

    /// <summary>Dummy test run length in seconds; the live battle is hard-cut at this duration. Clamped to &gt; 0
    /// (falls back to 60s).</summary>
    public int DummyDurationSec { get => _dummyDurationSec; set => _dummyDurationSec = value > 0 ? value : 60; }

    private long DummyDurationMs => Math.Max(1, _dummyDurationSec) * 1000L;

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

    /// <summary>Load the instanced-content (원정/초월/성역) boss classification: mobCode -> category.</summary>
    public void LoadContentTypes(IReadOnlyDictionary<int, string> contentTypes)
    {
        foreach (KeyValuePair<int, string> kv in contentTypes)
        {
            _contentTypes[kv.Key] = kv.Value;
        }
    }

    /// <summary>The instanced-content category (expedition/transcendence/sanctuary) of a boss mobCode, or null
    /// when the code isn't a classified 원정/초월/성역 boss.</summary>
    public string? ContentCategory(int mobCode) => _contentTypes.GetValueOrDefault(mobCode);

    /// <summary>True when <paramref name="mobCode"/> is a classified instanced (원정/초월/성역) boss — the scope
    /// gate for the opt-in "던전 강제 집계" display bypass.</summary>
    public bool IsInstancedBoss(int mobCode) => _contentTypes.ContainsKey(mobCode);

    public void LoadBuffs(IEnumerable<Buff> buffs)
    {
        foreach (Buff b in buffs)
        {
            _buffRepository.Save(b);
        }
    }

    public static bool IsPlaceholderBuffName(string? name) =>
        string.IsNullOrWhiteSpace(name) || name.Equals("None", StringComparison.OrdinalIgnoreCase);

    public void LoadBuffBlacklist(IEnumerable<int> codes)
    {
        foreach (int c in codes)
        {
            _buffBlacklist.Add(c);
        }
    }

    public bool IsBuffBlacklisted(int code) => _buffBlacklist.Contains(code);

    // ---- per-job buff picker (combat-assist overlay) ----
    // Names + job for each base skill code (110000000-buff / 11000000-skill share a base), for the picker UI.
    private readonly Dictionary<int, (string Name, string Job)> _buffNames = new();
    // Base skill codes ever seen on the local player / party — the catalog the picker lists.
    private readonly HashSet<int> _observedBuffBases = new();
    // Curated self-buff bases from the bundled catalog (datamine-verified) — listed in the picker even before
    // they're observed, so a buff can be configured up front.
    private readonly HashSet<int> _knownBuffBases = new();
    // Bases that should default to Off (toggle/aura buffs that stay on indefinitely) — applied on first run.
    private readonly HashSet<int> _defaultOffBuffBases = new();
    // Base skill codes the user unchecked — the overlay suppresses these.
    private readonly HashSet<int> _hiddenBuffBases = new();
    // Base skill codes set to voice ("오버레이+음성" or "음성만") — the store keeps these even when hidden so a
    // 음성만 buff still reaches the announce path (hidden AND voice = 음성만).
    private readonly HashSet<int> _voiceBuffBases = new();
    private readonly object _buffPickerGate = new();

    /// <summary>Runtime job-buff code (110000000..199999999) -> its base skill code (8-digit), the key both
    /// the name table and the picker/hidden sets use. Mirrors JoinIcons' buff→base mapping.</summary>
    public static int BuffBaseCode(int code) => code is >= 110_000_000 and <= 199_999_999 ? code / 100_000 * 10_000 : code;

    // 치유성 '대지의 징벌'(17400000)은 대상 몹에게 디버프 '대지의 징벌'을, 본인+파티원에게는 이름이 다른 버프
    // '대지의 축복'을 건다. 둘 다 BuffBaseCode로 접으면 17400000 한 슬롯이 되어 오버레이·음성·picker가
    // 인게임과 다른 이름("대지의 징벌")과 다른 아이콘(바위 가시)을 쓴다. 축복 쪽 abnormal 코드만 별도 표시
    // base로 돌린다 — 17400058은 skills.json에 이미 '대지의 축복'으로 있고 클라에서도 같은 아이콘을 쓰는
    // 실제 코드라, 이름표·아이콘·상세 스킬행이 한 코드로 정합된다. (데이터마인 07-01/07-15 동일 확인)
    private static readonly Dictionary<int, int> BuffDisplayBaseOverrides = new()
    {
        [174000271] = 17400058,
        [174000371] = 17400058,
        [174000571] = 17400058,
    };

    // 인게임에서 서로 중복 적용되지 않는 버프 쌍. 둘 다 활성으로 보이면 지는 쪽을 오버레이에서 감춘다.
    // 코퍼스 실측상 쌍마다 서버 동작이 다르다:
    //  · 노련한 반격↔격앙  : 서버가 둘 다 보낸다(전 지속시간 겹침, p50 10s) → 우리가 반드시 감춰야 한다.
    //  · 보호의 빛↔불패의 진언 : 서버가 중재하고 잔상 최대 2.4초. 인게임 설명문에 "스킬 레벨이 높은 1개만
    //    적용, 동일하면 불패의 진언" 이라고 명문화돼 있어 동률 승자를 고정한다.
    //  · 대지의 축복↔질풍의 권능 : 서버가 새 적용은 막지만(질풍 우선) 이미 걸린 축복을 제거하진 않아
    //    최대 ~20초 잔존 → 질풍이 살아 있으면 축복을 감춘다(고정 승자).
    private readonly record struct ExclusiveBuffPair(int A, int B, int FixedWinner, int TieWinner);

    private static readonly ExclusiveBuffPair[] ExclusiveBuffPairs =
    {
        new(11780000, 12780000, FixedWinner: 0, TieWinner: 0),          // 검성 노련한 반격 ↔ 수호성 격앙
        new(17410000, 18190000, FixedWinner: 0, TieWinner: 18190000),   // 치유성 보호의 빛 ↔ 호법성 불패의 진언
        new(17400058, 18250000, FixedWinner: 18250000, TieWinner: 0),   // 치유성 대지의 축복 ↔ 호법성 질풍의 권능
    };

    /// <summary>오버레이/음성/picker가 쓰는 표시용 base 코드. 한 스킬이 이름이 다른 두 효과를 뿌리는 경우만
    /// <see cref="BuffBaseCode"/>와 갈라진다. 집계·통계 경로는 <see cref="BuffBaseCode"/>를 그대로 쓴다.</summary>
    public static int BuffDisplayBase(int code) =>
        BuffDisplayBaseOverrides.TryGetValue(code, out int mapped) ? mapped : BuffBaseCode(code);

    /// <summary>buff_names.json: base skill code -> (name, job) for the per-job buff picker.</summary>
    public void LoadBuffNames(IEnumerable<(int Code, string Name, string Job)> names)
    {
        lock (_buffPickerGate)
        {
            foreach ((int code, string name, string job) in names)
            {
                _buffNames[code] = (name, job);
            }
        }
    }

    /// <summary>Replace the hidden-buff set (base codes the user unchecked in the picker).</summary>
    public void SetHiddenBuffBases(IEnumerable<int> baseCodes)
    {
        lock (_buffPickerGate)
        {
            _hiddenBuffBases.Clear();
            foreach (int c in baseCodes)
            {
                _hiddenBuffBases.Add(c);
            }
        }
    }

    /// <summary>Replace the voice-buff set (base codes set to "오버레이+음성" or "음성만" in the picker).</summary>
    public void SetVoiceBuffBases(IEnumerable<int> baseCodes)
    {
        lock (_buffPickerGate)
        {
            _voiceBuffBases.Clear();
            foreach (int c in baseCodes)
            {
                _voiceBuffBases.Add(c);
            }
        }
    }

    /// <summary>Seed the observed catalog from a persisted set (so the picker isn't empty on launch).</summary>
    public void SeedObservedBuffBases(IEnumerable<int> baseCodes)
    {
        lock (_buffPickerGate)
        {
            foreach (int c in baseCodes)
            {
                _observedBuffBases.Add(c);
            }
        }
    }

    /// <summary>buff_catalog.json: curated self-buff bases (datamine-verified) that the picker lists even
    /// before they're observed, plus the default-off (toggle/aura) subset. Names are merged into the table.</summary>
    public void LoadBuffCatalog(IEnumerable<(int Code, string Name, string Job)> catalog, IEnumerable<int> defaultOff)
    {
        lock (_buffPickerGate)
        {
            foreach ((int code, string name, string job) in catalog)
            {
                _knownBuffBases.Add(code);
                if (!_buffNames.ContainsKey(code) && !string.IsNullOrEmpty(name))
                {
                    _buffNames[code] = (name, string.IsNullOrEmpty(job) ? "기타" : job);
                }
            }

            foreach (int c in defaultOff)
            {
                _defaultOffBuffBases.Add(c);
            }
        }
    }

    /// <summary>The toggle/aura buffs that should default to Off (applied once on first run by the app).</summary>
    public IReadOnlyCollection<int> DefaultOffBuffBases()
    {
        lock (_buffPickerGate)
        {
            return _defaultOffBuffBases.ToList();
        }
    }

    private bool IsBuffHidden(int runtimeCode)
    {
        lock (_buffPickerGate)
        {
            return _hiddenBuffBases.Contains(BuffDisplayBase(runtimeCode));
        }
    }

    private bool IsBuffVoice(int runtimeCode)
    {
        lock (_buffPickerGate)
        {
            return _voiceBuffBases.Contains(BuffDisplayBase(runtimeCode));
        }
    }

    /// <summary>The picker catalog: every observed job buff, grouped-ready as (base code, name, job, hidden).
    /// Name/job come from the bundled table; unknown codes fall back to the code + "기타".</summary>
    public IReadOnlyList<(int BaseCode, string Name, string Job, bool Hidden)> BuffPickerCatalog()
    {
        lock (_buffPickerGate)
        {
            var bases = new HashSet<int>(_observedBuffBases);
            bases.UnionWith(_knownBuffBases); // curated catalog + anything actually observed
            var list = new List<(int, string, string, bool)>(bases.Count);
            foreach (int b in bases)
            {
                (string name, string job) = _buffNames.TryGetValue(b, out (string Name, string Job) v)
                    ? v
                    : ($"스킬 {b}", "기타");
                list.Add((b, name, job, _hiddenBuffBases.Contains(b)));
            }

            return list;
        }
    }

    /// <summary>The current observed base-code set (for persistence).</summary>
    public IReadOnlyCollection<int> ObservedBuffBases()
    {
        lock (_buffPickerGate)
        {
            return _observedBuffBases.ToList();
        }
    }

    /// <summary>Raised when a new base buff code is observed (so the picker can refresh its catalog).</summary>
    public event Action? BuffCatalogChanged;

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
        PromoteUnresolvedStart(mid, code);
    }

    /// <summary>시작 토글이 mobCode 미해결로 거부됐던 엔티티의 스폰이 이제 도착했다면 그 전투를 되살린다.
    /// 되살릴 때는 <b>원래 토글 시각</b>으로 시작을 스탬프한다 — 지금 시각으로 열면 그 사이의 딜이
    /// ActivePacketCutoff에 걸려 통째로 빠진다(패킷 자체는 타겟별 링버퍼에 남아 있다).
    /// <para>가드: 토글 이후 <see cref="PendingStartTtlMs"/> 이내 + 진행 중인 전투 없음 + 해석된 몹이
    /// 보스이고 허수아비가 아님. 특히 보스 검사를 빼면 잡몹 스폰이 늦게 올 때마다 전투가 열려 전투창이
    /// 절단·분할된다(191M 오염과 같은 계열).</para></summary>
    private void PromoteUnresolvedStart(int mid, int code)
    {
        if (!_unresolvedStarts.TryGetValue(mid, out long toggledAt))
        {
            return;
        }

        _unresolvedStarts.Remove(mid); // 성공하든 말든 한 번만 시도한다
        if (Clock() - toggledAt > PendingStartTtlMs || CurrentTarget() > 0)
        {
            return;
        }

        if (Mob(code) is not { Boss: true, IsDummy: false })
        {
            return;
        }

        StartBattleAt(mid, toggledAt);
    }

    public void RememberUnresolvedBattleStart(int mobId)
    {
        if (mobId <= 0)
        {
            return;
        }

        long now = Clock();
        if (_unresolvedStarts.Count >= UnresolvedStartsCap)
        {
            // 만료분부터 정리하고, 그래도 꽉 차 있으면 가장 오래된 것을 밀어낸다.
            foreach (int stale in _unresolvedStarts.Where(kv => now - kv.Value > PendingStartTtlMs).Select(kv => kv.Key).ToList())
            {
                _unresolvedStarts.Remove(stale);
            }

            if (_unresolvedStarts.Count >= UnresolvedStartsCap)
            {
                _unresolvedStarts.Remove(_unresolvedStarts.OrderBy(kv => kv.Value).First().Key);
            }
        }

        _unresolvedStarts[mobId] = now;
    }

    public bool SkillExists(long code) => _skillRepository.Exist(code);

    // A recognized player = a uid with an observed nickname. Excludes provisional (nickname-less) EnsureUser
    // rows, so the summon-owner fallback validates only against real players.
    public bool IsKnownUser(int uid) => !string.IsNullOrEmpty(_userRepository.Get(uid)?.Nickname);

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

    /// <summary>Seed the aether balance from a persisted value at startup so the badge isn't blank until the
    /// game's next resource broadcast. Overwritten by the first live broadcast; cleared on a character switch.
    /// A live broadcast is never overridden (guarded by <paramref name="onlyIfEmpty"/>).</summary>
    public void RestoreAetherStatus(int baseVal, int bonus, int total, bool onlyIfEmpty = true)
    {
        lock (_aetherGate)
        {
            if (onlyIfEmpty && _aetherHasValue)
            {
                return; // a live value already arrived — don't clobber it with the restored one
            }

            _aetherBase = baseVal;
            _aetherBonus = bonus;
            _aetherTotal = total;
            _aetherHasValue = true;
        }

        AetherStatusChanged?.Invoke();
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

    // ---- shugo-festa key (슈고 페스타 보상 열쇠), shown in the footer next to aether ----
    // Rides the same 0x610x packets as aether (different key byte); same threading + back-compute semantics.
    private readonly object _shugoKeyGate = new();
    private int _shugoKeyBase;
    private int _shugoKeyBonus;
    private int _shugoKeyTotal;
    private bool _shugoKeyHasValue;

    /// <summary>Raised (packet-consumer thread) when the shugo-key count changes, so the overlay can refresh.</summary>
    public event Action? ShugoKeyChanged;

    /// <summary>The local player's current shugo-festa key count, or (0,0,false) until one has been seen.</summary>
    public (int Base, int Bonus, int Total, bool HasValue) CurrentShugoKey
    {
        get { lock (_shugoKeyGate) { return (_shugoKeyBase, _shugoKeyBonus, _shugoKeyTotal, _shugoKeyHasValue); } }
    }

    public void SaveShugoKey(bool split, int baseVal, int bonus, int total)
    {
        lock (_shugoKeyGate)
        {
            if (split)
            {
                _shugoKeyBase = baseVal;
                _shugoKeyBonus = bonus;
            }
            else
            {
                int delta = total - _shugoKeyTotal;
                if (delta >= 0)
                {
                    _shugoKeyBase += delta;
                }
                else
                {
                    int drop = -delta;
                    int fromBase = Math.Min(_shugoKeyBase, drop);
                    _shugoKeyBase -= fromBase;
                    _shugoKeyBonus = Math.Max(0, _shugoKeyBonus - (drop - fromBase));
                }
            }

            _shugoKeyTotal = total;
            _shugoKeyHasValue = true;
        }

        ShugoKeyChanged?.Invoke();
    }

    private void ClearShugoKey()
    {
        lock (_shugoKeyGate)
        {
            if (!_shugoKeyHasValue && _shugoKeyBase == 0 && _shugoKeyBonus == 0 && _shugoKeyTotal == 0)
            {
                return;
            }

            _shugoKeyBase = _shugoKeyBonus = _shugoKeyTotal = 0;
            _shugoKeyHasValue = false;
        }

        ShugoKeyChanged?.Invoke();
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
        // A 0x9702 snapshot can arrive PARTIAL — the byte-scan misses a record, or an incremental re-broadcast
        // carries a subset — and a naive Clear+Replace then SHRINKS a complete roster (observed live: 5→4→3→2
        // over ~11 s), which would strand real party members / mis-gate the display. Guard: ignore a snapshot
        // that is a STRICT SUBSET of the current roster (same identities, fewer of them) — keep the fuller one,
        // just refresh its freshness. Any snapshot with a NEW member (the party grew or changed) still replaces.
        // A genuine party change/leave is handled by ClearPartyRoster (0x971D ExitParty) and the resets, not by
        // a shrinking snapshot.
        var incoming = members.Select(m => (m.Nickname, m.Server)).ToHashSet();
        var current = _partyRoster.Select(m => (m.Nickname, m.Server)).ToHashSet();
        if (_partyRoster.Count > 0 && incoming.Count < current.Count && incoming.IsSubsetOf(current))
        {
            _partyRosterAtMs = Clock(); // partial re-broadcast of the same party — hold the fuller roster, stay fresh
            return;
        }

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

    /// <summary>The (nickname, server) of every current party/raid roster member (the 0x9702 snapshot),
    /// if it arrived within <paramref name="withinMs"/>; empty otherwise. Unlike <see cref="PartyRoster"/>
    /// this returns the raw roster identities (no uid resolution / drop), used to scope the movement replay
    /// to party/raid members only — works for any party size (slots aren't required, unlike CurrentPartySlots).</summary>
    public IReadOnlyList<(string Nickname, int Server)> PartyMemberIdentities(long withinMs)
    {
        if (_partyRoster.Count == 0 || Clock() - _partyRosterAtMs > withinMs)
        {
            return Array.Empty<(string, int)>();
        }

        return _partyRoster.Select(m => (m.Nickname, m.Server)).ToList();
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
        else
        {
            TryRebindExecutorByIdentity(uid, user);
        }
    }

    /// <summary>이름 앵커 재발급 — 본인 로드 패킷(0x3633) 없이도 본인을 새 엔티티 id에 다시 묶는다.
    /// <para>존 이동·난입으로 본인의 uid가 바뀌어도 게임이 본인 로드 패킷을 항상 다시 보내지는 않는다. 그동안
    /// 본인 딜은 신원 미상으로 남고, 그걸 메우려던 휴리스틱 복구가 낯선 사람을 본인으로 둔갑시킨 사고가 있었다
    /// (필드보스 오귀속). 이 경로는 추정이 아니라 <b>신원 완전일치</b>다: 아무 플레이어 메타데이터 패킷이든
    /// 그 (닉네임, 서버)가 현재 본인과 정확히 같으면 그 uid를 본인으로 승격시킨다.</para>
    /// <para>가드 — ① 현재 본인이 확정돼 있어야 한다(앵커가 없으면 본인을 만들어낼 수 없다) ② 닉네임 완전일치
    /// ③ 서버는 <b>양쪽 다 알 때만</b> 비교한다(잘린 0x3633은 Server=-1로 남으므로 모르면 통과시킨다).
    /// 승격은 <see cref="SaveExecutorId"/>를 타므로 이름·서버가 같은 재인스턴스로 분류되어 파티 프리뷰 등
    /// 직전 상태가 보존된다(캐릭터 교체와 구분됨).</para></summary>
    private void TryRebindExecutorByIdentity(int uid, User candidate)
    {
        int executor = _userRepository.Executor();
        if (executor == 0 || executor == uid || string.IsNullOrWhiteSpace(candidate.Nickname))
        {
            return;
        }

        User? current = _userRepository.Get(executor);
        if (current == null
            || string.IsNullOrWhiteSpace(current.Nickname)
            || !string.Equals(current.Nickname, candidate.Nickname, StringComparison.Ordinal))
        {
            return;
        }

        if (current.Server > 0 && candidate.Server > 0 && current.Server != candidate.Server)
        {
            return; // 타 서버 동명이인 — 본인이 아니다
        }

        SaveExecutorId(uid);
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
                ClearShugoKey();     // the shugo-festa key count, likewise
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
        // The 10-min throttle only guards the fire-and-forget power-enrichment path (onResult == null), whose
        // result is persisted on the User object so a re-request within the window is pure waste. A caller that
        // passes a callback (the party-join panel, which injects skill/stigma badges per request) MUST always
        // reach LookupAsync — its own 6h/10min TTL cache + in-flight de-dup already suppress redundant network
        // calls, and answer a cached character synchronously. Throttling the callback path here silently dropped
        // the callback on any re-application within 10 min, leaving the join card with no badges.
        if (onResult == null && _officialLookupAttempts.TryGetValue(uid, out long previous) && now - previous < 10 * 60 * 1000L)
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
        SaveUseBuff(uid, skillCode, buffStart, buffEnd, duration, actorId, 0);

    /// <summary><paramref name="level"/> = 어노멀 레벨(0 = 모름). 서로 중복 적용되지 않는 버프 쌍에서 높은 쪽을
    /// 고르는 데 쓰인다.</summary>
    public void SaveUseBuff(int uid, int skillCode, long buffStart, long buffEnd, long duration, int actorId, int level) =>
        SaveUseBuff(uid, skillCode, buffStart, buffEnd, duration, actorId, level, 0);

    /// <summary><paramref name="slot"/> = 그 대상의 버프 슬롯 번호(0 = 모름). 제거 브로드캐스트(0x382C)가
    /// 이 슬롯을 지목하므로, 들고 있어야 정확히 그 인스턴스만 지울 수 있다.</summary>
    public void SaveUseBuff(int uid, int skillCode, long buffStart, long buffEnd, long duration, int actorId, int level, int slot)
    {
        SaveUseBuff(uid, new UseBuff(skillCode, buffStart, buffEnd, duration, actorId));

        // Live combat-assist overlay: track buffs currently ON the local player (recipient == executor), so
        // the overlay can show what's active + how long is left. Job-skill buffs only — consumable/item buffs
        // (food/drink/scroll/potion, in the lower item-code band) and blacklisted buffs are excluded.
        int owner = _userRepository.Executor();

        // Buff-tracking diagnostics (crowded-raid overlay failure investigation). Counts, per job-buff seen,
        // whether it was accepted onto the self-overlay (uid==owner), or lost because the executor is unknown
        // (owner==0, e.g. a self-recognition 0x3633 dropped on a flooded instance entry). Single-consumer
        // thread, so plain increments. Read via BuffDiagSnapshot on the same thread.
        if (IsJobBuffCode(skillCode) && !IsBuffBlacklisted(skillCode))
        {
            _diagJobBuffSeen++;
            if (owner == 0)
            {
                _diagOwnerZeroJobBuff++;
            }
            else if (uid == owner)
            {
                _diagSelfBuffAccepted++;
            }
        }

        if (owner != 0 && uid == owner && IsJobBuffCode(skillCode) && !IsBuffBlacklisted(skillCode))
        {
            RecordObservedBuff(skillCode); // populate the per-job picker catalog

            // Store unless fully Off (hidden AND not voice). A "음성만" buff (hidden + voice) is still stored so
            // the announce path can speak it; the overlay drops it downstream via OwnerBuffView.Overlay.
            if (!IsBuffHidden(skillCode) || IsBuffVoice(skillCode))
            {
                int baseCode = BuffDisplayBase(skillCode);
                bool indefinite = baseCode == IndefiniteStanceBaseCode; // 폭주: synthetic-TTL maintained stance
                // Keep the maintained stance on screen well past its short synthetic duration so a held
                // re-broadcast gap doesn't false-expire it; a real "off" then clears within the keep-alive.
                long overlayEnd = indefinite ? buffStart + IndefiniteStanceOverlayKeepAliveMs : buffEnd;
                lock (_ownerBuffGate)
                {
                    // Key by BASE code so the SAME buff re-cast by a different player/rank refreshes the one slot
                    // in place (no duplicate icon, no duplicate start alert) — the later cast takes over.
                    _ownerBuffs[baseCode] = (overlayEnd, actorId, duration, indefinite, level, slot);
                }

                LiveBuffsChanged?.Invoke();
            }
        }
        else if (IsJobBuffCode(skillCode) && !IsBuffBlacklisted(skillCode) && IsPartyMember(uid))
        {
            // A party member's job buff — not shown on the (self-only) overlay, but catalogued so the picker
            // lists other jobs' buffs too (self + party coverage).
            RecordObservedBuff(skillCode);
        }
    }

    /// <summary>버프 제거 브로드캐스트(0x382C) 반영. 본인 것만, 그리고 <b>슬롯이 일치하는 항목만</b> 지운다.
    /// <para>지금까지는 제거 신호가 없다고 보고 duration이 다 흐를 때까지 슬롯을 남겨 뒀는데, 실측상 서버가
    /// 예상 만료보다 1초 이상 일찍 끊는 경우가 절반을 넘어(0x382C로 종료된 인스턴스의 57.6%) 오버레이가
    /// 오래 과다 표시되고 있었다. 슬롯 매칭이라 같은 코드가 겹쳐 걸려도 엉뚱한 인스턴스를 지울 수 없다.</para>
    /// <para>슬롯을 모르는(0) 엔트리는 건드리지 않는다 — 기존 만료 로직이 그대로 처리한다(fail-open).</para></summary>
    public void RemoveBuffSlots(int entityId, IReadOnlyList<int> slots)
    {
        if (entityId <= 0 || slots.Count == 0 || entityId != _userRepository.Executor())
        {
            return;
        }

        bool changed = false;
        lock (_ownerBuffGate)
        {
            foreach (int baseCode in _ownerBuffs
                         .Where(kv => kv.Value.Slot != 0 && slots.Contains(kv.Value.Slot))
                         .Select(kv => kv.Key)
                         .ToList())
            {
                _ownerBuffs.Remove(baseCode);
                changed = true;
            }
        }

        if (changed)
        {
            LiveBuffsChanged?.Invoke();
        }
    }

    /// <summary>엔티티 사망(0x8D04). <b>본인</b>이 죽었을 때만 버프 오버레이 스토어를 비운다 — 사망 후
    /// 부활하면 게임에서 모든 버프가 날아간 상태이기 때문이다.
    /// <para>쿨다운(<c>_cooldowns</c>)은 <b>비우지 않는다</b>: 사망이 스킬 쿨다운을 초기화하지는 않으므로
    /// 함께 지우면 다음 0x3847 스냅샷이 올 때까지 "쿨타임 회색" 표시가 틀리게 된다.</para>
    /// <para><see cref="OwnerBuffClearRevision"/>을 올려 두면 500ms 오버레이 틱이 "이번 틱에 사망 클리어가
    /// 있었다"를 알 수 있다 — 스냅샷을 뜬 직후 클리어가 들어오는 서브초 레이스에서 잔여 버프가 종료 음성을
    /// 외치는 것을 막는 용도다(사망으로 인한 초기화에는 종료 알림을 내지 않는다).</para></summary>
    public void SaveEntityDeath(int entityId, long arrivedAt)
    {
        int owner = _userRepository.Executor();
        if (owner == 0 || entityId != owner)
        {
            return; // 몹·파티원 사망은 오버레이와 무관
        }

        lock (_ownerBuffGate)
        {
            _ownerBuffs.Clear();
            _ownerBuffClearRevision++;
        }

        LiveBuffsChanged?.Invoke();
    }

    private long _ownerBuffClearRevision;

    /// <summary>사망으로 버프 스토어가 비워질 때마다 증가. 오버레이 틱이 값 변화를 보고 종료 음성을 건너뛴다.</summary>
    public long OwnerBuffClearRevision
    {
        get { lock (_ownerBuffGate) { return _ownerBuffClearRevision; } }
    }

    /// <summary>회생의 계약 긴급 회복 발동. 발동 시각을 기록하고(가동률 표의 "N회" 집계용), 본인 것이면
    /// 오버레이 슬롯을 60초 재발동 대기로 채운다.
    /// <para>일부러 <see cref="SaveUseBuff(int, UseBuff)"/>를 쓰지 않는다 — 그 경로는 _useBuffRepository →
    /// 버프 업타임 집계 → 통계 웹 페이로드로 흘러가므로, 우리가 합성한 60초짜리 "버프"가 실제로는 존재하지
    /// 않는 업타임으로 업로드된다. 이 데이터는 미터 화면 전용이다.</para></summary>
    public void SaveRevivalHeal(int uid, int skillCode, long amount, long arrivedAt)
    {
        if (uid <= 0)
        {
            return;
        }

        lock (_revivalHealGate)
        {
            if (!_revivalHeals.TryGetValue(uid, out List<(long At, int Code)>? list))
            {
                list = new List<(long At, int Code)>();
                _revivalHeals[uid] = list;
            }

            list.Add((arrivedAt, skillCode));
            if (list.Count > RevivalHealsPerUserCap)
            {
                list.RemoveRange(0, list.Count - RevivalHealsPerUserCap);
            }
        }

        // 오버레이/음성은 본인 것만. (이 프레임은 주변 플레이어 전원에 대해 방송된다.)
        if (uid != _userRepository.Executor())
        {
            return;
        }

        int baseCode = RevivalContractBase(skillCode);
        int cooldownCode = RevivalHealCooldownCode(baseCode);

        // 합성 코드를 이름표에 등록해 두면 오버레이 이름·직업별 picker 노출·숨김/음성 토글이 전부 기존
        // 경로로 해결된다((A) 버프와 같은 직업 그룹에 묶이도록 job 문자열을 물려받는다).
        lock (_buffPickerGate)
        {
            if (!_buffNames.ContainsKey(cooldownCode))
            {
                string job = _buffNames.TryGetValue(baseCode, out (string Name, string Job) bn) ? bn.Job : "기타";
                _buffNames[cooldownCode] = (RevivalHealCooldownName, job);
            }
        }

        RecordObservedBuff(cooldownCode);
        if (IsBuffHidden(cooldownCode) && !IsBuffVoice(cooldownCode))
        {
            return; // picker에서 완전히 끈 항목
        }

        lock (_ownerBuffGate)
        {
            _ownerBuffs[cooldownCode] = (arrivedAt + RevivalHealCooldownMs, uid, RevivalHealCooldownMs, false, 0, 0);
        }

        LiveBuffsChanged?.Invoke();
    }

    /// <summary>회복 프록 코드(예: 15790007)를 그 직업의 회생의 계약 버프 base(15790000)로. <see cref="BuffBaseCode"/>는
    /// 9자리 직업 버프 대역 전용이라 8자리인 이 코드에는 쓸 수 없다.</summary>
    private static int RevivalContractBase(int skillCode) => skillCode / 10000 * 10000;

    /// <summary>[<paramref name="start"/>, <paramref name="end"/>] 창에서 회생의 계약 긴급 회복이 몇 번
    /// 발동했는지 + 표시용 base 코드/이름. 가동률(%)이 무의미한 발동형이라 상세 창이 "N회"로 그린다.
    /// 통계 웹에는 보내지 않는다(미터 전용).</summary>
    public (int Count, int Code, string Name) RevivalHealSummary(int uid, long start, long end)
    {
        int count = 0, code = 0;
        lock (_revivalHealGate)
        {
            if (_revivalHeals.TryGetValue(uid, out List<(long At, int Code)>? list))
            {
                foreach ((long at, int c) in list)
                {
                    if (at >= start && at <= end)
                    {
                        count++;
                        code = c;
                    }
                }
            }
        }

        if (count == 0)
        {
            return (0, 0, string.Empty);
        }

        // (A) 5초 저항 스택도 "회생의 계약" 이름으로 가동률(%) 행을 차지하므로, 발동 횟수 행은 오버레이와
        // 같은 이름/코드를 써서 구분한다(아이콘은 base 폴백으로 동일하게 나온다).
        return (count, RevivalHealCooldownCode(RevivalContractBase(code)), RevivalHealCooldownName);
    }

    private void RecordObservedBuff(int runtimeCode)
    {
        int baseCode = BuffDisplayBase(runtimeCode);
        bool added;
        lock (_buffPickerGate)
        {
            added = _observedBuffBases.Add(baseCode);
        }

        if (added)
        {
            BuffCatalogChanged?.Invoke();
        }
    }

    private bool IsPartyMember(int uid)
    {
        User? u = _userRepository.Get(uid);
        if (u?.Nickname is not { Length: > 0 } nick)
        {
            return false;
        }

        IReadOnlyList<(string Nickname, int Server)> party = PartyMemberIdentities(30 * 60 * 1000L);
        foreach ((string Nickname, int Server) m in party)
        {
            if (m.Nickname == nick && m.Server == u.Server)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A class-skill buff code (11xxxxxxx 검성 .. 19xxxxxxx 권성), as opposed to an item/consumable
    /// buff in the lower code band (food/drink/scroll/potion) which the overlay excludes. Also the only safe
    /// gate for reading a job prefix off a code: 8-digit mob/consumable codes (12000101 = 중독) sit in the same
    /// leading digits as a class and would otherwise pass for that class's self-buff.</summary>
    public static bool IsJobBuffCode(int code) => code is >= 110_000_000 and <= 199_999_999;

    // ---- live owner-buff store (for the combat-assist overlay) ----
    // Keyed by BASE skill code (level-independent) so a re-cast of the same buff refreshes one entry.
    private readonly Dictionary<int, (long End, int Actor, long Duration, bool Indefinite, int Level, int Slot)> _ownerBuffs = new(); // baseCode -> (expiry, applier, duration, indefinite, abnormal level)

    // 폭주 (권성): the only maintained-stance buff broadcast with no expiry (duration 0xFFFFFFFF). The parser
    // gives every apply a short synthetic duration (StreamProcessor.IndefiniteStanceFallbackMs) and its whole
    // runtime band 191300000..191399999 collapses to this base. On the LIVE overlay we keep the slot alive far
    // longer than that synthetic duration so an ordinary held re-broadcast gap (combat lull / dropped frame /
    // momentary owner==0) doesn't false-expire it — the reported "폭주가 유지되는데 꺼졌다고 뜬다" bug.
    private const int IndefiniteStanceBaseCode = 19130000;
    private const long IndefiniteStanceOverlayKeepAliveMs = 20_000;

    // ---- 회생의 계약: (B) 긴급 회복 프록 ----
    // 이 스킬은 두 효과를 가지는데 서버가 버프로 방송하는 건 (A) 5초 상태이상-저항 스택뿐이다. 실전에서 의미
    // 있는 (B) "생명력 10% 이하 즉시 회복"은 버프로 존재하지 않고 actor == target 인 0x3804 프레임으로만 오며,
    // 1분 재발동 제한을 알려주는 서버 신호도 없다(60초짜리 마커 버프도, 0x3847 쿨다운 항목도 없음). 그래서
    // 락아웃은 발동 시각부터 우리가 센다. 상수 근거 = 코퍼스 186개 간격의 최솟값 60,101ms(60초 미만 0건).
    private const long RevivalHealCooldownMs = 60_000;
    private const int RevivalHealsPerUserCap = 512; // 1분 쿨이라 장시간 세션도 수백 건 이하
    private readonly object _revivalHealGate = new();
    private readonly Dictionary<int, List<(long At, int Code)>> _revivalHeals = new();

    /// <summary>회생의 계약 계열의 버프 base 코드(살성13·궁성14·마도성15·정령성16·권성19).</summary>
    private static bool IsRevivalContractBase(int baseCode) => baseCode is
        13790000 or 14790000 or 15790000 or 16790000 or 19790000;

    // 회복 쿨다운은 (A) 5초 상태이상-저항 스택과 별개의 슬롯으로 띄운다 — 같은 base 코드를 공유하면
    // _ownerBuffs가 last-write-wins라 (A)가 60초 카운트다운을 5초로 잘라먹기 때문이다(실측: 회복 발동의 7%가
    // ±200ms 내 (A)와 동시 발동). base + 7 을 합성 키로 쓰면 JoinIcons.Skill이 8자리 코드를
    // code/10000*10000 으로 접어 아이콘을 찾으므로 회생의 계약 아이콘이 그대로 재사용된다.
    private const string RevivalHealCooldownName = "회계·회복";
    private static int RevivalHealCooldownCode(int baseCode) => baseCode + 7;
    // Skill cooldowns from the 0x3847 snapshot, keyed by the SAME base code, so a buff slot can be grayed while
    // its skill is on cooldown. Value = cooldown end (ms, capture clock); on-cooldown iff end > now.
    private readonly Dictionary<int, long> _cooldowns = new(); // baseCode -> cooldown end (ms)
    private readonly object _ownerBuffGate = new();

    /// <summary>Cooldown update from 0x3847 (self snapshot, <paramref name="actorId"/>=0) or 0x3802 (per-cast,
    /// real actor). Stored under the buff overlay's base scheme (skill 8-digit -> /10000*10000, buff 9-digit ->
    /// /100000*10000 — validated to line up with buff bases). remaining 0 = ready (end in the past). Only the
    /// self's cooldowns are kept: actorId 0 (snapshot) or == executor. Consumer-thread writer.</summary>
    public void SaveCooldown(int skillCode, long remainingMs, long arrivedAt, int actorId)
    {
        if (actorId != 0 && actorId != _userRepository.Executor())
        {
            return; // another player's cooldown — not for the self overlay
        }

        int baseCode = skillCode is >= 11_000_000 and <= 19_999_999 ? skillCode / 10_000 * 10_000 : BuffBaseCode(skillCode);
        lock (_ownerBuffGate)
        {
            _cooldowns[baseCode] = arrivedAt + Math.Max(0, remainingMs);
        }
    }

    // Buff-tracking diagnostics (see SaveUseBuff). Written on the single consumer thread only.
    private long _diagJobBuffSeen;        // job-buff apply/refresh frames seen (any recipient)
    private long _diagSelfBuffAccepted;   // ... of those, target==executor -> counted onto the self overlay
    private long _diagOwnerZeroJobBuff;   // ... seen while executor is unknown (owner==0): self-recognition lost

    /// <summary>Snapshot of the buff-tracking diagnostic counters + current executor and live owner-buff store
    /// size. Read on the consumer thread. Discriminates the crowded-raid overlay failure: healthy
    /// <c>SelfAccepted</c> means self buff frames arrive and pass the gate (fault is downstream / refresh loss);
    /// a spike in <c>OwnerZero</c> or <c>SelfAccepted</c> stalling to 0 while <c>JobBuffSeen</c> keeps rising
    /// means the executor gate is blacking out self buffs.</summary>
    public (long JobBuffSeen, long SelfAccepted, long OwnerZero, int Owner, int StoreCount, int CdStore, int CdActive, int BuffsOnCd) BuffDiagSnapshot(long nowMs)
    {
        int storeCount, cdStore, cdActive = 0, buffsOnCd = 0;
        lock (_ownerBuffGate)
        {
            storeCount = _ownerBuffs.Count;
            cdStore = _cooldowns.Count;
            foreach (long cdEnd in _cooldowns.Values)
            {
                if (cdEnd > nowMs)
                {
                    cdActive++;
                }
            }

            // active owner buffs whose skill is on cooldown right now — these are the ones that SHOULD gray.
            foreach (KeyValuePair<int, (long End, int Actor, long Duration, bool Indefinite, int Level, int Slot)> kv in _ownerBuffs)
            {
                if (kv.Value.End > nowMs && _cooldowns.TryGetValue(kv.Key, out long cd) && cd > nowMs)
                {
                    buffsOnCd++;
                }
            }
        }

        return (_diagJobBuffSeen, _diagSelfBuffAccepted, _diagOwnerZeroJobBuff, _userRepository.Executor(), storeCount, cdStore, cdActive, buffsOnCd);
    }

    /// <summary>Raised when a buff on the local player is applied/refreshed.</summary>
    public event Action? LiveBuffsChanged;

    /// <summary>The buffs currently active on the local player at <paramref name="nowMs"/>, longest remaining
    /// first. <c>Code</c> is the base skill code; <c>DurationMs</c> is the full duration (for the countdown
    /// ring); <c>ByOther</c> = applied by someone else; <c>Overlay</c> = draw it (false for a 음성만 buff, which
    /// is returned only so the announce path can speak it). Fully-Off buffs (hidden + not voice) are excluded.</summary>
    public IReadOnlyList<OwnerBuffView> ActiveOwnerBuffs(long nowMs)
    {
        int owner = _userRepository.Executor();
        var result = new List<OwnerBuffView>();
        lock (_ownerBuffGate)
        {
            foreach (KeyValuePair<int, (long End, int Actor, long Duration, bool Indefinite, int Level, int Slot)> kv in _ownerBuffs)
            {
                if (kv.Value.End <= nowMs)
                {
                    continue; // expired
                }

                bool hidden = IsBuffHidden(kv.Key);
                if (hidden && !IsBuffVoice(kv.Key))
                {
                    continue; // Off — unchecked in the picker; hide immediately, don't wait for expiry
                }

                string name = _buffNames.TryGetValue(BuffBaseCode(kv.Key), out (string Name, string Job) bn)
                    ? bn.Name
                    : Buff(kv.Key)?.Name ?? Skill(kv.Key)?.Name ?? $"버프 {kv.Key}";
                bool onCooldown = _cooldowns.TryGetValue(kv.Key, out long cdEnd) && cdEnd > nowMs;
                result.Add(new OwnerBuffView(
                    kv.Key, name, kv.Value.End - nowMs, kv.Value.Duration, kv.Value.End,
                    owner != 0 && kv.Value.Actor != owner,
                    !hidden,  // Overlay: 음성만 (hidden + voice) is announced but not drawn
                    onCooldown,
                    kv.Value.Indefinite));
            }

            SuppressExclusiveLosers(result, nowMs);
        }

        return result.OrderByDescending(r => r.RemainingMs).ToList();
    }

    /// <summary>인게임에서 서로 중복 적용되지 않는 버프 쌍이 둘 다 살아 있으면 지는 쪽을 목록에서 뺀다.
    /// 승자 판정: 고정 승자가 있으면 그것, 없으면 어노멀 레벨이 높은 쪽, 레벨이 같거나 둘 다 모르면(0)
    /// 지정된 동률 승자, 그것도 없으면 나중에 적용된 쪽(End가 늦은 쪽)을 남긴다.
    /// <para>_ownerBuffGate를 이미 잡은 상태에서 호출된다.</para></summary>
    private void SuppressExclusiveLosers(List<OwnerBuffView> rows, long nowMs)
    {
        foreach (ExclusiveBuffPair pair in ExclusiveBuffPairs)
        {
            int ai = rows.FindIndex(r => r.Code == pair.A);
            int bi = rows.FindIndex(r => r.Code == pair.B);
            if (ai < 0 || bi < 0)
            {
                continue; // 한쪽만 켜져 있으면 아무것도 감추지 않는다
            }

            OwnerBuffView a = rows[ai], b = rows[bi];
            int loser;
            if (pair.FixedWinner != 0)
            {
                loser = pair.FixedWinner == pair.A ? pair.B : pair.A;
            }
            else
            {
                int la = _ownerBuffs.TryGetValue(pair.A, out var va) ? va.Level : 0;
                int lb = _ownerBuffs.TryGetValue(pair.B, out var vb) ? vb.Level : 0;
                if (la != lb && la > 0 && lb > 0)
                {
                    loser = la > lb ? pair.B : pair.A;
                }
                else if (pair.TieWinner != 0)
                {
                    loser = pair.TieWinner == pair.A ? pair.B : pair.A;
                }
                else
                {
                    loser = a.EndMs >= b.EndMs ? pair.B : pair.A; // 레벨을 모르면 나중에 걸린 쪽을 남긴다
                }
            }

            rows.RemoveAll(r => r.Code == loser);
        }
    }

    private void ClearOwnerBuffs()
    {
        lock (_ownerBuffGate)
        {
            _ownerBuffs.Clear();
            _cooldowns.Clear();
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
        // Training-dummy test mode: a hit on a dummy drives (and is gated by) the dummy battle machine. Drop it —
        // never record — when test mode is off or the duration cut has fired, so an idle/finished dummy shows no
        // combat and post-cut damage can't inflate the frozen result. Non-dummy targets take the plain path.
        if (pdp.TargetId > 0 && IsMobDummy(pdp.TargetId) && !AcceptDummyHit(pdp.TargetId))
        {
            return;
        }

        _packetRepository.Save(pdp);
    }

    // ---- battle state machine ----

    public int CurrentTarget() => _packetRepository.CurrentTarget();
    private void SaveCurrentTarget(int targetId) => _packetRepository.CurrentTarget(targetId);
    public long CurrentBattleStart() => _packetRepository.CurrentBattleStart();
    public long CurrentBattleEnd() => _packetRepository.CurrentBattleEnd();
    private void SaveCurrentBattleStart() => _packetRepository.SaveCurrentBattleStart(Clock());
    private void SaveCurrentBattleEnd(long time) => _packetRepository.SaveCurrentBattleEnd(time);

    public bool IsMobDummy(int mobId)
    {
        if (mobId <= 0) return false;
        int? mobCode = GetMobId(mobId);
        return mobCode != null && Mob(mobCode.Value)?.IsDummy == true;
    }

    public bool IsCurrentTargetDummy() => IsMobDummy(CurrentTarget());

    /// <summary>Decide whether a damage packet against a training dummy should be RECORDED (and drive the live
    /// dummy battle). Called from <see cref="SaveDamage"/> on the consumer thread. Returns false — so the packet
    /// is dropped and never counted — when the dummy test mode is off, or the chosen duration has elapsed (the
    /// hard cut). The first accepted hit opens the battle window; a hit at/after the duration ends the run and
    /// latches <see cref="_dummyCutoff"/> so every later hit is ignored until a reset clears it.</summary>
    private bool AcceptDummyHit(int mobId)
    {
        if (!_dummyTestMode || _dummyCutoff) return false;

        long now = Clock();
        if (CurrentTarget() <= 0)
        {
            _battleRevision++; // a fresh battle id, so DpsCalculator resets its per-battle cache/sequence
            SaveCurrentBattleStart();
            SaveCurrentTarget(mobId);
            _lastDummyHitTime = now;
            return true;
        }

        long start = CurrentBattleStart();
        if (start > 0 && now - start >= DummyDurationMs)
        {
            SaveCurrentBattleEnd(start + DummyDurationMs); // freeze the run at exactly the chosen duration
            SaveCurrentTarget(-1);
            _dummyCutoff = true;
            return false; // this hit is past the cut — drop it too
        }

        _lastDummyHitTime = now;
        return true;
    }

    /// <summary>Per-report-tick maintenance of a live dummy battle (called at the top of
    /// <see cref="DpsCalculator.GetDps"/>). Enforces the duration hard cut even when hits pause, ends the run
    /// promptly if test mode is switched off mid-run, and keeps the original 5s idle auto-end.</summary>
    public void TickDummyBattle()
    {
        int current = CurrentTarget();
        if (current <= 0 || !IsCurrentTargetDummy()) return;

        long now = Clock();
        if (!_dummyTestMode)
        {
            SaveCurrentBattleEnd(now); // mode turned off mid-run — end now (no cutoff latch; re-enabling starts fresh)
            SaveCurrentTarget(-1);
            _lastDummyHitTime = 0;
            return;
        }

        long start = CurrentBattleStart();
        if (start > 0 && now - start >= DummyDurationMs)
        {
            SaveCurrentBattleEnd(start + DummyDurationMs);
            SaveCurrentTarget(-1);
            _dummyCutoff = true;
            return;
        }

        if (now - _lastDummyHitTime > DummyTimeoutMs)
        {
            SaveCurrentBattleEnd(_lastDummyHitTime);
            SaveCurrentTarget(-1);
            _lastDummyHitTime = 0;
        }
    }

    /// <summary>Clear the duration hard-cut latch so the next dummy hit opens a fresh window (used by the dummy
    /// DPS reset and the full/soft resets). The mode and chosen duration are intentionally NOT touched here.</summary>
    public void ResetDummyCutoff() => _dummyCutoff = false;

    public void StartBattle(int mobId) => StartBattleAt(mobId, Clock());

    /// <summary>전투 시작. <paramref name="startAt"/>는 <b>스탬프할</b> 시작 시각으로, 통상 경로에서는 현재
    /// 시각이지만 소급 승격(<see cref="PromoteUnresolvedStart"/>)에서는 원래 토글 시각이다. 억제 가드들은
    /// 스탬프 시각이 아니라 항상 현재 시각으로 판단한다.</summary>
    private void StartBattleAt(int mobId, long startAt)
    {
        int? mobCode = GetMobId(mobId);
        long now = Clock();
        EndedBattle? endedBattle = _recentlyEndedBattles.TryGetValue(mobId, out EndedBattle eb) ? eb : null;
        if (CurrentTarget() <= 0
            && endedBattle != null
            && endedBattle.Value.MobCode == mobCode
            && (MobHp(mobId) ?? 0) == 0 // int? — a despawned corpse loses HP tracking (null); null must count as
                                        // "corpse", else the guard leaks and a ghost restart re-stamps
                                        // CurrentBattleStart at the kill (→ split + 191M-DPS upload)
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
        _unresolvedStarts.Remove(mobId);
        _recentlyEndedBattles.Remove(mobId);
        _battleRevision++;
        _packetRepository.SaveCurrentBattleStart(startAt);
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
            DpsSeries = data.DpsSeries,          // frozen per-second damage series so the replayed DPS graph isn't empty
            BuffIntervals = data.BuffIntervals,  // frozen buff timeline (built pre-prune by the caller) for the graph's icon lane
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
        _dummyCutoff = false; // full wipe re-arms the dummy test window (mode/duration are preserved)
        _partyRoster.Clear();
        _partyRosterAtMs = 0;
        ClearAetherStatus();
        ClearShugoKey();
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
        _dummyCutoff = false;     // the 초기화 button re-arms the dummy test window (mode/duration are preserved)
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

namespace WaffleMeter.Capture;

/// <summary>
/// The game-state / catalog dependencies the packet parser needs — a narrow subset of Kotlin
/// <c>DataManager</c>. Read side: the static mob catalog + the runtime instanceId-&gt;mobCode map +
/// skill-code membership. Write side: the data-layer side effects the Kotlin handlers perform
/// (saveDamage / startBattle / saveNickname / ...), expressed in PRIMITIVES only so the parser
/// (Capture) need not reference the data-layer entity types.
///
/// In capture-only validation these writes are no-ops (<see cref="NullCaptureGameData"/> / the
/// reference-data GameData), so they do not change the parser's emitted events. The full DataManager
/// implements them to drive the DPS pipeline.
/// </summary>
public interface ICaptureGameData
{
    // ---- read ----
    Mob? GetMob(int code);
    int? GetMobId(int instanceId);
    void SaveMobId(int instanceId, int mobCode);
    bool SkillExists(long code);
    long CurrentEpoch();

    /// <summary>True if <paramref name="uid"/> is a recognized player (a nickname has been observed for it).
    /// Used to validate the looser summon-owner fallback marker (07 02 01) — the same byte sequence occurs as
    /// an unrelated field inside mob-spawn packets, so without this check it would mis-map a mob to a fixed
    /// garbage "owner". Default false (capture-only contexts don't track users).</summary>
    bool IsKnownUser(int uid) => false;

    // ---- write side effects (no-op in capture-only mode) ----
    void SaveDamage(ParsedDamagePacket pdp, long epoch);
    void StartBattle(int target);
    void EndBattle(int target);
    void SaveNickname(int uid, string nickname, bool isExecutor, int server, int jobByte);
    void SaveUserPower(int uid, int power);
    void SaveSummon(int summonId, int ownerId);
    void SaveMobHp(int instanceId, int hp);

    /// <summary>0x8D00의 statId 7이 실어 오는 <b>권위 있는</b> 최대 HP. 종전에는 "관측된 현재 HP의 최댓값"을
    /// 최대치로 추정했는데, 이 값이 오면 그보다 정확하다(저장은 단조 증가라 낮은 값으로 덮이지 않는다).
    /// 다만 보스의 6.9%에만 오므로 추정 경로는 그대로 남는다. 기본 no-op.</summary>
    void SaveMobMaxHp(int instanceId, int maxHp) { }

    /// <summary>스폰(0x3641) 유실로 mobCode가 등록되지 않은 던전 보스를 HP 휴리스틱으로 되살린다 —
    /// 0x8D00이 미등록 엔티티에 대해 보스급 HP를 실어 오고, 그 엔티티가 교전 토글(0x8D21)을 쏜 적이 있으면
    /// 합성 '미상 보스'로 승격해 소급 집계한다. 데이터 계층이 게이트를 판정한다. 기본 no-op(캡처 전용 모드).</summary>
    void TryPromoteUnregisteredBoss(int entityId, long hp) { }

    /// <summary>0x9200 멤버 프로필이 실어 온 (엔티티 uid, 닉네임, 서버). 이름 앵커의 유일한 입력이다 —
    /// 본인 이름은 본인 로드 패킷(0x3633)에만 오고 0x3645에는 절대 오지 않으므로(코퍼스 13,076프레임 0건),
    /// 존 이동·난입으로 본인 uid가 바뀌었는데 0x3633이 다시 오지 않으면 본인을 새 uid에 묶을 방법이 없었다.
    /// <para>파서는 <b>모든</b> 레코드를 그대로 넘긴다. "현재 본인과 신원 완전일치인가"는 executor를 아는 데이터
    /// 계층만 판단할 수 있고, 남의 레코드는 거기서 no-op으로 떨어진다. 기본 no-op(캡처 전용 모드).</para></summary>
    void TryBindExecutorByIdentity(int uid, string nickname, int server) { }

    /// <summary>0x9200 멤버 프로필이 실어 온 (엔티티 uid, 닉네임, 서버)를 표시-계층 보조 로스터에 저장한다.
    /// <see cref="TryBindExecutorByIdentity"/>와 같은 레코드에서 파생되지만 이쪽은 <b>모든</b> 멤버를 담는다 —
    /// 0x9702 로스터가 유실됐을 때의 폴백이자, 타인 닉(0x3645)이 유실돼 무명인 전투행을 uid로 곧장 명명하는
    /// 소스다. uid 재사용 위험 때문에 신원 저장소가 아니라 TTL 있는 표시-전용 맵에만 담는다. 기본 no-op.</summary>
    void SaveMemberProfile(int uid, string nickname, int server) { }
    void SaveUseBuff(int uid, int skillCode, long buffStart, long buffEnd, long duration, int actorId);

    /// <summary>버프 적용 + <paramref name="level"/>(어노멀 레벨, 0 = 모름). 서로 중복 적용되지 않는 버프 쌍에서
    /// "레벨이 높은 쪽"을 고르는 데 쓴다. 기본 구현은 레벨을 버리고 위 오버로드로 위임하므로, 레벨이 필요 없는
    /// 구현체(캡처 전용 모드 등)는 손댈 필요가 없다.</summary>
    void SaveUseBuff(int uid, int skillCode, long buffStart, long buffEnd, long duration, int actorId, int level)
        => SaveUseBuff(uid, skillCode, buffStart, buffEnd, duration, actorId, level, 0);

    /// <summary>버프 적용 + 레벨 + <paramref name="slot"/>(그 대상의 버프 슬롯 번호, 0 = 모름). 슬롯은 제거
    /// 브로드캐스트(0x382C)가 참조하는 키라, 들고 있어야 "정확히 그 인스턴스만" 지울 수 있다.</summary>
    void SaveUseBuff(int uid, int skillCode, long buffStart, long buffEnd, long duration, int actorId, int level, int slot)
        => SaveUseBuff(uid, skillCode, buffStart, buffEnd, duration, actorId);

    /// <summary>버프 제거 브로드캐스트(0x382C). <paramref name="slots"/> = 그 대상에서 사라진 버프 슬롯들.
    /// 슬롯 매칭이라 "이미 만료된 쪽인지 살아 있는 쪽인지" 모호함이 없다 — 코드만 주는 0x921A는 같은 코드가
    /// 겹칠 때 어느 인스턴스를 닫는 신호인지 원리적으로 구분할 수 없어 제거 신호로 쓰지 않는다. 기본 no-op.</summary>
    void RemoveBuffSlots(int entityId, IReadOnlyList<int> slots) { }

    void RequestOfficialCharacterLookup(int uid);

    /// <summary>Skill cooldown update: <paramref name="remainingMs"/> ms left on <paramref name="skillCode"/>'s
    /// cooldown (0 = ready) as of <paramref name="arrivedAt"/> (capture wall-clock ms). <paramref name="actorId"/>
    /// is the caster's entity id, or 0 for the self-only 0x3847 hotbar snapshot (no filter needed); the data
    /// layer keeps only self cooldowns. Default no-op. Drives the buff overlay's cooldown gray-out.</summary>
    void SaveCooldown(int skillCode, long remainingMs, long arrivedAt, int actorId) { }

    /// <summary>엔티티 사망 브로드캐스트(0x8D04). 몹·파티원에게도 오므로 본인 여부 판정은 executor를 아는
    /// 데이터 계층이 한다. 기본 no-op(캡처 전용 모드).</summary>
    void SaveEntityDeath(int entityId, long arrivedAt) { }

    /// <summary>전투 시작 토글이 도착했지만 그 엔티티의 instanceId→mobCode가 아직 등록되지 않아(스폰 패킷 유실
    /// 또는 아직 도착 전) 전투를 열지 못한 경우. 보스 스폰은 교전당 1회뿐이고 전투 중 재방송이 없어서, 그냥
    /// 버리면 그 판은 끝까지 안 열린다. 데이터 계층이 기억해 뒀다가 스폰이 도착하면 되살린다. 기본 no-op.</summary>
    void RememberUnresolvedBattleStart(int mobId) { }

    /// <summary>회생의 계약(살성/궁성/마도성/정령성/권성)의 "생명력 10% 이하 즉시 회복" 발동. 이 효과는 버프로
    /// 방송되지 않고, actor == target 인 0x3804 프레임으로만 관측된다(데미지 varint = 회복량). 서버가 1분
    /// 재발동 제한을 알려주는 신호는 없으므로 락아웃은 데이터 계층이 이 발동 시각부터 센다.
    /// <paramref name="uid"/>는 회복받은 본인. 기본 no-op(캡처 전용 모드).</summary>
    void SaveRevivalHeal(int uid, int skillCode, long amount, long arrivedAt) { }

    /// <summary>Aether (오드) resource update from the 0x610x family. <paramref name="split"/> true =
    /// <paramref name="baseVal"/>/<paramref name="bonus"/> were both carried; false = only
    /// <paramref name="total"/> is meaningful and the data layer back-computes base/bonus from its previous
    /// value. No-op in capture-only mode.</summary>
    void SaveAetherStatus(bool split, int baseVal, int bonus, int total);

    /// <summary>Shugo-festa key (슈고 페스타 보상 열쇠) count update from the 0x610x family (same packets as
    /// aether; a different key byte). <paramref name="split"/> semantics mirror <see cref="SaveAetherStatus"/>.
    /// No-op in capture-only mode.</summary>
    void SaveShugoKey(bool split, int baseVal, int bonus, int total);

    /// <summary>Field-boss respawn timers (boss code → target Unix-ms) from the 0x9101 broadcast. No-op in
    /// capture-only mode.</summary>
    void SaveFieldBossTimers(IReadOnlyList<(int Code, long TargetMs)> timers);

    /// <summary>Full party/raid roster snapshot (each member's nickname + server + sub-group slot 1-8)
    /// from the 0x9702 roster packet. Lets the data layer match members to known uids for the pre-combat
    /// party preview, and (for an 8-인 공대) tag each player's sub-party — slots 1-4 = party 1, 5-8 = party 2.
    /// Slot is 0 when the record header that carries it wasn't matched.</summary>
    void SavePartyRoster(IReadOnlyList<(string Nickname, int Server, int Slot)> members);

    /// <summary>0x9702 로스터가 실어 온 (닉네임, 서버, 직업코드, 전투력). 전투 전 파티 프리뷰의 직업 아이콘·
    /// 전투력을 채우는 display-only 보조 소스. 기본 no-op(캡처 전용/구현 안 한 컨텍스트).</summary>
    void SavePartyRosterJobPower(IReadOnlyList<(string Nickname, int Server, int JobCode, int Power)> members) { }
}

/// <summary>No catalog / empty runtime map; all writes no-op (default capture-only context).</summary>
public sealed class NullCaptureGameData : ICaptureGameData
{
    public static readonly NullCaptureGameData Instance = new();

    public Mob? GetMob(int code) => null;
    public int? GetMobId(int instanceId) => null;
    public void SaveMobId(int instanceId, int mobCode) { }
    public bool SkillExists(long code) => false;
    public long CurrentEpoch() => 0;

    public void SaveDamage(ParsedDamagePacket pdp, long epoch) { }
    public void StartBattle(int target) { }
    public void EndBattle(int target) { }
    public void SaveNickname(int uid, string nickname, bool isExecutor, int server, int jobByte) { }
    public void SaveUserPower(int uid, int power) { }
    public void SaveSummon(int summonId, int ownerId) { }
    public void SaveMobHp(int instanceId, int hp) { }
    public void SaveUseBuff(int uid, int skillCode, long buffStart, long buffEnd, long duration, int actorId) { }
    public void RequestOfficialCharacterLookup(int uid) { }
    public void SavePartyRoster(IReadOnlyList<(string Nickname, int Server, int Slot)> members) { }
    public void SaveAetherStatus(bool split, int baseVal, int bonus, int total) { }
    public void SaveShugoKey(bool split, int baseVal, int bonus, int total) { }
    public void SaveFieldBossTimers(IReadOnlyList<(int Code, long TargetMs)> timers) { }
}

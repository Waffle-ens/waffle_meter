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
    void SaveUseBuff(int uid, int skillCode, long buffStart, long buffEnd, long duration, int actorId);

    /// <summary>버프 적용 + <paramref name="level"/>(어노멀 레벨, 0 = 모름). 서로 중복 적용되지 않는 버프 쌍에서
    /// "레벨이 높은 쪽"을 고르는 데 쓴다. 기본 구현은 레벨을 버리고 위 오버로드로 위임하므로, 레벨이 필요 없는
    /// 구현체(캡처 전용 모드 등)는 손댈 필요가 없다.</summary>
    void SaveUseBuff(int uid, int skillCode, long buffStart, long buffEnd, long duration, int actorId, int level)
        => SaveUseBuff(uid, skillCode, buffStart, buffEnd, duration, actorId);
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

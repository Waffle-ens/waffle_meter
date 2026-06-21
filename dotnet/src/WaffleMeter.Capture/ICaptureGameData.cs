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

    // ---- write side effects (no-op in capture-only mode) ----
    void SaveDamage(ParsedDamagePacket pdp, long epoch);
    void StartBattle(int target);
    void EndBattle(int target);
    void SaveNickname(int uid, string nickname, bool isExecutor, int server, int jobByte);
    void SaveUserPower(int uid, int power);
    void SaveSummon(int summonId, int ownerId);
    void SaveMobHp(int instanceId, int hp);
    void SaveUseBuff(int uid, int skillCode, long buffStart, long buffEnd, long duration, int actorId);
    void RequestOfficialCharacterLookup(int uid);
    void TouchDummyBattle(int target, long epoch);

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
    public void TouchDummyBattle(int target, long epoch) { }
    public void SavePartyRoster(IReadOnlyList<(string Nickname, int Server, int Slot)> members) { }
}

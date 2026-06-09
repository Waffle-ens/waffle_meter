using WaffleMeter.Capture;

namespace WaffleMeter.Data;

/// <summary>
/// Concrete <see cref="ICaptureGameData"/>: a static mob catalog + skill-code set (loaded from
/// mobs.json / skills.json) plus the runtime instanceId-&gt;mobCode map built from spawn packets
/// (Kotlin DataManager's mobIdRepository). Single-consumer; not thread-safe (the live pipeline
/// drives it from one consumer, matching the capture pipeline).
/// </summary>
public sealed class GameData : ICaptureGameData
{
    private readonly IReadOnlyDictionary<int, Mob> _mobs;
    private readonly HashSet<long> _skillCodes;
    private readonly Dictionary<int, int> _mobId = new();

    public GameData(IReadOnlyDictionary<int, Mob> mobs, HashSet<long> skillCodes)
    {
        _mobs = mobs;
        _skillCodes = skillCodes;
    }

    public Mob? GetMob(int code) => _mobs.TryGetValue(code, out Mob? m) ? m : null;

    public int? GetMobId(int instanceId) => _mobId.TryGetValue(instanceId, out int code) ? code : null;

    // Kotlin saveMobId overwrites (last write wins); the previous!=code battle-state side effect is deferred.
    public void SaveMobId(int instanceId, int mobCode) => _mobId[instanceId] = mobCode;

    public bool SkillExists(long code) => _skillCodes.Contains(code);
}

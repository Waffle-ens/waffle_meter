namespace WaffleMeter.Capture;

/// <summary>
/// The game-state / catalog dependencies the packet parser needs — a narrow subset of Kotlin
/// <c>DataManager</c>:
///  - the static mob catalog (mobs.json) via <see cref="GetMob"/>,
///  - the runtime instanceId -> mobCode map (built from spawn packets) via <see cref="GetMobId"/> /
///    <see cref="SaveMobId"/>,
///  - skill-code catalog membership (skills.json) via <see cref="SkillExists"/> for skill normalization.
/// Defined here so the parser stays decoupled from the concrete data layer (implemented in
/// WaffleMeter.Data).
/// </summary>
public interface ICaptureGameData
{
    Mob? GetMob(int code);
    int? GetMobId(int instanceId);
    void SaveMobId(int instanceId, int mobCode);
    bool SkillExists(long code);
}

/// <summary>No catalog / empty runtime map (default). Mob lookups miss, skills don't exist.</summary>
public sealed class NullCaptureGameData : ICaptureGameData
{
    public static readonly NullCaptureGameData Instance = new();
    public Mob? GetMob(int code) => null;
    public int? GetMobId(int instanceId) => null;
    public void SaveMobId(int instanceId, int mobCode) { }
    public bool SkillExists(long code) => false;
}

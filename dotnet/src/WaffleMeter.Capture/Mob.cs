namespace WaffleMeter.Capture;

/// <summary>
/// Reference mob entry from mobs.json. Mirrors Kotlin <c>entity/Mob.kt</c>:
/// <c>data class Mob(code, name, boss, isDummy = false)</c>. <see cref="IsDummy"/> is read from the JSON's
/// optional <c>"isDummy"</c> flag (see <c>ReferenceJson.LoadMobs</c>) and marks 훈련용 허수아비, which the
/// 허수아비 test mode meters only while enabled. Defaults false when the flag is absent.
/// </summary>
public sealed record Mob(int Code, string Name, bool Boss, bool IsDummy = false);

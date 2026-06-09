namespace WaffleMeter.Capture;

/// <summary>
/// Reference mob entry from mobs.json. Mirrors Kotlin <c>entity/Mob.kt</c>:
/// <c>data class Mob(code, name, boss, @Transient isDummy = false)</c>. isDummy is not present in
/// mobs.json (Kotlin @Transient) and is effectively always false at runtime, so it defaults false.
/// </summary>
public sealed record Mob(int Code, string Name, bool Boss, bool IsDummy = false);

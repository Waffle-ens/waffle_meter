namespace WaffleMeter.Capture;

/// <summary>
/// Special-damage flags carried by a damage packet. Verbatim order from Kotlin
/// <c>entity/enums/SpecialDamage.kt</c> (the ordinal order is not relied on, but kept identical).
/// POWER_SHARD exists in the enum but its flag bit (0x80) is currently commented out in the parser.
/// </summary>
public enum SpecialDamage
{
    BACK,
    UNKNOWN,
    PARRY,
    PERFECT,
    DOUBLE,
    ENDURE,
    Restoration,
    POWER_SHARD,
}

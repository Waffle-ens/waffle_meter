namespace WaffleMeter.Capture;

/// <summary>
/// Parsed damage/DoT event — the capture layer's output for one attack, mirroring Kotlin
/// <c>entity/ParsedDamagePacket.kt</c>. (In the final layering this is the DTO that the data/DPS
/// layer consumes; it lives in Capture for now and may move to Domain in Phase 1.)
/// </summary>
public sealed class ParsedDamagePacket
{
    public int ActorId { get; set; }
    public int TargetId { get; set; }
    public int Flag { get; set; }
    public int Damage { get; set; }
    public int SkillCode { get; set; }
    public int Type { get; set; }
    public int Unknown { get; set; }
    public int SwitchVariable { get; set; }
    public int Loop { get; set; }
    public long Timestamp { get; set; }
    public IReadOnlyList<SpecialDamage> Specials { get; set; } = [];
    public bool Dot { get; set; }

    /// <summary>Kotlin: <c>isCrit() = type == 3</c>.</summary>
    public bool IsCrit => Type == 3;
}

namespace WaffleMeter.Data;

/// <summary>
/// Port of Kotlin <c>entity/JoinRequestUser.kt</c>: a pending party-join applicant. Keyed everywhere by
/// <see cref="Requester"/> (the requesting character's uid). <see cref="Skill"/> is empty on the initial
/// packet and populated later from the official-character lookup (skill code → level).
/// </summary>
public sealed record JoinRequestUser
{
    public required string Nickname { get; init; }
    public int Power { get; init; }
    public string? Job { get; init; } // resolved Korean class name (e.g. "마도성"); null if unknown
    public int Server { get; init; }
    public int Requester { get; init; }
    public long ArrivedAt { get; init; }
    public IReadOnlyDictionary<int, int> Skill { get; init; } = new Dictionary<int, int>();
}

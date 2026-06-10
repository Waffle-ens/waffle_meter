namespace WaffleMeter.Data;

/// <summary>
/// Official character info (Kotlin <c>official.OfficialCharacterInfo</c>). Lives in the data layer
/// so <see cref="DataManager"/> can consume it without depending on the Services project; the actual
/// HTTP lookup (WaffleMeter.Services.OfficialCharacterLookup) implements
/// <see cref="IOfficialCharacterLookup"/> and is injected via <see cref="DataManager.OfficialLookup"/>.
/// </summary>
public sealed record OfficialCharacterInfo(
    string Nickname,
    int Server,
    JobClass? Job,
    int Power,
    IReadOnlyDictionary<int, int> Skills);

/// <summary>
/// Abstraction over the official-site character lookup, injected into <see cref="DataManager"/>.
/// Left null in offline/replay runs (no network), which keeps enrichment a no-op and the DPS golden
/// unchanged.
/// </summary>
public interface IOfficialCharacterLookup
{
    void LookupAsync(string? nickname, int server, JobClass? fallbackJob, Action<OfficialCharacterInfo> callback);

    OfficialCharacterInfo? LookupBlocking(string? nickname, int server, JobClass? fallbackJob);
}

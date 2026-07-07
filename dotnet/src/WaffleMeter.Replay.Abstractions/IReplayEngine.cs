using WaffleMeter.Data;

namespace WaffleMeter.Replay;

/// <summary>
/// The positional-replay engine surface the app binds to. The concrete implementation (the
/// reverse-engineered 0x37xx decode + reconstruction) lives in the private, obfuscated
/// <c>WaffleMeter.Replay</c> assembly and is discovered at runtime via <see cref="ReplayEngineLoader"/>;
/// the app never references it at compile time, so it builds and runs fine when the engine DLL is absent
/// (replay simply unavailable). This interface mirrors the engine's public API 1:1.
/// </summary>
public interface IReplayEngine
{
    /// <summary>Parallel tap: feed one assembled application packet (same bytes the DPS parser receives).
    /// A no-op-safe hot-path call; must never disturb the parity-critical damage path.</summary>
    void Scan(byte[] packet, long at);

    /// <summary>Build and store the replay for a just-logged battle (kill or wipe). Wire to
    /// <c>DpsCalculator.OnBattleLogged</c>. <paramref name="partyMembers"/> scopes tracks to the
    /// party/raid roster (null = every contributor).</summary>
    ReplayRecording OnBattleLogged(
        DpsLog log, IReadOnlyCollection<(string Nickname, int Server)>? partyMembers = null);

    /// <summary>Clear all buffered movement + stored recordings (wire to the meter reset / flush).</summary>
    void Reset();

    /// <summary>The most recently built recording = the 직전 전투 (or last cleared battle); null until one exists.</summary>
    ReplayRecording? LastRecording { get; }

    /// <summary>Look up a stored recording by its battle-start (matches a saved <c>DpsReport.BattleStart</c>).</summary>
    bool TryGetForBattle(long battleStartMs, out ReplayRecording? recording);
}

/// <summary>
/// Constructs an <see cref="IReplayEngine"/>. The private engine assembly exposes exactly one public
/// implementation of this (with a parameterless constructor) so <see cref="ReplayEngineLoader"/> can
/// find and instantiate it by reflection without a compile-time reference.
/// </summary>
public interface IReplayEngineFactory
{
    /// <param name="extraIdentity">Live resolver to include named movers who weren't damage contributors
    /// (supports). Pass a <see cref="DataManagerIdentitySource"/>; null to include only report contributors.</param>
    /// <param name="persistDir">If set, each recording is also written to <c>{persistDir}/replay-{startMs}.json</c>.</param>
    IReplayEngine Create(IReplayIdentitySource? extraIdentity, string? persistDir);
}

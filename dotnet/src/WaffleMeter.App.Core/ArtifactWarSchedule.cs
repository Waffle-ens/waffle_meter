namespace WaffleMeter.App.Core;

/// <summary>
/// What a server's own 아티팩트 점령전(아티쟁) time decides: where one 어비스 회랑 cycle ends and the next
/// begins. The source is not a clock but the window the server itself stamps on the 점령 현황 broadcast
/// (0xE305/0xE307 zone header, <see cref="AbyssArtifactStore.LatestWindow(int,long)"/>) — its end IS that
/// server's next war start, R.
///
/// <para><b>Why the clock had to go (2026-10-07).</b> Since the 09-30 patch the war no longer starts at one time
/// for everyone: servers are grouped at 21:20 / 21:50 / 22:20 (owner report). MEASURED on server 2003: the
/// 10-07 00:02 E307 carried end = Wed 10-07 21:20:00 for both zones, and the live client's <c>EventSchedule</c>
/// 3001/3002 now read Wed/Sat 21:20 — the other two groups are not in the client at all, they are the server's.
/// So the fixed Wed/Sat 22:20 snap / 22:25 give-up in <see cref="AbyssCorridorCycle"/> is wrong for two groups
/// out of three. Run against the live blob, the shipped build kept the previous cycle's corridors on a 21:20
/// server for 65 more minutes (until 22:25), and threw away a corridor spent at 21:47 — after that day's war —
/// at 22:25 as "last cycle's".</para>
///
/// <para><b>The rule.</b> While the window is still running (now &lt; end) the boundary is its start — the
/// earliest zone settle, since anything heard after the settle describes this occupation. Once now reaches the
/// end, the war has started and the boundary is the end itself: everything from the previous cycle is retired
/// on the spot, not at some later hour. Between R and the new settle nothing of the old cycle is shown; the
/// next 0xE305/0xE307 (sent from the settle on — the parser only accepts a frame whose event state is End)
/// brings the new window and its start takes over.</para>
///
/// <para><b>No window, the old clock.</b> A server the meter has never heard a window for (an alt's server, a
/// fresh install), or one whose window ended more than <see cref="AbyssArtifactStore.WindowStaleAfterMs"/>
/// ago, gets exactly the legacy <see cref="AbyssCorridorCycle"/> behaviour — 0 from
/// <see cref="CorridorBoundaryMs(AbyssArtifactWindow?,long)"/> is what tells the corridor store to use it.</para>
///
/// <para>Server epochs are compared with the local clock, the same assumption
/// <see cref="AbyssArtifactZoneState.IsCurrent"/> already makes. Pure, so the boundary is unit-testable.</para>
/// </summary>
public static class ArtifactWarSchedule
{
    /// <summary>The 회랑 cycle boundary a server's window gives at <paramref name="nowMs"/>: the window's start
    /// while it is running, its end once the war has started, or 0 when there is no window — which
    /// <see cref="AbyssCorridorStore"/> reads as "use the legacy clock".</summary>
    public static long CorridorBoundaryMs(AbyssArtifactWindow? window, long nowMs) =>
        window is { } w
            ? nowMs < w.EndMs ? w.StartMs : w.EndMs
            : 0;

    /// <summary>As <see cref="CorridorBoundaryMs(AbyssArtifactWindow?,long)"/>, for a server's newest window on
    /// file. 0 for an unknown server (id 0) or one with no usable window.</summary>
    public static long CorridorBoundaryMs(AbyssArtifactStore? artifacts, int serverId, long nowMs) =>
        serverId > 0 && artifacts is not null
            ? CorridorBoundaryMs(artifacts.LatestWindow(serverId, nowMs), nowMs)
            : 0;
}

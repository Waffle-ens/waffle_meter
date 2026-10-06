using WaffleMeter.Capture;

namespace WaffleMeter.App.Core;

/// <summary>
/// The field-boss timer supply the alarm polls once a second: <see cref="ArtifactWarSchedule.WithWarBossTargets"/>
/// plus a memory of the 아티쟁 spawns it has already derived this session.
///
/// <para><b>Why the memory.</b> A derived spawn is R + <see cref="ArtifactWarSchedule.BossSpawnMinutesAfterWarStart"/>
/// minutes, and R is the END of the stored window. But the war's own end brings the NEXT window: from the settle
/// on (21:37:57 for 하층 on 10-03 — some 18 minutes after a 21:20 start) the server sends 0xE305/0xE307 again —
/// measured landing in the live blob 56 s after that settle — and filing it moves the stored end to the following
/// war. Read statelessly,
/// today's spawn would vanish a few minutes before it happens, taking the 5-minute lead (R + 20) with it — and for
/// precisely the players standing in the abyss for the war. So a spawn once derived is kept until it has passed
/// (<see cref="ArtifactWarSchedule.BossTargetGraceMs"/>), per server, and the earlier of a remembered and a fresh
/// target is the one offered: today's, until it is over, then the next.</para>
///
/// <para>In-memory only, like the 0x9101 table it sits beside. UI-thread only (the alarm's DispatcherTimer).</para>
/// </summary>
public sealed class ArtifactWarBossTimers
{
    private readonly Dictionary<(int Server, int Code), long> _derived = new();

    /// <summary>The timers to evaluate at <paramref name="nowMs"/> — see
    /// <see cref="ArtifactWarSchedule.WithWarBossTargets"/> for the rules; the only difference is that a spawn
    /// already derived this session survives its window being replaced.</summary>
    public IReadOnlyDictionary<int, long> Merge(
        IReadOnlyDictionary<int, long> serverTimers, AbyssArtifactStore? artifacts, int currentServer, long nowMs)
    {
        foreach ((int Server, int Code) key in _derived
                     .Where(kv => kv.Value + ArtifactWarSchedule.BossTargetGraceMs < nowMs)
                     .Select(kv => kv.Key)
                     .ToList())
        {
            _derived.Remove(key);
        }

        int server = ArtifactWarSchedule.ServerFor(currentServer, artifacts);
        if (server <= 0)
        {
            return serverTimers;
        }

        var offered = new Dictionary<int, long>();
        foreach (int code in FieldBossFixedSchedule.ArtifactWarTiedCodes)
        {
            if (ArtifactWarSchedule.BossTargetMs(code, artifacts, server, nowMs) is long fresh)
            {
                _derived[(server, code)] = _derived.TryGetValue((server, code), out long kept)
                    ? Math.Min(kept, fresh)
                    : fresh;
            }

            if (_derived.TryGetValue((server, code), out long target))
            {
                offered[code] = target;
            }
        }

        return ArtifactWarSchedule.Merge(serverTimers, offered, nowMs);
    }
}

namespace WaffleMeter.Replay;

/// <summary>One point on an entity's path: a time offset (ms from the battle start) and world position.</summary>
/// <param name="TMs">Milliseconds since <see cref="ReplayRecording.StartMs"/>.</param>
/// <param name="X">World X (2D plot).</param>
/// <param name="Y">World Y (2D plot).</param>
/// <param name="Z">World Z (height; hover "(xx m)").</param>
public readonly record struct ReplayPoint(int TMs, float X, float Y, float Z);

/// <summary>One participant's movement path during a battle (a player, or the boss/target).</summary>
public sealed class ReplayTrack
{
    /// <summary>Entity uid (== damage actor id keyspace).</summary>
    public int Uid { get; init; }

    public string? Nickname { get; init; }

    public int Server { get; init; }

    /// <summary>Localized job/class name (e.g. 검성), or null if unknown.</summary>
    public string? Job { get; init; }

    /// <summary>The local player.</summary>
    public bool IsSelf { get; init; }

    /// <summary>The boss/target rather than a player participant.</summary>
    public bool IsTarget { get; init; }

    /// <summary>8-인 공대 sub-party slot 1-8 (1-4 = party 1, 5-8 = party 2), 0 if unknown.</summary>
    public int PartySlot { get; init; }

    /// <summary>Path points, ascending by <see cref="ReplayPoint.TMs"/>. May be empty when the entity
    /// participated in combat but no position broadcast was captured (e.g. interest-management / AoI gap).</summary>
    public IReadOnlyList<ReplayPoint> Points { get; init; } = [];

    /// <summary>The single (opcode, offset) layout the points were decoded at (diagnostics / RE confirmation).</summary>
    public int SourceOpcode { get; init; }

    public int SourceOffset { get; init; }
}

/// <summary>
/// A complete battle replay: the position timelines of every combat participant, scoped to the battle
/// window. Produced for an ENDED battle and equally for the standby "직전 전투" (a battle that stopped
/// without the boss dying) — the only difference is <see cref="BossDefeated"/>.
/// </summary>
public sealed class ReplayRecording
{
    /// <summary>Schema version of the on-disk / wire format.</summary>
    public const int CurrentSchema = 1;

    public int Schema { get; init; } = CurrentSchema;

    /// <summary>Reset epoch the battle ran under (matches the DPS battle identity).</summary>
    public long BattleEpoch { get; init; }

    /// <summary>Absolute wall-clock of the battle start (ms). Track points are relative to this.</summary>
    public long StartMs { get; init; }

    /// <summary>Absolute wall-clock of the battle end (ms).</summary>
    public long EndMs { get; init; }

    /// <summary>True if the boss reached 0 HP (a "cleared"/ended battle); false for a wipe/stop = 직전 전투.</summary>
    public bool BossDefeated { get; init; }

    public int? TargetCode { get; init; }

    public string? TargetName { get; init; }

    public IReadOnlyList<ReplayTrack> Tracks { get; init; } = [];

    public int DurationMs => (int)Math.Max(0, EndMs - StartMs);

    public int PointCount => Tracks.Sum(t => t.Points.Count);

    /// <summary>World-space bounds of all player/boss points (minX, minY, maxX, maxY), or null if empty.
    /// The renderer maps this to the flat map; Z (height) is intentionally excluded.</summary>
    public (float MinX, float MinY, float MaxX, float MaxY)? Bounds()
    {
        bool any = false;
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (ReplayTrack t in Tracks)
        {
            foreach (ReplayPoint p in t.Points)
            {
                any = true;
                if (p.X < minX) minX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.X > maxX) maxX = p.X;
                if (p.Y > maxY) maxY = p.Y;
            }
        }

        return any ? (minX, minY, maxX, maxY) : null;
    }
}

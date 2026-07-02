using WaffleMeter.Data;

namespace WaffleMeter.Replay;

/// <summary>Identity for a moving entity, resolved at recording-finalize time.</summary>
/// <param name="Found">Whether the uid was known (had an identity packet).</param>
public readonly record struct ReplayIdentity(
    bool Found,
    string? Nickname,
    int Server,
    string? Job,
    bool IsSelf);

/// <summary>
/// Resolves an entity uid to a human identity. Abstracted from <see cref="DataManager"/> so the recorder
/// is unit-testable without the full data layer, and so the resolution rule (which is timing-sensitive
/// due to entity-id reuse) lives in one place.
/// </summary>
public interface IReplayIdentitySource
{
    ReplayIdentity Resolve(int uid);

    /// <summary>The local player's uid (0 if unknown).</summary>
    int SelfUid { get; }
}

/// <summary>Adapter over the live <see cref="DataManager"/> repository.</summary>
public sealed class DataManagerIdentitySource(DataManager dm) : IReplayIdentitySource
{
    public int SelfUid => dm.ExecutorId();

    public ReplayIdentity Resolve(int uid)
    {
        User? u = dm.User(uid);
        if (u is null)
        {
            return new ReplayIdentity(false, null, 0, null, uid != 0 && uid == dm.ExecutorId());
        }

        return new ReplayIdentity(true, u.Nickname, u.Server, u.Job?.ClassName(), u.IsExecutor);
    }
}

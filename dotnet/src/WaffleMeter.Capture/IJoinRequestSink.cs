namespace WaffleMeter.Capture;

/// <summary>
/// App-facing channel for party join-request events (Kotlin PacketEvent.JoinRequest / JoinRequestRemove
/// / RefuseJoinRequest / ExitPartyUI). Kept separate from <see cref="IStreamProcessorSink"/> (which is
/// for diagnostics) and uses primitives only, so <c>WaffleMeter.Capture</c> stays free of a dependency
/// on the data/domain layer — App.Core resolves the job name and builds the JoinRequestUser.
/// </summary>
public interface IJoinRequestSink
{
    /// <summary>A join request arrived (or was refreshed). Add/replace by <paramref name="requester"/>.</summary>
    void OnJoinRequest(int requester, string nickname, int jobCode, int server, int power, long arrivedAt);

    /// <summary>The request was cancelled by the applicant or admitted by the leader — remove it.</summary>
    void OnJoinRequestRemove(int requester);

    /// <summary>A refusal with no id — drop the oldest pending request.</summary>
    void OnRefuseJoinRequest();

    /// <summary>Instance start or party exit — clear all pending requests.</summary>
    void OnExitPartyUi();
}

/// <summary>No-op sink (default when the app does not wire join handling, e.g. replay/tests).</summary>
public sealed class NullJoinRequestSink : IJoinRequestSink
{
    public static readonly NullJoinRequestSink Instance = new();
    public void OnJoinRequest(int requester, string nickname, int jobCode, int server, int power, long arrivedAt) { }
    public void OnJoinRequestRemove(int requester) { }
    public void OnRefuseJoinRequest() { }
    public void OnExitPartyUi() { }
}

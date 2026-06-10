using System.Globalization;
using WaffleMeter.Data;
using WaffleMeter.Services;

namespace WaffleMeter.Stats;

/// <summary>
/// Verbatim port of Kotlin <c>stats.StatsConsentManager</c>: the consent state machine. Persists
/// state to settings (via <see cref="PropertyHandler"/>) and reconciles with the backend
/// (<see cref="StatsApiClient"/>). Dependencies are injected (props, data, api, own-character
/// provider, clock) so it is testable; the own-character provider is supplied by the payload builder
/// in the live app.
/// </summary>
public sealed class StatsConsentManager
{
    public const string ConsentVersion = "2026-06-04";

    private const string KeyState = "statsConsentState";
    private const string KeyUploadEnabled = "statsUploadEnabled";
    private const string KeyPublicCharacter = "statsPublicCharacter";
    private const string KeyConsentVersion = "statsConsentVersion";
    private const string KeyUpdatedAt = "statsConsentUpdatedAt";
    private const string KeyIdentityHash = "statsConsentIdentityHash";
    private const string KeyRemoteExists = "statsConsentRemoteExists";
    private const string KeySyncStatus = "statsConsentSyncStatus";
    private const string KeySyncError = "statsConsentSyncError";
    private const string KeyServerUpdatedAt = "statsConsentServerUpdatedAt";
    private const string KeyLastSeenAt = "statsConsentLastSeenAt";

    // Interned sentinel: SaveLocal's identityHash defaults to "the current character's hash"; an
    // explicit value (including null, from a remote response) overrides it.
    private const string KeepCurrentIdentity = "￿__use_current_identity__";

    public enum State
    {
        unknown,
        accepted,
        declined,
        revoked,
    }

    public sealed record Info(
        string State,
        bool UploadEnabled,
        bool PublicCharacter,
        string ConsentVersion,
        long UpdatedAt,
        string? IdentityHash = null,
        bool RemoteExists = false,
        string SyncStatus = "local",
        string? SyncError = null,
        string? ServerUpdatedAt = null,
        string? LastSeenAt = null);

    private readonly PropertyHandler _props;
    private readonly DataManager _data;
    private readonly StatsApiClient _api;
    private readonly Func<StatsOwnCharacter> _ownCharacter;
    private readonly Func<long> _clock;

    public StatsConsentManager(
        PropertyHandler props,
        DataManager data,
        StatsApiClient api,
        Func<StatsOwnCharacter> ownCharacter,
        Func<long>? clock = null)
    {
        _props = props;
        _data = data;
        _api = api;
        _ownCharacter = ownCharacter;
        _clock = clock ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    public Info GetInfo(bool syncRemote = false, string clientVersion = "dev") =>
        syncRemote ? RefreshFromServer(clientVersion) : LocalInfo();

    public Info Set(string state, bool uploadEnabled, bool publicCharacter, string clientVersion = "dev")
    {
        State next = TryState(state) ?? State.unknown;
        switch (next)
        {
            case State.accepted:
                return Accept(uploadEnabled, publicCharacter, clientVersion);
            case State.revoked:
                return Revoke(clientVersion);
            case State.declined:
                SaveLocal(State.declined, uploadEnabled: false, publicCharacter: false, syncStatus: "local_declined");
                return LocalInfo();
            default:
                SaveLocal(State.unknown, uploadEnabled: false, publicCharacter: false, syncStatus: "local_unknown");
                return LocalInfo();
        }
    }

    public bool IsUploadAllowed()
    {
        Info current = LocalInfo();
        return current.State == State.accepted.ToString() && current.UploadEnabled;
    }

    private Info RefreshFromServer(string clientVersion)
    {
        string? identityHash = CurrentIdentityHash();
        if (identityHash == null)
        {
            RememberSync("identity_missing", null);
            return LocalInfo();
        }

        try
        {
            ConsentStatusResponse response = _api.GetConsentStatus(identityHash);
            Info current = LocalInfo();
            bool requestedUpload = current.State == State.accepted.ToString() ? current.UploadEnabled : true;
            return ApplyRemote(response, requestedUpload);
        }
        catch (Exception e)
        {
            RememberSync("sync_failed", Summarize(e));
            return LocalInfo();
        }
    }

    private Info Accept(bool uploadEnabled, bool publicCharacter, string clientVersion)
    {
        ConsentEventCharacter? character = CurrentConsentCharacter(publicCharacter);
        if (character == null)
        {
            SaveLocal(State.accepted, uploadEnabled: false, publicCharacter: publicCharacter, syncStatus: "identity_missing");
            return LocalInfo();
        }

        try
        {
            ConsentStatusResponse response = _api.PostConsentEvent(
                new ConsentEventRequest(State.accepted.ToString(), ConsentVersion, Character: character),
                clientVersion);
            return ApplyRemote(response, requestedUpload: uploadEnabled);
        }
        catch (Exception e)
        {
            SaveLocal(
                State.accepted,
                uploadEnabled: false,
                publicCharacter: publicCharacter,
                syncStatus: "sync_failed",
                identityHash: character.IdentityHash,
                syncError: Summarize(e));
            return LocalInfo();
        }
    }

    private Info Revoke(string clientVersion)
    {
        string? identityHash = CurrentIdentityHash() ?? NonBlank(_props.GetProperty(KeyIdentityHash));
        if (identityHash == null)
        {
            SaveLocal(State.revoked, uploadEnabled: false, publicCharacter: false, syncStatus: "identity_missing");
            return LocalInfo();
        }

        try
        {
            ConsentStatusResponse response = _api.PostConsentEvent(
                new ConsentEventRequest(State.revoked.ToString(), ConsentVersion, IdentityHash: identityHash),
                clientVersion);
            return ApplyRemote(response, requestedUpload: false);
        }
        catch (Exception e)
        {
            SaveLocal(
                State.revoked,
                uploadEnabled: false,
                publicCharacter: false,
                syncStatus: "sync_failed",
                identityHash: identityHash,
                syncError: Summarize(e));
            return LocalInfo();
        }
    }

    private Info ApplyRemote(ConsentStatusResponse response, bool requestedUpload)
    {
        State remoteState = TryState(response.ConsentState) ?? State.unknown;
        bool accepted = remoteState == State.accepted && response.Exists;
        bool publicCharacter = accepted && response.PublicCharacter;
        SaveLocal(
            remoteState,
            uploadEnabled: accepted && requestedUpload,
            publicCharacter: publicCharacter,
            syncStatus: "synced",
            consentVersion: response.ConsentVersion ?? ConsentVersion,
            updatedAt: ParseRemoteTime(response.UpdatedAt) ?? _clock(),
            identityHash: response.IdentityHash,
            remoteExists: response.Exists,
            syncError: null,
            serverUpdatedAt: response.UpdatedAt,
            lastSeenAt: response.LastSeenAt);
        return LocalInfo();
    }

    private ConsentEventCharacter? CurrentConsentCharacter(bool publicCharacter)
    {
        User? user = _data.User(_data.ExecutorId());
        string? nickname = NonBlank(user?.Nickname);
        if (user == null || nickname == null)
        {
            return null;
        }

        string? identityHash = StatsIdentity.CharacterIdentityHash(user.Server, nickname);
        if (identityHash == null)
        {
            return null;
        }

        StatsOwnCharacter resolved = _ownCharacter();
        return new ConsentEventCharacter(
            identityHash,
            nickname,
            user.Server,
            publicCharacter,
            Job: resolved.Job ?? user.Job?.ClassName(),
            Power: resolved.Power);
    }

    private string? CurrentIdentityHash()
    {
        User? user = _data.User(_data.ExecutorId());
        return user == null ? null : StatsIdentity.CharacterIdentityHash(user.Server, user.Nickname);
    }

    private Info LocalInfo()
    {
        State state = TryState(_props.GetProperty(KeyState)) ?? State.unknown;
        bool uploadEnabled = ParseBoolStrict(_props.GetProperty(KeyUploadEnabled)) ?? false;
        bool publicCharacter = ParseBoolStrict(_props.GetProperty(KeyPublicCharacter)) ?? false;
        string consentVersion = _props.GetProperty(KeyConsentVersion) ?? ConsentVersion;
        long updatedAt = long.TryParse(_props.GetProperty(KeyUpdatedAt), NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed) ? parsed : 0L;
        string? identityHash = NonBlank(_props.GetProperty(KeyIdentityHash));
        bool remoteExists = ParseBoolStrict(_props.GetProperty(KeyRemoteExists)) ?? false;
        string syncStatus = _props.GetProperty(KeySyncStatus) ?? "local";
        string? syncError = NonBlank(_props.GetProperty(KeySyncError));
        string? serverUpdatedAt = NonBlank(_props.GetProperty(KeyServerUpdatedAt));
        string? lastSeenAt = NonBlank(_props.GetProperty(KeyLastSeenAt));
        return new Info(
            state.ToString(),
            state == State.accepted && uploadEnabled,
            state == State.accepted && publicCharacter,
            consentVersion,
            updatedAt,
            identityHash,
            remoteExists,
            syncStatus,
            syncError,
            serverUpdatedAt,
            lastSeenAt);
    }

    private void SaveLocal(
        State state,
        bool uploadEnabled,
        bool publicCharacter,
        string syncStatus,
        string? consentVersion = null,
        long? updatedAt = null,
        string? identityHash = KeepCurrentIdentity,
        bool remoteExists = false,
        string? syncError = null,
        string? serverUpdatedAt = null,
        string? lastSeenAt = null)
    {
        string? resolvedIdentity = ReferenceEquals(identityHash, KeepCurrentIdentity) ? CurrentIdentityHash() : identityHash;
        string version = consentVersion ?? ConsentVersion;
        long updated = updatedAt ?? _clock();

        _props.SetProperty(KeyState, state.ToString());
        _props.SetProperty(KeyUploadEnabled, BoolStr(state == State.accepted && uploadEnabled));
        _props.SetProperty(KeyPublicCharacter, BoolStr(state == State.accepted && publicCharacter));
        _props.SetProperty(KeyConsentVersion, version);
        _props.SetProperty(KeyUpdatedAt, updated.ToString(CultureInfo.InvariantCulture));
        _props.SetProperty(KeyIdentityHash, resolvedIdentity ?? string.Empty);
        _props.SetProperty(KeyRemoteExists, BoolStr(remoteExists));
        _props.SetProperty(KeySyncStatus, syncStatus);
        _props.SetProperty(KeySyncError, syncError ?? string.Empty);
        _props.SetProperty(KeyServerUpdatedAt, serverUpdatedAt ?? string.Empty);
        _props.SetProperty(KeyLastSeenAt, lastSeenAt ?? string.Empty);
    }

    private void RememberSync(string status, string? error)
    {
        _props.SetProperty(KeySyncStatus, status);
        _props.SetProperty(KeySyncError, error ?? string.Empty);
    }

    private static long? ParseRemoteTime(string? value)
    {
        if (value == null)
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsed)
            ? parsed.ToUnixTimeMilliseconds()
            : null;
    }

    // Kotlin Boolean.toString() / toBooleanStrictOrNull() are lowercase-exact; match that so files
    // round-trip with the Kotlin app (C# bool.ToString() would write "True").
    private static string BoolStr(bool value) => value ? "true" : "false";

    private static bool? ParseBoolStrict(string? value) => value switch
    {
        "true" => true,
        "false" => false,
        _ => null,
    };

    private static string? NonBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static State? TryState(string? value)
    {
        if (value != null && Enum.TryParse(value, ignoreCase: false, out State state) && Enum.IsDefined(state))
        {
            return state;
        }

        return null;
    }

    private static string Summarize(Exception e)
    {
        string? message = e.Message;
        if (!string.IsNullOrWhiteSpace(message))
        {
            return message.Length > 160 ? message[..160] : message;
        }

        return e.GetType().Name;
    }
}

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    // Per-character consent memory (req 2): identityHash -> remembered decision, so switching
    // characters never re-prompts an already-decided one. Session-level sync metadata (syncStatus
    // etc.) stays in the global keys above. The global state/identity keys are kept as a migration /
    // no-character fallback.
    private const string KeyCharacters = "statsConsentCharacters";

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

    /// <summary>True if the CURRENT character has an accepted consent on record (live or remembered).</summary>
    public bool IsCurrentCharacterConsented() => LocalInfo().State == State.accepted.ToString();

    /// <summary>True when an own character is detected but has no consent decision yet — the UI should
    /// show the consent modal (React: modal opens when state is unknown for the detected character).</summary>
    public bool NeedsConsentPrompt() => _ownCharacter().Detected && LocalInfo().State == State.unknown.ToString();

    /// <summary>Identity hash of the current character (lets the UI prompt once per character).</summary>
    public string? CurrentCharacterHash() => CurrentIdentityHash();

    /// <summary>Identity hashes of all locally-remembered consented characters (the consented list).</summary>
    public IReadOnlyList<string> ConsentedCharacterHashes() =>
        LoadCharacters().Where(kv => TryState(kv.Value.State) == State.accepted).Select(kv => kv.Key).ToList();

    /// <summary>One locally-remembered character's consent, for the per-character management UI.</summary>
    public sealed record CharacterConsentInfo(
        string IdentityHash, string? Nickname, int Server, string? Job, string State,
        bool UploadEnabled, bool PublicCharacter, long UpdatedAt, bool IsCurrent, bool CanSetPublic);

    /// <summary>All locally-remembered characters and their consent (the management list), current character
    /// first then most-recently-updated. <c>CanSetPublic</c> is false for a legacy entry that has no stored
    /// nickname/server and isn't the current character — its accept event can't be rebuilt, so its public
    /// flag can't be changed (it can still be revoked, which needs only the hash).</summary>
    public IReadOnlyList<CharacterConsentInfo> ListCharacters()
    {
        string? current = CurrentIdentityHash();
        return LoadCharacters()
            .Select(kv =>
            {
                CharConsent c = kv.Value;
                bool isCurrent = kv.Key == current;
                bool named = !string.IsNullOrWhiteSpace(c.Nickname) && c.Server > 0;
                return new CharacterConsentInfo(
                    kv.Key, c.Nickname, c.Server, c.Job, c.State, c.UploadEnabled, c.PublicCharacter,
                    c.UpdatedAt, isCurrent, CanSetPublic: isCurrent || named);
            })
            .OrderByDescending(c => c.IsCurrent)
            .ThenByDescending(c => c.UpdatedAt)
            .ToList();
    }

    /// <summary>Set a SPECIFIC character's public flag. The current character routes through the normal
    /// accept path; a non-current character re-issues an accept event from its stored identity (name+server)
    /// and updates only its own remembered entry. No-op if the character isn't accepted or can't be rebuilt.</summary>
    public void SetCharacterPublic(string identityHash, bool publicCharacter, string clientVersion = "dev")
    {
        if (identityHash == CurrentIdentityHash())
        {
            Accept(LocalInfo().UploadEnabled, publicCharacter, clientVersion);
            return;
        }

        Dictionary<string, CharConsent> map = LoadCharacters();
        if (!map.TryGetValue(identityHash, out CharConsent? entry)
            || TryState(entry.State) != State.accepted
            || string.IsNullOrWhiteSpace(entry.Nickname)
            || entry.Server <= 0)
        {
            return;
        }

        var character = new ConsentEventCharacter(identityHash, entry.Nickname!, entry.Server, publicCharacter, entry.Job, 0);
        try
        {
            ConsentStatusResponse response = _api.PostConsentEvent(
                new ConsentEventRequest(State.accepted.ToString(), ConsentVersion, Character: character), clientVersion);
            bool accepted = (TryState(response.ConsentState) ?? State.unknown) == State.accepted && response.Exists;
            UpsertCharacter(identityHash, accepted ? State.accepted : State.declined, entry.UploadEnabled,
                accepted && response.PublicCharacter, response.ConsentVersion ?? ConsentVersion,
                ParseRemoteTime(response.UpdatedAt) ?? _clock(), entry.Nickname, entry.Server, entry.Job);
        }
        catch
        {
            // leave the entry unchanged on a failed sync; the list re-reads the old state
        }
    }

    /// <summary>Revoke a SPECIFIC character's consent. The current character routes through the normal
    /// revoke path; a non-current character posts a revoke event (hash only) and marks its remembered entry
    /// revoked — even on a failed sync, so future uploads stop locally regardless.</summary>
    public void RevokeCharacter(string identityHash, string clientVersion = "dev")
    {
        if (identityHash == CurrentIdentityHash())
        {
            Revoke(clientVersion);
            return;
        }

        LoadCharacters().TryGetValue(identityHash, out CharConsent? entry);
        try
        {
            ConsentStatusResponse response = _api.PostConsentEvent(
                new ConsentEventRequest(State.revoked.ToString(), ConsentVersion, IdentityHash: identityHash), clientVersion);
            UpsertCharacter(identityHash, State.revoked, false, false,
                response.ConsentVersion ?? ConsentVersion, ParseRemoteTime(response.UpdatedAt) ?? _clock(),
                entry?.Nickname, entry?.Server ?? 0, entry?.Job);
        }
        catch
        {
            UpsertCharacter(identityHash, State.revoked, false, false, ConsentVersion, _clock(),
                entry?.Nickname, entry?.Server ?? 0, entry?.Job);
        }
    }

    private Info LocalInfo()
    {
        // Session-level sync metadata is always global (it describes the last sync op, not a character).
        bool remoteExists = ParseBoolStrict(_props.GetProperty(KeyRemoteExists)) ?? false;
        string syncStatus = _props.GetProperty(KeySyncStatus) ?? "local";
        string? syncError = NonBlank(_props.GetProperty(KeySyncError));
        string? serverUpdatedAt = NonBlank(_props.GetProperty(KeyServerUpdatedAt));
        string? lastSeenAt = NonBlank(_props.GetProperty(KeyLastSeenAt));

        string? currentHash = CurrentIdentityHash();
        Dictionary<string, CharConsent> map = LoadCharacters();

        // 1) Remembered decision for the current character -> use it (no re-prompt).
        if (currentHash != null && map.TryGetValue(currentHash, out CharConsent? entry))
        {
            State st = TryState(entry.State) ?? State.unknown;
            return new Info(
                st.ToString(),
                st == State.accepted && entry.UploadEnabled,
                st == State.accepted && entry.PublicCharacter,
                entry.ConsentVersion ?? ConsentVersion,
                entry.UpdatedAt,
                currentHash,
                remoteExists, syncStatus, syncError, serverUpdatedAt, lastSeenAt);
        }

        // 2) Migration / no-character fallback: use the legacy global keys only when they belong to the
        //    current character (a pre-feature consent) or no character is detected yet.
        string? globalIdentity = NonBlank(_props.GetProperty(KeyIdentityHash));
        if (currentHash == null || globalIdentity == currentHash)
        {
            return GlobalInfo(remoteExists, syncStatus, syncError, serverUpdatedAt, lastSeenAt);
        }

        // 3) A detected character with no decision on record -> unknown (eligible to be prompted).
        return new Info(State.unknown.ToString(), false, false, ConsentVersion, 0, currentHash,
            remoteExists, syncStatus, syncError, serverUpdatedAt, lastSeenAt);
    }

    private Info GlobalInfo(bool remoteExists, string syncStatus, string? syncError, string? serverUpdatedAt, string? lastSeenAt)
    {
        State state = TryState(_props.GetProperty(KeyState)) ?? State.unknown;
        bool uploadEnabled = ParseBoolStrict(_props.GetProperty(KeyUploadEnabled)) ?? false;
        bool publicCharacter = ParseBoolStrict(_props.GetProperty(KeyPublicCharacter)) ?? false;
        string consentVersion = _props.GetProperty(KeyConsentVersion) ?? ConsentVersion;
        long updatedAt = long.TryParse(_props.GetProperty(KeyUpdatedAt), NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed) ? parsed : 0L;
        string? identityHash = NonBlank(_props.GetProperty(KeyIdentityHash));
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

    private Dictionary<string, CharConsent> LoadCharacters()
    {
        string? raw = _props.GetProperty(KeyCharacters);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new();
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, CharConsent>>(raw) ?? new();
        }
        catch
        {
            return new();
        }
    }

    private void UpsertCharacter(string identityHash, State state, bool uploadEnabled, bool publicCharacter,
        string version, long updatedAt, string? nickname = null, int server = 0, string? job = null)
    {
        Dictionary<string, CharConsent> map = LoadCharacters();
        CharConsent entry = map.TryGetValue(identityHash, out CharConsent? existing) ? existing : new CharConsent();
        entry.State = state.ToString();
        entry.UploadEnabled = state == State.accepted && uploadEnabled;
        entry.PublicCharacter = state == State.accepted && publicCharacter;
        entry.ConsentVersion = version;
        entry.UpdatedAt = updatedAt;
        // Preserve previously-stored identity metadata when this call doesn't supply it (e.g. a hash-only
        // revoke of a non-current character).
        if (!string.IsNullOrWhiteSpace(nickname)) entry.Nickname = nickname;
        if (server > 0) entry.Server = server;
        if (!string.IsNullOrWhiteSpace(job)) entry.Job = job;
        map[identityHash] = entry;
        _props.SetProperty(KeyCharacters, JsonSerializer.Serialize(map));
    }

    private sealed class CharConsent
    {
        [JsonPropertyName("state")] public string State { get; set; } = "unknown";
        [JsonPropertyName("uploadEnabled")] public bool UploadEnabled { get; set; }
        [JsonPropertyName("publicCharacter")] public bool PublicCharacter { get; set; }
        [JsonPropertyName("consentVersion")] public string? ConsentVersion { get; set; }
        [JsonPropertyName("updatedAt")] public long UpdatedAt { get; set; }
        // Display metadata for the per-character management UI (so it can label characters by name, not the
        // raw hash). MUST stay on the DEFAULT serializer so Korean is \uXXXX-escaped to ASCII and survives
        // the EUC-KR settings encoding — StatsJson's relaxed escaping would corrupt raw Korean here.
        [JsonPropertyName("nickname")] public string? Nickname { get; set; }
        [JsonPropertyName("server")] public int Server { get; set; }
        [JsonPropertyName("job")] public string? Job { get; set; }
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

        // Remember this decision against the CURRENT character (req 2), keyed by the locally-computed
        // identity hash — which is exactly what LocalInfo looks up (the server may echo a different/
        // stubbed hash into the global key, but the per-character map must match the local lookup). When
        // no character is detected, only the global keys are written.
        string? mapKey = CurrentIdentityHash();
        if (mapKey != null)
        {
            // Stamp the current character's display identity (name/server/job) so the management UI can
            // label it; resolve from the live executor (and the own-character provider for job).
            User? u = _data.User(_data.ExecutorId());
            string? job = NonBlank(_ownCharacter().Job) ?? (u?.Job is { } jc ? jc.ClassName() : null);
            UpsertCharacter(mapKey, state, uploadEnabled, publicCharacter, version, updated,
                NonBlank(u?.Nickname), u?.Server ?? 0, job);
        }
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

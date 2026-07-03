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
        // A character's shared public flag may only be CHANGED (turned ON or OFF) by an install that OWNS
        // the character (holds its grant). A non-owning install (e.g. a PC방/재설치 install with no grant)
        // must NOT touch the flag: it sends the accept with `public` OMITTED so the server PRESERVES whatever
        // it has, and therefore can never downgrade a character another install legitimately made public.
        // (Privacy fail-safe is unaffected: 철회/revoke is always allowed and needs no grant.)
        string? currentHash = CurrentIdentityHash();
        bool owns = currentHash != null && HasGrant(currentHash);
        bool? publicIntent = publicCharacter ? true : (owns ? false : (bool?)null);

        ConsentEventCharacter? character = CurrentConsentCharacter(publicIntent);
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
        catch (Exception e) when (publicCharacter && IsPublicOwnershipRejection(e))
        {
            // Public refused (no grant). Re-affirm consent WITHOUT asserting public (omit → server preserves),
            // never downgrading a shared row, then surface the ownership notice. Replaces the old destructive
            // "re-accept as private" rollback that overwrote public=false and un-published the character.
            return AcceptPublicUnmanaged(character, uploadEnabled, clientVersion);
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

    /// <summary>Fallback when accept+public was refused for lack of grant: re-affirm consent with the public
    /// flag OMITTED (public=null) so the server PRESERVES the character's existing public state — never
    /// downgrading a row an owning install already made public. Reflect the server's real state locally, and
    /// only stamp the public_requires_ownership notice when the character is genuinely still private (there is
    /// nothing to warn about if another owning install has already made it public).</summary>
    private Info AcceptPublicUnmanaged(ConsentEventCharacter character, bool uploadEnabled, string clientVersion)
    {
        ConsentEventCharacter consentOnly = character with { PublicCharacter = null };
        bool serverPublic;
        try
        {
            ConsentStatusResponse response = _api.PostConsentEvent(
                new ConsentEventRequest(State.accepted.ToString(), ConsentVersion, Character: consentOnly), clientVersion);
            ApplyRemote(response, requestedUpload: uploadEnabled);
            serverPublic = (TryState(response.ConsentState) ?? State.unknown) == State.accepted
                && response.Exists && response.PublicCharacter;
        }
        catch (Exception e)
        {
            SaveLocal(State.accepted, uploadEnabled: false, publicCharacter: false,
                syncStatus: "sync_failed", identityHash: consentOnly.IdentityHash, syncError: Summarize(e));
            return LocalInfo();
        }

        if (!serverPublic)
        {
            RememberSync(PublicRequiresOwnership, null);
        }
        return LocalInfo();
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
            lastSeenAt: response.LastSeenAt,
            grant: response.Granted);
        return LocalInfo();
    }

    private ConsentEventCharacter? CurrentConsentCharacter(bool? publicCharacter)
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
        bool UploadEnabled, bool PublicCharacter, long UpdatedAt, bool IsCurrent, bool CanSetPublic,
        // True once this install holds the server-side grant for this character (cached from an upload/accept
        // response). The "공개" toggle needs grant; see W18 (Grant || IsCurrent allows the attempt).
        bool Grant = false);

    /// <summary>All locally-remembered characters and their consent (the management list), current character
    /// first then most-recently-updated. <c>CanSetPublic</c> is false for a legacy entry that has no stored
    /// nickname/server and isn't the current character — its accept event can't be rebuilt, so its public
    /// flag can't be changed (it can still be revoked, which needs only the hash).</summary>
    public IReadOnlyList<CharacterConsentInfo> ListCharacters()
    {
        string? current = CurrentIdentityHash();
        // The CURRENT character's display name may be missing from its stored entry (synced in a prior session,
        // or saved before its nickname was recognized). Resolve it LIVE from the executor so "내 캐릭터 관리"
        // shows the real name instead of "이름 없음" right away (BackfillCurrentCharacterIdentity persists it).
        User? liveSelf = current != null ? _data.User(_data.ExecutorId()) : null;
        string? liveNick = NonBlank(liveSelf?.Nickname);
        return LoadCharacters()
            .Select(kv =>
            {
                CharConsent c = kv.Value;
                bool isCurrent = kv.Key == current;
                string? nickname = c.Nickname;
                int server = c.Server;
                string? job = c.Job;
                if (isCurrent && liveNick != null)
                {
                    nickname = liveNick;
                    if (liveSelf!.Server > 0) server = liveSelf.Server;
                    job ??= NonBlank(_ownCharacter().Job) ?? liveSelf.Job?.ClassName();
                }

                bool named = !string.IsNullOrWhiteSpace(nickname) && server > 0;
                return new CharacterConsentInfo(
                    kv.Key, nickname, server, job, c.State, c.UploadEnabled, c.PublicCharacter,
                    c.UpdatedAt, isCurrent, CanSetPublic: isCurrent || named, Grant: c.Grant);
            })
            // Hide name-less legacy records (consented in a prior session before names were stored): a list of
            // "이름 없음 (이전 기록)" rows is confusing. The consent record stays (uploads honor the prior decision);
            // the character reappears here — named — the moment it reconnects (BackfillCurrentCharacterIdentity).
            .Where(c => !string.IsNullOrWhiteSpace(c.Nickname))
            .OrderByDescending(c => c.IsCurrent)
            .ThenByDescending(c => c.UpdatedAt)
            .ToList();
    }

    /// <summary>Fill in the CURRENT character's display name (nickname/server/job) on its stored consent record
    /// from the live executor — local only, never uploaded — so the management list shows the real name even
    /// after the character disconnects. Idempotent: no-op when nothing changed or the character has no record.</summary>
    public void BackfillCurrentCharacterIdentity()
    {
        User? u = _data.User(_data.ExecutorId());
        string? nickname = NonBlank(u?.Nickname);
        if (u == null || nickname == null)
        {
            return;
        }

        string? hash = StatsIdentity.CharacterIdentityHash(u.Server, nickname);
        if (hash == null)
        {
            return;
        }

        Dictionary<string, CharConsent> map = LoadCharacters();
        if (!map.TryGetValue(hash, out CharConsent? entry))
        {
            return; // only label characters that already have a consent record (don't create one here)
        }

        string? job = NonBlank(_ownCharacter().Job) ?? (u.Job is { } jc ? jc.ClassName() : null);
        bool changed = entry.Nickname != nickname
            || (u.Server > 0 && entry.Server != u.Server)
            || (job != null && entry.Job != job);
        if (!changed)
        {
            return;
        }

        entry.Nickname = nickname;
        if (u.Server > 0) entry.Server = u.Server;
        if (job != null) entry.Job = job;
        map[hash] = entry;
        _props.SetProperty(KeyCharacters, JsonSerializer.Serialize(map));
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

        // W18 gate (§2.4): turning a NON-current character public needs this install's grant. Making it
        // private is always allowed (privacy reduction). Block the ungranted public attempt + inform.
        if (publicCharacter && !entry.Grant)
        {
            RememberSync(PublicRequiresOwnership, null);
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
                ParseRemoteTime(response.UpdatedAt) ?? _clock(), entry.Nickname, entry.Server, entry.Job,
                grant: response.Granted);
            RememberSync("synced", null); // clear any stale public_requires_ownership notice from a prior action
        }
        catch (Exception e) when (publicCharacter && IsPublicOwnershipRejection(e))
        {
            // Grant cache was stale; the server refused. Roll the toggle back to private + inform.
            UpsertCharacter(identityHash, State.accepted, entry.UploadEnabled, publicCharacter: false,
                entry.ConsentVersion ?? ConsentVersion, _clock(), entry.Nickname, entry.Server, entry.Job);
            RememberSync(PublicRequiresOwnership, null);
        }
        catch
        {
            // leave the entry unchanged on a generic failed sync; the list re-reads the old state
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
        string version, long updatedAt, string? nickname = null, int server = 0, string? job = null, bool grant = false)
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
        if (grant) entry.Grant = true; // monotone: a response can confer grant, never withdraw it
        map[identityHash] = entry;
        _props.SetProperty(KeyCharacters, JsonSerializer.Serialize(map));
    }

    /// <summary>Mark a character as granted (server confirmed this install owns it). Called after a successful
    /// signed upload whose response carries <c>granted</c>. Grant-only: never changes consent state, and only
    /// ever sets the flag true (the server grant set is monotone). No-op if already granted.</summary>
    public void MarkGranted(string identityHash)
    {
        if (string.IsNullOrWhiteSpace(identityHash))
        {
            return;
        }

        Dictionary<string, CharConsent> map = LoadCharacters();
        CharConsent entry = map.TryGetValue(identityHash, out CharConsent? existing) ? existing : new CharConsent();
        if (entry.Grant)
        {
            return; // already granted — avoid a redundant settings write from the upload thread
        }

        entry.Grant = true;
        map[identityHash] = entry;
        _props.SetProperty(KeyCharacters, JsonSerializer.Serialize(map));
    }

    /// <summary>True when this install holds the server-side grant for <paramref name="identityHash"/> (cached).
    /// Used by the public-transition gate (§2.4) and the management UI.</summary>
    public bool HasGrant(string identityHash) =>
        !string.IsNullOrWhiteSpace(identityHash)
        && LoadCharacters().TryGetValue(identityHash, out CharConsent? entry)
        && entry.Grant;

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
        // Character grant (§2.2): true once this install uploaded/accepted with this character so the server
        // granted ownership — gates the "공개" toggle. Cache only; the server is the authority. Grant is a
        // monotone TOFU set on the server, so locally we only ever set it true, never clear it.
        [JsonPropertyName("grant")] public bool Grant { get; set; }
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
        string? lastSeenAt = null,
        bool grant = false)
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
                NonBlank(u?.Nickname), u?.Server ?? 0, job, grant);
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

    /// <summary>Server error code (§2.4) returned when a public transition lacks the required grant. Surfaced
    /// to the UI as a sync status so it can roll the toggle back + show a localized notice. ASCII-only — never
    /// store a Korean message in the global sync keys (the EUC-KR settings re-decode would corrupt it).</summary>
    public const string PublicRequiresOwnership = "public_requires_ownership";

    private static bool IsPublicOwnershipRejection(Exception e) =>
        (e is StatsApiException sae && sae.ResponseBody?.Contains(PublicRequiresOwnership, StringComparison.Ordinal) == true)
        || (e.Message?.Contains(PublicRequiresOwnership, StringComparison.Ordinal) == true);

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

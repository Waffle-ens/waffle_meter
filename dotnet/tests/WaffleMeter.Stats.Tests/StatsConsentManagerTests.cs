using WaffleMeter.Data;
using WaffleMeter.Services;
using WaffleMeter.Stats;
using Xunit;

namespace WaffleMeter.Stats.Tests;

public sealed class StatsConsentManagerTests : IDisposable
{
    private readonly string _tempAppData;
    private readonly PropertyHandler _props;
    private readonly DataManager _data = new();

    public StatsConsentManagerTests()
    {
        _tempAppData = Path.Combine(Path.GetTempPath(), "wm_consent_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempAppData);
        _props = new PropertyHandler(_tempAppData);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempAppData, recursive: true);
        }
        catch
        {
            // best effort
        }
    }

    private StatsConsentManager Manager(StatsApiClient api) => new(
        _props,
        _data,
        api,
        ownCharacter: () => new StatsOwnCharacter(true, 1, "Hero", 3, "마도성", 5000),
        clock: () => 1_700_000_000_000);

    private static StatsApiClient ApiReturning(string body) =>
        new(() => "install-1", (_, _, _, _) => new StatsHttpResponse(200, body));

    private static StatsApiClient ApiFailing() =>
        new(() => "install-1", (_, _, _, _) => new StatsHttpResponse(500, "server_down"));

    private void GiveExecutor() => _data.SaveNickname(1, "Hero", isExecutor: true, server: 3, jobByte: 0);

    [Fact]
    public void Declined_persists_locally_without_upload()
    {
        StatsConsentManager manager = Manager(ApiFailing());

        StatsConsentManager.Info info = manager.Set("declined", uploadEnabled: true, publicCharacter: true);

        Assert.Equal("declined", info.State);
        Assert.False(info.UploadEnabled);
        Assert.Equal("local_declined", info.SyncStatus);
        Assert.False(manager.IsUploadAllowed());

        // Persisted: a fresh manager reads the same.
        Assert.Equal("declined", Manager(ApiFailing()).GetInfo().State);
    }

    [Fact]
    public void Unknown_persists_locally()
    {
        StatsConsentManager manager = Manager(ApiFailing());
        StatsConsentManager.Info info = manager.Set("unknown", false, false);
        Assert.Equal("unknown", info.State);
        Assert.Equal("local_unknown", info.SyncStatus);
    }

    [Fact]
    public void Accept_without_executor_character_is_identity_missing()
    {
        // No executor user -> currentConsentCharacter null -> no HTTP, identity_missing.
        StatsConsentManager manager = Manager(ApiFailing());

        StatsConsentManager.Info info = manager.Set("accepted", uploadEnabled: true, publicCharacter: true);

        Assert.Equal("accepted", info.State);
        Assert.False(info.UploadEnabled);
        Assert.Equal("identity_missing", info.SyncStatus);
    }

    [Fact]
    public void Accept_applies_remote_and_enables_upload()
    {
        GiveExecutor();
        StatsApiClient api = ApiReturning(
            """{"ok":true,"identityHash":"h","exists":true,"consentState":"accepted","public":true,"consentVersion":"2026-06-04","updatedAt":"2026-06-04T00:00:00Z"}""");
        StatsConsentManager manager = Manager(api);

        StatsConsentManager.Info info = manager.Set("accepted", uploadEnabled: true, publicCharacter: true);

        Assert.Equal("accepted", info.State);
        Assert.True(info.UploadEnabled);
        Assert.True(info.PublicCharacter);
        Assert.Equal("synced", info.SyncStatus);
        Assert.True(info.RemoteExists);
        Assert.True(manager.IsUploadAllowed());
    }

    [Fact]
    public void Accept_remote_failure_records_sync_failed_and_keeps_identity()
    {
        GiveExecutor();
        StatsConsentManager manager = Manager(ApiFailing());

        StatsConsentManager.Info info = manager.Set("accepted", uploadEnabled: true, publicCharacter: false);

        Assert.Equal("accepted", info.State);
        Assert.False(info.UploadEnabled); // not synced -> upload stays off
        Assert.Equal("sync_failed", info.SyncStatus);
        Assert.NotNull(info.IdentityHash);
        Assert.False(manager.IsUploadAllowed());
    }

    [Fact]
    public void Accept_remote_not_exists_does_not_enable_upload()
    {
        GiveExecutor();
        StatsApiClient api = ApiReturning(
            """{"ok":true,"identityHash":"h","exists":false,"consentState":"accepted","public":true}""");
        StatsConsentManager manager = Manager(api);

        StatsConsentManager.Info info = manager.Set("accepted", uploadEnabled: true, publicCharacter: true);

        Assert.False(info.UploadEnabled); // exists=false -> not accepted-effective
        Assert.False(info.PublicCharacter);
    }

    [Fact]
    public void RefreshFromServer_without_identity_is_identity_missing()
    {
        // No executor -> no identity hash.
        StatsConsentManager manager = Manager(ApiFailing());
        StatsConsentManager.Info info = manager.GetInfo(syncRemote: true);
        Assert.Equal("identity_missing", info.SyncStatus);
    }

    [Fact]
    public void Consent_is_remembered_per_character()
    {
        StatsApiClient accept = ApiReturning(
            """{"ok":true,"identityHash":"h","exists":true,"consentState":"accepted","public":true,"consentVersion":"2026-06-04","updatedAt":"2026-06-04T00:00:00Z"}""");

        // Character A accepts.
        _data.SaveNickname(1, "Alice", isExecutor: true, server: 3, jobByte: 0);
        Manager(accept).Set("accepted", uploadEnabled: true, publicCharacter: true);
        Assert.True(Manager(accept).IsCurrentCharacterConsented());
        Assert.True(Manager(accept).IsUploadAllowed());

        // Switch to character B -> NOT A's accepted; an undecided character is unknown.
        _data.SaveNickname(2, "Bob", isExecutor: true, server: 3, jobByte: 0);
        Assert.Equal("unknown", Manager(accept).GetInfo().State);
        Assert.False(Manager(accept).IsCurrentCharacterConsented());
        Assert.False(Manager(accept).IsUploadAllowed());

        // B declines (remembered for B only).
        Manager(ApiFailing()).Set("declined", uploadEnabled: false, publicCharacter: false);
        Assert.Equal("declined", Manager(accept).GetInfo().State);

        // Switch back to A -> still accepted (no re-prompt), upload still allowed.
        _data.SaveNickname(1, "Alice", isExecutor: true, server: 3, jobByte: 0);
        Assert.Equal("accepted", Manager(accept).GetInfo().State);
        Assert.True(Manager(accept).IsCurrentCharacterConsented());
        Assert.True(Manager(accept).IsUploadAllowed());

        // The consented list has A but not B.
        IReadOnlyList<string> consented = Manager(accept).ConsentedCharacterHashes();
        Assert.Contains(StatsIdentity.CharacterIdentityHash(3, "Alice")!, consented);
        Assert.DoesNotContain(StatsIdentity.CharacterIdentityHash(3, "Bob")!, consented);
    }

    private static StatsApiClient AcceptApi(bool pub) => ApiReturning(
        $$"""{"ok":true,"identityHash":"h","exists":true,"consentState":"accepted","public":{{(pub ? "true" : "false")}},"consentVersion":"2026-06-04","updatedAt":"2026-06-04T00:00:00Z"}""");

    [Fact]
    public void ListCharacters_includes_name_server_job_metadata()
    {
        GiveExecutor(); // Hero, server 3
        Manager(AcceptApi(true)).Set("accepted", uploadEnabled: true, publicCharacter: true);

        StatsConsentManager.CharacterConsentInfo hero =
            Manager(AcceptApi(true)).ListCharacters().Single(c => c.Nickname == "Hero");

        Assert.Equal(3, hero.Server);
        Assert.Equal("마도성", hero.Job);          // from the own-character provider
        Assert.Equal("accepted", hero.State);
        Assert.True(hero.IsCurrent);
        Assert.True(hero.PublicCharacter);
        Assert.True(hero.CanSetPublic);
        Assert.Equal(StatsIdentity.CharacterIdentityHash(3, "Hero"), hero.IdentityHash);
    }

    [Fact]
    public void SetCharacterPublic_updates_a_non_current_character_only()
    {
        // Alice accepts public=true, then we switch to Bob (Alice is now non-current).
        _data.SaveNickname(1, "Alice", isExecutor: true, server: 3, jobByte: 0);
        Manager(AcceptApi(true)).Set("accepted", uploadEnabled: true, publicCharacter: true);
        string aliceHash = StatsIdentity.CharacterIdentityHash(3, "Alice")!;
        _data.SaveNickname(2, "Bob", isExecutor: true, server: 3, jobByte: 0);

        Manager(AcceptApi(false)).SetCharacterPublic(aliceHash, publicCharacter: false);

        StatsConsentManager.CharacterConsentInfo alice =
            Manager(AcceptApi(false)).ListCharacters().Single(c => c.IdentityHash == aliceHash);
        Assert.False(alice.PublicCharacter); // toggled private
        Assert.Equal("accepted", alice.State);
        Assert.False(alice.IsCurrent);
        // Bob's (current) consent is untouched / undecided.
        Assert.Equal("unknown", Manager(AcceptApi(false)).GetInfo().State);
    }

    [Fact]
    public void RevokeCharacter_revokes_a_non_current_character()
    {
        _data.SaveNickname(1, "Alice", isExecutor: true, server: 3, jobByte: 0);
        Manager(AcceptApi(true)).Set("accepted", uploadEnabled: true, publicCharacter: true);
        string aliceHash = StatsIdentity.CharacterIdentityHash(3, "Alice")!;
        _data.SaveNickname(2, "Bob", isExecutor: true, server: 3, jobByte: 0);

        StatsApiClient revoked = ApiReturning(
            """{"ok":true,"identityHash":"h","exists":false,"consentState":"revoked","consentVersion":"2026-06-04","updatedAt":"2026-06-05T00:00:00Z"}""");
        Manager(revoked).RevokeCharacter(aliceHash);

        Assert.DoesNotContain(aliceHash, Manager(revoked).ConsentedCharacterHashes());
        Assert.Equal("revoked", Manager(revoked).ListCharacters().Single(c => c.IdentityHash == aliceHash).State);
    }

    [Fact]
    public void Korean_nickname_round_trips_through_property_storage()
    {
        // EUC-KR settings encoding corrupts raw Korean; the default JSON serializer \uXXXX-escapes it to
        // ASCII so it survives. A fresh manager (re-reads settings.properties) must still see the name.
        _data.SaveNickname(1, "와플", isExecutor: true, server: 3, jobByte: 0);
        Manager(AcceptApi(false)).Set("accepted", uploadEnabled: true, publicCharacter: false);

        Assert.Contains(Manager(AcceptApi(false)).ListCharacters(), c => c.Nickname == "와플");
    }

    [Fact]
    public void ListCharacters_shows_the_current_character_name_live_when_its_record_has_none()
    {
        GiveExecutor(); // Hero recognized (uid 1, server 3)
        string hash = StatsIdentity.CharacterIdentityHash(3, "Hero")!;
        // A prior-session record with NO stored nickname (e.g. server-synced before the name was known).
        _props.SetProperty("statsConsentCharacters",
            "{\"" + hash + "\":{\"state\":\"accepted\",\"uploadEnabled\":true,\"publicCharacter\":false,\"updatedAt\":1}}");

        StatsConsentManager.CharacterConsentInfo c = Manager(ApiFailing()).ListCharacters().Single();

        Assert.Equal("Hero", c.Nickname); // resolved live from the executor, not "이름 없음 (이전 기록)"
        Assert.Equal(3, c.Server);
        Assert.True(c.IsCurrent);
    }

    [Fact]
    public void BackfillCurrentCharacterIdentity_persists_the_name_so_it_shows_when_not_current()
    {
        GiveExecutor(); // Hero current
        string hash = StatsIdentity.CharacterIdentityHash(3, "Hero")!;
        _props.SetProperty("statsConsentCharacters",
            "{\"" + hash + "\":{\"state\":\"accepted\",\"uploadEnabled\":true,\"publicCharacter\":false,\"updatedAt\":1}}");

        Manager(ApiFailing()).BackfillCurrentCharacterIdentity();

        // Switch away so Hero is no longer current -> its name must come from the PERSISTED record, not live.
        _data.SaveNickname(2, "Bob", isExecutor: true, server: 3, jobByte: 0);
        StatsConsentManager.CharacterConsentInfo hero =
            Manager(ApiFailing()).ListCharacters().Single(c => c.IdentityHash == hash);
        Assert.Equal("Hero", hero.Nickname);
        Assert.False(hero.IsCurrent);
    }

    [Fact]
    public void ListCharacters_hides_name_less_legacy_records()
    {
        // Legacy records with no stored nickname and no character recognized -> all hidden (no confusing rows).
        string h1 = StatsIdentity.CharacterIdentityHash(3, "Alice")!;
        string h2 = StatsIdentity.CharacterIdentityHash(3, "Bob")!;
        _props.SetProperty("statsConsentCharacters",
            "{\"" + h1 + "\":{\"state\":\"accepted\",\"uploadEnabled\":true,\"publicCharacter\":true,\"updatedAt\":1},"
            + "\"" + h2 + "\":{\"state\":\"accepted\",\"uploadEnabled\":true,\"publicCharacter\":false,\"updatedAt\":2}}");

        Assert.Empty(Manager(ApiFailing()).ListCharacters());
    }

    [Fact]
    public void ListCharacters_shows_only_named_characters_when_some_records_are_name_less()
    {
        GiveExecutor(); // Hero recognized -> live-named
        string heroHash = StatsIdentity.CharacterIdentityHash(3, "Hero")!;
        string ghostHash = StatsIdentity.CharacterIdentityHash(3, "Ghost")!;
        _props.SetProperty("statsConsentCharacters",
            "{\"" + heroHash + "\":{\"state\":\"accepted\",\"uploadEnabled\":true,\"publicCharacter\":false,\"updatedAt\":1},"
            + "\"" + ghostHash + "\":{\"state\":\"accepted\",\"uploadEnabled\":true,\"publicCharacter\":false,\"updatedAt\":2}}");

        IReadOnlyList<StatsConsentManager.CharacterConsentInfo> list = Manager(ApiFailing()).ListCharacters();

        StatsConsentManager.CharacterConsentInfo only = Assert.Single(list); // Hero (live-named); nameless Ghost hidden
        Assert.Equal("Hero", only.Nickname);
        Assert.True(only.IsCurrent);
    }

    [Fact]
    public void Accept_caches_grant_from_response()
    {
        GiveExecutor();
        StatsApiClient api = ApiReturning(
            """{"ok":true,"identityHash":"h","exists":true,"consentState":"accepted","public":false,"granted":true,"consentVersion":"2026-06-04","updatedAt":"2026-06-04T00:00:00Z"}""");
        Manager(api).Set("accepted", uploadEnabled: true, publicCharacter: false);

        string hash = StatsIdentity.CharacterIdentityHash(3, "Hero")!;
        Assert.True(Manager(ApiFailing()).HasGrant(hash));
        Assert.True(Manager(ApiFailing()).ListCharacters().Single(c => c.IdentityHash == hash).Grant);
    }

    [Fact]
    public void MarkGranted_caches_grant_without_changing_state()
    {
        GiveExecutor();
        // AcceptApi(false) carries no "granted" field -> grant starts false.
        Manager(AcceptApi(false)).Set("accepted", uploadEnabled: true, publicCharacter: false);
        string hash = StatsIdentity.CharacterIdentityHash(3, "Hero")!;
        Assert.False(Manager(ApiFailing()).HasGrant(hash));

        Manager(ApiFailing()).MarkGranted(hash);

        StatsConsentManager.CharacterConsentInfo hero =
            Manager(ApiFailing()).ListCharacters().Single(c => c.IdentityHash == hash);
        Assert.True(hero.Grant);
        Assert.Equal("accepted", hero.State); // grant-only: consent state untouched
        Assert.True(Manager(ApiFailing()).HasGrant(hash));
    }

    [Fact]
    public void SetCharacterPublic_blocks_public_transition_without_grant()
    {
        // Alice accepts private, no grant. Switch to Bob so Alice is non-current.
        _data.SaveNickname(1, "Alice", isExecutor: true, server: 3, jobByte: 0);
        Manager(AcceptApi(false)).Set("accepted", uploadEnabled: true, publicCharacter: false);
        string aliceHash = StatsIdentity.CharacterIdentityHash(3, "Alice")!;
        _data.SaveNickname(2, "Bob", isExecutor: true, server: 3, jobByte: 0);

        // AcceptApi(true) WOULD make her public if the gate let the request through — it must not.
        Manager(AcceptApi(true)).SetCharacterPublic(aliceHash, publicCharacter: true);

        StatsConsentManager.CharacterConsentInfo alice =
            Manager(ApiFailing()).ListCharacters().Single(c => c.IdentityHash == aliceHash);
        Assert.False(alice.PublicCharacter); // gate blocked the public transition (no grant)
        Assert.False(alice.Grant);
    }

    [Fact]
    public void SetCharacterPublic_allows_public_transition_with_grant()
    {
        StatsApiClient grantingAccept = ApiReturning(
            """{"ok":true,"identityHash":"h","exists":true,"consentState":"accepted","public":false,"granted":true,"consentVersion":"2026-06-04","updatedAt":"2026-06-04T00:00:00Z"}""");
        _data.SaveNickname(1, "Alice", isExecutor: true, server: 3, jobByte: 0);
        Manager(grantingAccept).Set("accepted", uploadEnabled: true, publicCharacter: false);
        string aliceHash = StatsIdentity.CharacterIdentityHash(3, "Alice")!;
        Assert.True(Manager(ApiFailing()).HasGrant(aliceHash));
        _data.SaveNickname(2, "Bob", isExecutor: true, server: 3, jobByte: 0);

        Manager(AcceptApi(true)).SetCharacterPublic(aliceHash, publicCharacter: true);

        StatsConsentManager.CharacterConsentInfo alice =
            Manager(ApiFailing()).ListCharacters().Single(c => c.IdentityHash == aliceHash);
        Assert.True(alice.PublicCharacter); // grant present -> public transition allowed
    }

    [Fact]
    public void Successful_public_toggle_clears_a_stale_ownership_notice()
    {
        StatsApiClient grantingAccept = ApiReturning(
            """{"ok":true,"identityHash":"h","exists":true,"consentState":"accepted","public":false,"granted":true,"consentVersion":"2026-06-04","updatedAt":"2026-06-04T00:00:00Z"}""");
        _data.SaveNickname(1, "Alice", isExecutor: true, server: 3, jobByte: 0);
        Manager(grantingAccept).Set("accepted", uploadEnabled: true, publicCharacter: false);
        string aliceHash = StatsIdentity.CharacterIdentityHash(3, "Alice")!;
        _data.SaveNickname(2, "Bob", isExecutor: true, server: 3, jobByte: 0);

        // A prior blocked public attempt left the ownership notice in the global sync status.
        _props.SetProperty("statsConsentSyncStatus", StatsConsentManager.PublicRequiresOwnership);
        Assert.Equal(StatsConsentManager.PublicRequiresOwnership, Manager(ApiFailing()).GetInfo().SyncStatus);

        // A successful public toggle on the granted character must clear it (no stale notice).
        Manager(AcceptApi(true)).SetCharacterPublic(aliceHash, publicCharacter: true);
        Assert.Equal("synced", Manager(ApiFailing()).GetInfo().SyncStatus);
    }

    [Fact]
    public void Accept_public_requires_ownership_rolls_back_to_private()
    {
        GiveExecutor(); // Hero, server 3, current
        // Server refuses public=true (400) but accepts public=false (200) — drives the ReAcceptPrivate fallback.
        var api = new StatsApiClient(() => "install-1", (_, _, body, _) =>
            body != null && body.Contains("\"public\":true")
                ? new StatsHttpResponse(400, """{"ok":false,"error":{"code":"public_requires_ownership","message":"no grant"}}""")
                : new StatsHttpResponse(200, """{"ok":true,"identityHash":"h","exists":true,"consentState":"accepted","public":false,"consentVersion":"2026-06-04","updatedAt":"2026-06-04T00:00:00Z"}"""));

        StatsConsentManager.Info info = Manager(api).Set("accepted", uploadEnabled: true, publicCharacter: true);

        Assert.Equal("accepted", info.State);
        Assert.False(info.PublicCharacter); // rolled back to private
        Assert.Equal(StatsConsentManager.PublicRequiresOwnership, info.SyncStatus);
        Assert.True(info.UploadEnabled); // consent still landed; uploads stay on
    }

    [Fact]
    public void Revoke_is_never_gated_even_when_sync_fails()
    {
        GiveExecutor();
        Manager(AcceptApi(false)).Set("accepted", uploadEnabled: true, publicCharacter: false);

        // Revoke must persist locally regardless of the server (fail-safe, offline-capable).
        StatsConsentManager.Info info = Manager(ApiFailing()).Set("revoked", false, false);

        Assert.Equal("revoked", info.State);
        Assert.False(info.UploadEnabled);
        Assert.False(Manager(ApiFailing()).IsUploadAllowed());
    }
}

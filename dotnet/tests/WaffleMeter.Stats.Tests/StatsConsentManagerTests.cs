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
}

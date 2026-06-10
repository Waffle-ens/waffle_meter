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
}

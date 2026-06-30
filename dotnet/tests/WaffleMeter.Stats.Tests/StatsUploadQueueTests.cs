using WaffleMeter.Capture;
using WaffleMeter.Data;
using WaffleMeter.Services;
using WaffleMeter.Stats;
using Xunit;

namespace WaffleMeter.Stats.Tests;

public sealed class StatsUploadQueueTests : IDisposable
{
    private readonly string _temp;
    private readonly PropertyHandler _props;
    private readonly DataManager _dm;

    public StatsUploadQueueTests()
    {
        _temp = Path.Combine(Path.GetTempPath(), "wm_queue_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_temp);
        _props = new PropertyHandler(_temp);
        _dm = new DataManager();
        _dm.SaveNickname(1, "Me", isExecutor: true, server: 3, jobByte: 5);
        _dm.SaveUserPower(1, 5000);
        _dm.SaveNickname(2, "Ally", isExecutor: false, server: 3, jobByte: 25);
        _dm.SaveUserPower(2, 3000);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_temp, recursive: true);
        }
        catch
        {
            // best effort
        }
    }

    private static StatsApiClient AcceptingApi() => new(() => "install-1", (_, url, _, _) =>
        url.Contains("/api/v1/reports")
            ? new StatsHttpResponse(200, """{"ok":true,"reportId":"r1","duplicate":false}""")
            : new StatsHttpResponse(200, """{"ok":true,"identityHash":"h","exists":true,"consentState":"accepted","public":false,"consentVersion":"2026-06-04"}"""));

    private StatsUploadQueue NewQueue(StatsApiClient api, bool accept)
    {
        var builder = new StatsPayloadBuilder(_dm, () => false);
        var consent = new StatsConsentManager(_props, _dm, api, () => builder.OwnCharacter());
        if (accept)
        {
            consent.Set("accepted", uploadEnabled: true, publicCharacter: false);
        }

        return new StatsUploadQueue(consent, builder, api, _dm, _props,
            dispatch: job => job(), killRecheckDelay: () => { }, clock: () => 1);
    }

    private DpsLog BossLog(int remainHp, bool boss = true)
    {
        User me = _dm.User(1)!;
        User ally = _dm.User(2)!;
        var report = new DpsReport
        {
            Contributors = new List<User> { me, ally },
            BattleStart = 1_000_000,
            BattleEnd = 1_030_000,
            Target = new MobInfo(100, new Mob(12345, "보스", boss), remainHp: remainHp, maxHp: 1_000_000),
            Information = new Dictionary<int, DpsInformation>
            {
                [1] = new DpsInformation(1_000_000, 50_000, 60.0, 40.0),
                [2] = new DpsInformation(600_000, 30_000, 40.0, 24.0),
            },
        };
        return new DpsLog
        {
            Report = report,
            SkillDetails = new Dictionary<int, Dictionary<string, AnalyzedSkill>>
            {
                [1] = new() { ["11020001"] = new AnalyzedSkill { SkillCode = 11020001, Name = "강타", DamageAmount = 1_000_000, Times = 100 } },
                [2] = new() { ["15210001"] = new AnalyzedSkill { SkillCode = 15210001, Name = "파이어", DamageAmount = 600_000, Times = 50 } },
            },
            BuffRates = new Dictionary<int, List<OperatingData>>(),
            BossBuffRates = new List<OperatingData>(),
        };
    }

    [Fact]
    public void Skips_when_consent_not_allowed()
    {
        using StatsUploadQueue queue = NewQueue(AcceptingApi(), accept: false);
        queue.OfferIfEligible(BossLog(0));

        StatsUploadStatus status = queue.Status();
        Assert.Equal(0, status.Uploaded);
        Assert.Equal(1, status.Skipped);
        Assert.Equal("consent_not_allowed", status.LastReason);
    }

    [Fact]
    public void Skips_non_boss()
    {
        using StatsUploadQueue queue = NewQueue(AcceptingApi(), accept: true);
        queue.OfferIfEligible(BossLog(0, boss: false));

        Assert.Equal("not_boss", queue.Status().LastReason);
        Assert.Equal(0, queue.Status().Uploaded);
    }

    [Fact]
    public void Uploads_a_confirmed_boss_kill()
    {
        using StatsUploadQueue queue = NewQueue(AcceptingApi(), accept: true);
        queue.OfferIfEligible(BossLog(remainHp: 0)); // snapshot kill

        StatsUploadStatus status = queue.Status();
        Assert.Equal(1, status.Uploaded);
        Assert.Equal(0, status.Skipped);
        Assert.Equal(0, status.Failed);
        Assert.StartsWith("uploaded:", status.LastReason);
    }

    [Fact]
    public void Dedupes_repeat_battle_hash()
    {
        using StatsUploadQueue queue = NewQueue(AcceptingApi(), accept: true);
        DpsLog log = BossLog(remainHp: 0);

        queue.OfferIfEligible(log);
        queue.OfferIfEligible(log);

        StatsUploadStatus status = queue.Status();
        Assert.Equal(1, status.Uploaded);
        Assert.Equal(1, status.Skipped);
        Assert.Equal("duplicate", status.LastReason);
    }

    [Fact]
    public void Skips_when_kill_not_confirmed_after_recheck()
    {
        using StatsUploadQueue queue = NewQueue(AcceptingApi(), accept: true);
        queue.OfferIfEligible(BossLog(remainHp: 500_000)); // boss still alive, no mob-hp update

        StatsUploadStatus status = queue.Status();
        Assert.Equal(0, status.Uploaded);
        Assert.Equal("not_kill", status.LastReason);
    }

    [Fact]
    public void Confirms_kill_via_latest_mob_hp()
    {
        _dm.SaveMobHp(100, 0);
        _dm.SaveMobMaxHp(100, 1_000_000);

        using StatsUploadQueue queue = NewQueue(AcceptingApi(), accept: true);
        queue.OfferIfEligible(BossLog(remainHp: 500_000)); // snapshot not a kill, but latest hp is 0

        Assert.Equal(1, queue.Status().Uploaded);
    }

    [Fact]
    public void Successful_signed_upload_with_granted_caches_the_grant()
    {
        // Report response carries granted:true -> the queue must cache the uploader character's grant so the
        // public toggle unlocks without a separate consent round-trip (§2.2, W17 upload path).
        StatsApiClient api = new(() => "install-1", (_, url, _, _) =>
            url.Contains("/api/v1/reports")
                ? new StatsHttpResponse(200, """{"ok":true,"reportId":"r1","duplicate":false,"granted":true}""")
                : new StatsHttpResponse(200, """{"ok":true,"identityHash":"h","exists":true,"consentState":"accepted","public":false,"consentVersion":"2026-06-04"}"""));
        var builder = new StatsPayloadBuilder(_dm, () => false);
        var consent = new StatsConsentManager(_props, _dm, api, () => builder.OwnCharacter());
        consent.Set("accepted", uploadEnabled: true, publicCharacter: false);
        using var queue = new StatsUploadQueue(consent, builder, api, _dm, _props,
            dispatch: job => job(), killRecheckDelay: () => { }, clock: () => 1);

        queue.OfferIfEligible(BossLog(remainHp: 0));

        Assert.Equal(1, queue.Status().Uploaded);
        Assert.True(consent.HasGrant(StatsIdentity.CharacterIdentityHash(3, "Me")!));
    }

    [Fact]
    public void Records_failure_when_report_upload_throws()
    {
        StatsApiClient api = new(() => "install-1", (_, url, _, _) =>
            url.Contains("/api/v1/reports")
                ? new StatsHttpResponse(500, "server_down")
                : new StatsHttpResponse(200, """{"ok":true,"identityHash":"h","exists":true,"consentState":"accepted","public":false}"""));

        using StatsUploadQueue queue = NewQueue(api, accept: true);
        queue.OfferIfEligible(BossLog(remainHp: 0));

        StatsUploadStatus status = queue.Status();
        Assert.Equal(0, status.Uploaded);
        Assert.Equal(1, status.Failed);
        Assert.StartsWith("upload_failed:", status.LastReason);
    }
}

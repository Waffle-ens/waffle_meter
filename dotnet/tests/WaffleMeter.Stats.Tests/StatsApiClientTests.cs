using WaffleMeter.Stats;
using Xunit;

namespace WaffleMeter.Stats.Tests;

public sealed class StatsApiClientTests
{
    private sealed class Capture
    {
        public string? Method;
        public string? Url;
        public string? Body;
        public IReadOnlyDictionary<string, string>? Headers;
    }

    private static (StatsApiClient Client, Capture Captured) Build(int status, string body)
    {
        var captured = new Capture();
        StatsApiClient.RequestFunc fake = (method, url, requestBody, headers) =>
        {
            captured.Method = method;
            captured.Url = url;
            captured.Body = requestBody;
            captured.Headers = headers;
            return new StatsHttpResponse(status, body);
        };
        return (new StatsApiClient(() => "install-1", fake), captured);
    }

    [Fact]
    public void GetConsentStatus_sends_encoded_identity_and_parses_ok()
    {
        (StatsApiClient client, Capture captured) = Build(200,
            """{"ok":true,"identityHash":"h","exists":true,"consentState":"accepted","public":true}""");

        ConsentStatusResponse response = client.GetConsentStatus("a b");

        Assert.Equal("GET", captured.Method);
        Assert.Contains("/api/v1/consent/status?identityHash=a+b", captured.Url);
        Assert.Equal("application/json", captured.Headers!["Accept"]);
        Assert.True(response.Exists);
        Assert.True(response.PublicCharacter);
    }

    [Fact]
    public void GetConsentStatus_throws_when_not_ok()
    {
        (StatsApiClient client, _) = Build(200, """{"ok":false,"identityHash":"h","exists":false,"consentState":"unknown"}""");
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => client.GetConsentStatus("h"));
        Assert.Equal("consent_status_not_ok", ex.Message);
    }

    [Fact]
    public void Non_2xx_throws_with_status_and_summary()
    {
        (StatsApiClient client, _) = Build(500, "boom");
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => client.GetConsentStatus("h"));
        Assert.Contains("HTTP 500", ex.Message);
        Assert.Contains("boom", ex.Message);
    }

    [Fact]
    public void PostConsentEvent_sends_body_and_client_headers()
    {
        (StatsApiClient client, Capture captured) = Build(200,
            """{"ok":true,"identityHash":"h","exists":true,"consentState":"accepted"}""");

        client.PostConsentEvent(new ConsentEventRequest("accepted", "2026-06-04", IdentityHash: "h"), clientVersion: "1.7.9");

        Assert.Equal("POST", captured.Method);
        Assert.Contains("/api/v1/consent/events", captured.Url);
        Assert.Contains("\"consentState\":\"accepted\"", captured.Body);
        Assert.Equal("waffle_meter/1.7.9", captured.Headers!["User-Agent"]);
        Assert.Equal("1.7.9", captured.Headers["x-client-version"]);
        Assert.Equal("install-1", captured.Headers["x-install-id"]);
        Assert.Equal("2026-06-04", captured.Headers["x-consent-version"]);
        Assert.Equal("application/json", captured.Headers["Content-Type"]);
    }

    [Fact]
    public void PostReport_uses_payload_consent_version_header_and_parses()
    {
        (StatsApiClient client, Capture captured) = Build(200, """{"ok":true,"reportId":"r1","duplicate":false}""");
        var payload = SamplePayload("hash-1", "2026-06-04");

        ReportUploadResponse response = client.PostReport(payload, clientVersion: "1.7.9");

        Assert.Contains("/api/v1/reports", captured.Url);
        Assert.Equal("2026-06-04", captured.Headers!["x-consent-version"]);
        Assert.Equal("r1", response.ReportId);
        Assert.False(response.Duplicate);
    }

    [Fact]
    public void PostReport_throws_when_not_ok()
    {
        (StatsApiClient client, _) = Build(200, """{"ok":false}""");
        Assert.Throws<InvalidOperationException>(() => client.PostReport(SamplePayload("h", "2026-06-04"), "1.7.9"));
    }

    private static StatsUploadPayload SamplePayload(string battleHash, string consentVersion)
    {
        var result = new StatsResultPayload(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        return new StatsUploadPayload(
            SchemaVersion: 1,
            ClientVersion: "1.7.9",
            BattleHash: battleHash,
            IdentityHashVersion: StatsIdentity.IdentityHashVersion,
            ConsentVersion: consentVersion,
            UploadedAt: 0,
            Character: new StatsCharacterPayload("h", "Hero", 3, "마도성", 100, true),
            Encounter: new StatsEncounterPayload(5, "Boss"),
            Battle: new StatsBattlePayload(0, 1, 1, 8),
            PartyComposition: new StatsPartyCompositionPayload(
                new Dictionary<string, int>(),
                new StatsSynergyPayload(false, false, false, false, 0)),
            Participants: Array.Empty<StatsParticipantPayload>(),
            Result: result,
            Skills: Array.Empty<StatsSkillPayload>(),
            Buffs: Array.Empty<StatsBuffPayload>(),
            BossDebuffs: Array.Empty<StatsBuffPayload>());
    }
}

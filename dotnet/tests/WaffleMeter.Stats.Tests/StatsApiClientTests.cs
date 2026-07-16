using System.Security.Cryptography;
using System.Text;
using WaffleMeter.Services;
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

    /// <summary>Records the exact canonical string it was asked to sign and returns fixed marker values,
    /// so a test can assert the canonical bytes + that the headers carry the signer's outputs.</summary>
    private sealed class RecordingSigner : IStatsSigner
    {
        public string? LastCanonical;
        public string PublicKeyB64() => "PUBKEY_B64";
        public string Sign(string canonical)
        {
            LastCanonical = canonical;
            return "SIG_B64";
        }
    }

    private static (StatsApiClient Client, Capture Captured, RecordingSigner Signer) BuildSigned(int status, string body)
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
        var signer = new RecordingSigner();
        var client = new StatsApiClient(
            () => "install-1", fake, signer,
            clock: () => 1_700_000_000_000L,
            nonceProvider: () => "fixed-nonce");
        return (client, captured, signer);
    }

    private static string ExpectedBodyHash(string body) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(body)));

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
        InvalidOperationException ex = Assert.ThrowsAny<InvalidOperationException>(() => client.GetConsentStatus("h"));
        Assert.Equal("consent_status_not_ok", ex.Message);
    }

    [Fact]
    public void Non_2xx_throws_with_status_and_summary()
    {
        (StatsApiClient client, _) = Build(500, "boom");
        InvalidOperationException ex = Assert.ThrowsAny<InvalidOperationException>(() => client.GetConsentStatus("h"));
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
        Assert.ThrowsAny<InvalidOperationException>(() => client.PostReport(SamplePayload("h", "2026-06-04"), "1.7.9"));
    }

    [Fact]
    public void PostConsentEvent_signs_write_with_exact_canonical_and_headers()
    {
        (StatsApiClient client, Capture captured, RecordingSigner signer) = BuildSigned(200,
            """{"ok":true,"identityHash":"h","exists":true,"consentState":"accepted"}""");

        client.PostConsentEvent(new ConsentEventRequest("accepted", "2026-06-04", IdentityHash: "h"), clientVersion: "1.7.9");

        // The canonical is exactly METHOD\nPATH\nInstallId\nTimestamp\nNonce\nbase64(sha256(rawBody)).
        string expectedCanonical = string.Join('\n',
            "POST", "/api/v1/consent/events", "install-1", "1700000000000", "fixed-nonce", ExpectedBodyHash(captured.Body!));
        Assert.Equal(expectedCanonical, signer.LastCanonical);

        Assert.Equal("install-1", captured.Headers!["X-WM-Install-Id"]);
        Assert.Equal("PUBKEY_B64", captured.Headers["X-WM-Install-Key"]);
        Assert.Equal("1700000000000", captured.Headers["X-WM-Timestamp"]);
        Assert.Equal("fixed-nonce", captured.Headers["X-WM-Nonce"]);
        Assert.Equal("SIG_B64", captured.Headers["X-WM-Signature"]);
        // Legacy header retained alongside the signed one.
        Assert.Equal("install-1", captured.Headers["x-install-id"]);
    }

    [Fact]
    public void Signed_write_verifies_end_to_end_with_a_real_install_key()
    {
        string tempAppData = Path.Combine(Path.GetTempPath(), "wm_apiclient_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempAppData);
        try
        {
            var signer = new StatsInstallKey(new PropertyHandler(tempAppData));
            var captured = new Capture();
            StatsApiClient.RequestFunc fake = (method, url, body, headers) =>
            {
                captured.Method = method;
                captured.Url = url;
                captured.Body = body;
                captured.Headers = headers;
                return new StatsHttpResponse(200, """{"ok":true,"reportId":"r1"}""");
            };
            var client = new StatsApiClient(() => "install-9", fake, signer,
                clock: () => 1_700_000_000_000L, nonceProvider: () => "nonce-xyz");

            client.PostReport(SamplePayload("h", "2026-06-04"), clientVersion: "1.7.9");

            // Reconstruct the canonical from the CAPTURED header values + body and verify the signature under
            // the advertised public key — exactly what the stats-web verify-signature middleware does.
            string canonical = string.Join('\n',
                "POST", "/api/v1/reports",
                captured.Headers!["X-WM-Install-Id"], captured.Headers["X-WM-Timestamp"], captured.Headers["X-WM-Nonce"],
                ExpectedBodyHash(captured.Body!));
            using ECDsa verifier = ECDsa.Create();
            verifier.ImportSubjectPublicKeyInfo(Convert.FromBase64String(captured.Headers["X-WM-Install-Key"]), out _);
            bool ok = verifier.VerifyData(
                Encoding.UTF8.GetBytes(canonical),
                Convert.FromBase64String(captured.Headers["X-WM-Signature"]),
                HashAlgorithmName.SHA256,
                DSASignatureFormat.Rfc3279DerSequence);
            Assert.True(ok);
        }
        finally
        {
            Directory.Delete(tempAppData, recursive: true);
        }
    }

    [Fact]
    public void GetConsentStatus_is_unsigned_even_with_a_signer()
    {
        (StatsApiClient client, Capture captured, _) = BuildSigned(200,
            """{"ok":true,"identityHash":"h","exists":true,"consentState":"accepted"}""");

        client.GetConsentStatus("h");

        Assert.DoesNotContain("X-WM-Signature", captured.Headers!.Keys);
        Assert.DoesNotContain("X-WM-Nonce", captured.Headers.Keys);
        Assert.DoesNotContain("X-WM-Timestamp", captured.Headers.Keys);
    }

    [Fact]
    public void PostReport_signs_with_the_reports_path()
    {
        (StatsApiClient client, Capture captured, RecordingSigner signer) = BuildSigned(200,
            """{"ok":true,"reportId":"r1","duplicate":false}""");

        client.PostReport(SamplePayload("h", "2026-06-04"), clientVersion: "1.7.9");

        Assert.StartsWith("POST\n/api/v1/reports\n", signer.LastCanonical);
        Assert.Contains("X-WM-Signature", captured.Headers!.Keys);
    }

    [Fact]
    public void Public_requires_ownership_surfaces_as_StatsApiException_with_body()
    {
        (StatsApiClient client, _, _) = BuildSigned(400,
            """{"ok":false,"error":{"code":"public_requires_ownership","message":"no grant"}}""");

        StatsApiException ex = Assert.Throws<StatsApiException>(() =>
            client.PostConsentEvent(
                new ConsentEventRequest("accepted", "2026-06-04",
                    Character: new ConsentEventCharacter("h", "Hero", 3, true)),
                clientVersion: "1.7.9"));
        Assert.Equal(400, ex.StatusCode);
        Assert.Contains("public_requires_ownership", ex.ResponseBody);
    }

    private sealed class ThrowingSigner : IStatsSigner
    {
        public string PublicKeyB64() => throw new InvalidOperationException("dpapi_down");
        public string Sign(string canonical) => throw new InvalidOperationException("dpapi_down");
    }

    [Fact]
    public void A_throwing_signer_falls_back_to_an_unsigned_write()
    {
        var captured = new Capture();
        StatsApiClient.RequestFunc fake = (method, url, body, headers) =>
        {
            captured.Method = method;
            captured.Url = url;
            captured.Body = body;
            captured.Headers = headers;
            return new StatsHttpResponse(200, """{"ok":true,"identityHash":"h","exists":true,"consentState":"revoked"}""");
        };
        var client = new StatsApiClient(() => "install-1", fake, new ThrowingSigner());

        // Upload/revoke must never be blocked by a signing failure (server accepts unsigned writes).
        client.PostConsentEvent(new ConsentEventRequest("revoked", "2026-06-04", IdentityHash: "h"), clientVersion: "1.7.9");

        Assert.DoesNotContain("X-WM-Signature", captured.Headers!.Keys); // clean unsigned request
        Assert.DoesNotContain("X-WM-Install-Key", captured.Headers.Keys);
        Assert.Equal("install-1", captured.Headers["x-install-id"]); // request still went out
    }

    private static StatsUploadPayload SamplePayload(string battleHash, string consentVersion)
    {
        var result = new StatsResultPayload(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0); // +FrontRate
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

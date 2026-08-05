using System.Buffers.Text;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace WaffleMeter.Stats;

/// <summary>One HTTP response (status + body), the unit the injected transport returns.</summary>
/// <param name="RetryAfterSeconds">The server's <c>Retry-After</c>, when it sent one. Only 429 uses it in
/// practice (nginx <c>limit_req_status 429</c>), and honouring it beats guessing at a backoff.</param>
public sealed record StatsHttpResponse(int StatusCode, string Body, int? RetryAfterSeconds = null);

/// <summary>A binary HTTP response — the raw, still-compressed bytes exactly as they came off the wire.</summary>
public sealed record StatsBinaryResponse(int StatusCode, byte[] Body);

/// <summary>The two reads the tier service needs, as a seam so it can be exercised without a socket
/// (same pattern as <see cref="IStatsSigner"/>). <see cref="StatsApiClient"/> is the live implementation.</summary>
public interface ITierApi
{
    TierManifestResponse GetTierManifest();

    StatsBinaryResponse GetTierArtifactGzip(string path);

    TierLookupResponse PostTierLookup(TierLookupRequest request, string clientVersion, string? installId = null);
}

/// <summary>Thrown on a non-OK stats response. Carries the HTTP status + raw body so callers can branch
/// on a server error code (e.g. <c>public_requires_ownership</c>) without re-parsing. Derives from
/// <see cref="InvalidOperationException"/> so existing <c>Assert.Throws&lt;InvalidOperationException&gt;</c>
/// call sites keep working.</summary>
public sealed class StatsApiException : InvalidOperationException
{
    public int StatusCode { get; }
    public string? ResponseBody { get; }

    /// <summary>The server's <c>Retry-After</c>, when it sent one. Null for everything but a rate limit.</summary>
    public int? RetryAfterSeconds { get; }

    public StatsApiException(string message, int statusCode, string? responseBody, int? retryAfterSeconds = null)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
        RetryAfterSeconds = retryAfterSeconds;
    }

    /// <summary>Whether re-sending the identical request could plausibly succeed.
    /// <para>5xx and transport faults are transient by definition. 429 is a rate limit, so retrying IS the
    /// correct response — the server sets <c>limit_req_status 429</c>, which makes it a code we actually
    /// receive. 408 is a request timeout, likewise. Every other 4xx is a verdict on the request itself
    /// (400 unsupported_encounter, 401, 409): sending it again returns the same answer.</para></summary>
    public bool IsTransient => StatusCode >= 500 || StatusCode == 429 || StatusCode == 408 || StatusCode == 0;
}

/// <summary>
/// Verbatim port of Kotlin <c>stats.StatsApiClient</c>: talks to the telemetry backend
/// (와터기.kr / punycode xn--ok0b896b9wh.kr, HTTPS-only) for consent status/events and report upload.
/// The low-level request is injected (<see cref="RequestFunc"/>) so the consent/upload logic is
/// unit-testable without a network; the default uses a shared <see cref="HttpClient"/>.
/// Non-2xx and <c>ok=false</c> responses throw, exactly like the Kotlin client.
/// </summary>
public sealed class StatsApiClient : ITierApi
{
    private const string BaseUrl = "https://xn--ok0b896b9wh.kr";
    private const string ReportEndpointUrl = BaseUrl + "/api/v1/reports";
    private const string ConsentStatusEndpoint = BaseUrl + "/api/v1/consent/status";
    private const string ConsentEventsEndpoint = BaseUrl + "/api/v1/consent/events";
    private const string TierManifestEndpoint = BaseUrl + "/api/v1/tiers/manifest";
    private const string TierLookupEndpoint = BaseUrl + "/api/v1/tiers/lookup";
    private const int ConnectTimeoutMs = 8_000;
    private const int ReadTimeoutMs = 15_000;

    public delegate StatsHttpResponse RequestFunc(string method, string url, string? body, IReadOnlyDictionary<string, string> headers);

    /// <summary>Binary GET transport for the tier artifact. Separate from <see cref="RequestFunc"/> because the
    /// artifact must arrive as the EXACT bytes the server stored — see <see cref="GetTierArtifactGzip"/>.</summary>
    public delegate StatsBinaryResponse BinaryRequestFunc(string method, string url, IReadOnlyDictionary<string, string> headers);

    /// <summary>How long the artifact GET may take. It is a much bigger body than any other call here —
    /// currently ~115 KB, and the server's move to combat-power bands can take it toward 700 KB — so the
    /// 15 s that suits a JSON POST would start timing out on a slow line. A timeout there is not even a
    /// diagnosable failure: the retry is 12 hours away, and nothing distinguishes it from a stale artifact.</summary>
    private const int ArtifactReadTimeoutMs = 60_000;

    /// <summary>How long a pooled connection may live before it is reopened.
    /// <para>The default is Infinite, which means the process never resolves the hostname again. Measured on
    /// the 2026-08-05 origin move: nine hours after the address changed, 45% of uploads were still going to
    /// the old one, and the set of stragglers never refreshed — only restarting the meter cleared it. That
    /// migration kept the old origin as a proxy, so nothing was lost; without it, and with uploads having no
    /// retry, that 45% would simply have been gone.</para></summary>
    private static readonly TimeSpan ConnectionLifetime = TimeSpan.FromMinutes(2);

    private static SocketsHttpHandler NewHandler() => new()
    {
        ConnectTimeout = TimeSpan.FromMilliseconds(ConnectTimeoutMs),
        PooledConnectionLifetime = ConnectionLifetime,
    };

    private static readonly HttpClient SharedClient = new(NewHandler())
    {
        Timeout = TimeSpan.FromMilliseconds(ReadTimeoutMs),
    };

    /// <summary>Separate client purely for the artifact download's longer timeout — HttpClient.Timeout is
    /// per-instance, not per-request. Its handler leaves AutomaticDecompression at None for the same reason
    /// <see cref="SharedClient"/> does (see <see cref="GetTierArtifactGzip"/>).</summary>
    private static readonly HttpClient ArtifactClient = new(NewHandler())
    {
        Timeout = TimeSpan.FromMilliseconds(ArtifactReadTimeoutMs),
    };

    private readonly RequestFunc _request;
    private readonly BinaryRequestFunc _binaryRequest;
    private readonly Func<string> _installIdProvider;
    private readonly IStatsSigner? _signer;
    private readonly Func<long> _clock;
    private readonly Func<string> _nonceProvider;

    /// <param name="signer">Per-install ECDSA signer (§2.1). Injected so it can be faked in tests; when
    /// null, write requests go out unsigned (the server treats signature-absence as non-fatal in every
    /// rollout mode). The live app always supplies a real <see cref="StatsInstallKey"/>.</param>
    /// <param name="clock">epoch-ms source for <c>X-WM-Timestamp</c> (injectable for deterministic tests).</param>
    /// <param name="nonceProvider">per-request <c>X-WM-Nonce</c> (base64url) source (injectable for tests).</param>
    public StatsApiClient(
        Func<string> installIdProvider,
        RequestFunc? request = null,
        IStatsSigner? signer = null,
        Func<long>? clock = null,
        Func<string>? nonceProvider = null,
        BinaryRequestFunc? binaryRequest = null)
    {
        _installIdProvider = installIdProvider;
        _request = request ?? DefaultRequest;
        _binaryRequest = binaryRequest ?? DefaultBinaryRequest;
        _signer = signer;
        _clock = clock ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        _nonceProvider = nonceProvider ?? DefaultNonce;
    }

    public string ReportEndpoint() => ReportEndpointUrl;

    /// <summary>Public web URL where a user can view a SINGLE character's own uploaded battle records,
    /// keyed by the anonymous <paramref name="identityHash"/> ("내 캐릭터 검색", Tier A). The hash is
    /// recomputed from server+nickname, so the link is identical across reinstalls and other PCs and
    /// matches every historical upload; it carries no nickname. The separate stats-web project must serve
    /// this route and must NOT render nickname/server for characters that are not marked public.</summary>
    public string CharacterReportUrl(string identityHash) => $"{BaseUrl}/characters/{identityHash}";

    /// <summary>통계 웹서비스 첫 화면. 설정창 하단의 '통계 웹' 버튼이 연다 — 도메인을 UI 쪽에 또 적어두면
    /// 주소가 바뀔 때 한쪽만 고쳐지므로 여기서만 들고 있는다.</summary>
    public string WebHomeUrl => BaseUrl;

    public ConsentStatusResponse GetConsentStatus(string identityHash)
    {
        string encoded = WebUtility.UrlEncode(identityHash);
        // Read path — unsigned per §2.1 (only writes are signed).
        StatsHttpResponse response = Request("GET", $"{ConsentStatusEndpoint}?identityHash={encoded}", null, null, null, null, signed: false);
        ConsentStatusResponse parsed = StatsJson.Deserialize<ConsentStatusResponse>(response.Body);
        if (!parsed.Ok)
        {
            throw new StatsApiException("consent_status_not_ok", response.StatusCode, response.Body);
        }

        return parsed;
    }

    public ConsentStatusResponse PostConsentEvent(
        ConsentEventRequest request,
        string clientVersion,
        string? installId = null,
        string? consentVersion = null)
    {
        StatsHttpResponse response = Request(
            "POST",
            ConsentEventsEndpoint,
            StatsJson.Serialize(request),
            clientVersion,
            installId ?? _installIdProvider(),
            consentVersion ?? StatsConsentManager.ConsentVersion,
            signed: true);
        ConsentStatusResponse parsed = StatsJson.Deserialize<ConsentStatusResponse>(response.Body);
        if (!parsed.Ok)
        {
            throw new StatsApiException("consent_event_not_ok", response.StatusCode, response.Body);
        }

        return parsed;
    }

    public ReportUploadResponse PostReport(StatsUploadPayload payload, string clientVersion, string? installId = null)
    {
        StatsHttpResponse response = Request(
            "POST",
            ReportEndpointUrl,
            StatsJson.Serialize(payload),
            clientVersion,
            installId ?? _installIdProvider(),
            payload.ConsentVersion,
            signed: true);
        ReportUploadResponse parsed = StatsJson.Deserialize<ReportUploadResponse>(response.Body);
        if (!parsed.Ok)
        {
            throw new StatsApiException(
                "report_upload_not_ok", response.StatusCode, response.Body, response.RetryAfterSeconds);
        }

        return parsed;
    }

    /// <summary>Current distribution artifact pointer. Unsigned read (§2.1) and cheap — nginx serves it from a
    /// 60s cache, so the origin sees at most one request a minute no matter how many meters are running.</summary>
    public TierManifestResponse GetTierManifest()
    {
        StatsHttpResponse response = Request("GET", TierManifestEndpoint, null, null, null, null, signed: false);
        TierManifestResponse parsed = StatsJson.Deserialize<TierManifestResponse>(response.Body);
        if (!parsed.Ok || string.IsNullOrEmpty(parsed.ArtifactId))
        {
            throw new StatsApiException("tier_manifest_not_ok", response.StatusCode, response.Body);
        }

        return parsed;
    }

    /// <summary>
    /// Download the artifact as the RAW GZIP BYTES.
    /// <para>🔑 The transport must not decompress. The server answers with <c>Content-Encoding: gzip</c> regardless
    /// of <c>Accept-Encoding</c>, and <c>manifest.sha256</c> is the hash of the COMPRESSED bytes. If the HTTP stack
    /// transparently inflates (HttpClientHandler.AutomaticDecompression), the bytes we could hash are the plaintext
    /// and the integrity check can never pass. The default handler below leaves AutomaticDecompression at None for
    /// exactly this reason — do not "fix" it.</para>
    /// <para><paramref name="path"/> is the manifest's <c>url</c> field (server-relative), not a full URL.</para>
    /// </summary>
    public StatsBinaryResponse GetTierArtifactGzip(string path)
    {
        string url = path.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? path : BaseUrl + path;
        var headers = new Dictionary<string, string> { ["Accept"] = "application/json" };
        StatsBinaryResponse response = _binaryRequest("GET", url, headers);
        if (response.StatusCode is < 200 or > 299)
        {
            // 410 means the artifact rotated out; the caller re-reads the manifest rather than retrying this id.
            throw new StatsApiException($"tier_artifact_http_{response.StatusCode}", response.StatusCode, null);
        }

        return response;
    }

    /// <summary>
    /// Batch tier lookup for party applicants. SIGNED — the server forces signature mode "on" for this route
    /// regardless of its rollout setting, so an unsigned request is 401 rather than silently degraded.
    /// <para>Server caps: 12 hashes, 4,096-byte body, 120 requests/hour per install. Exceeding them returns
    /// 400/413/429; the exception carries the status so the caller can back off instead of hammering.</para>
    /// </summary>
    public TierLookupResponse PostTierLookup(TierLookupRequest request, string clientVersion, string? installId = null)
    {
        StatsHttpResponse response = Request(
            "POST",
            TierLookupEndpoint,
            StatsJson.Serialize(request),
            clientVersion,
            installId ?? _installIdProvider(),
            StatsConsentManager.ConsentVersion,
            signed: true);
        TierLookupResponse parsed = StatsJson.Deserialize<TierLookupResponse>(response.Body);
        if (!parsed.Ok)
        {
            throw new StatsApiException("tier_lookup_not_ok", response.StatusCode, response.Body);
        }

        return parsed;
    }

    private StatsHttpResponse Request(
        string method,
        string url,
        string? body,
        string? clientVersion,
        string? installId,
        string? consentVersion,
        bool signed)
    {
        var headers = new Dictionary<string, string> { ["Accept"] = "application/json" };
        if (clientVersion != null)
        {
            headers["User-Agent"] = $"waffle_meter/{clientVersion}";
            headers["x-client-version"] = clientVersion;
        }

        if (installId != null)
        {
            headers["x-install-id"] = installId;
        }

        if (consentVersion != null)
        {
            headers["x-consent-version"] = consentVersion;
        }

        if (body != null)
        {
            headers["Content-Type"] = "application/json";
        }

        if (signed && _signer != null && installId != null)
        {
            try
            {
                // §2.1 signed write. canonicalString (UTF-8, LF-joined):
                //   {METHOD}\n{PATH}\n{X-WM-Install-Id}\n{X-WM-Timestamp}\n{X-WM-Nonce}\n{base64(sha256(rawBody))}
                // PATH excludes the query string; rawBody is the exact transmitted bytes (UTF-8 of `body`, or
                // sha256("") when empty). Signature/key are standard base64; the nonce is base64url.
                long timestamp = _clock();
                string timestampStr = timestamp.ToString(CultureInfo.InvariantCulture);
                string nonce = _nonceProvider();
                string path = new Uri(url).AbsolutePath;
                string bodyHash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(body ?? string.Empty)));
                string canonical = string.Join('\n', method, path, installId, timestampStr, nonce, bodyHash);
                // Compute both (the throwing calls) BEFORE touching headers, so a failure leaves a clean
                // UNSIGNED request rather than a half-signed one.
                string installKey = _signer.PublicKeyB64();
                string signature = _signer.Sign(canonical);
                headers["X-WM-Install-Id"] = installId;
                headers["X-WM-Install-Key"] = installKey;
                headers["X-WM-Timestamp"] = timestampStr;
                headers["X-WM-Nonce"] = nonce;
                headers["X-WM-Signature"] = signature;
            }
            catch
            {
                // Signing is best-effort: a key/DPAPI failure must NEVER block an upload or a revoke (the
                // server accepts unsigned writes in every rollout mode, §2.5/§2.6). Send the request unsigned.
            }
        }

        StatsHttpResponse response = _request(method, url, body, headers);
        if (response.StatusCode is < 200 or > 299)
        {
            string summary = response.Body.Length > 300 ? response.Body[..300] : response.Body;
            if (string.IsNullOrEmpty(summary))
            {
                summary = "empty_response";
            }

            throw new StatsApiException(
                $"HTTP {response.StatusCode}: {summary}", response.StatusCode, response.Body, response.RetryAfterSeconds);
        }

        return response;
    }

    private static string DefaultNonce() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16));

    /// <summary>Binary GET for the artifact, over <see cref="ArtifactClient"/> and its longer timeout. The
    /// handler leaves AutomaticDecompression at None, so this returns the gzip bytes the server actually sent —
    /// which is what <c>manifest.sha256</c> is computed over.</summary>
    private static StatsBinaryResponse DefaultBinaryRequest(string method, string url, IReadOnlyDictionary<string, string> headers)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), url);
        foreach (KeyValuePair<string, string> header in headers)
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        using HttpResponseMessage response = ArtifactClient.Send(request);
        byte[] bytes = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
        return new StatsBinaryResponse((int)response.StatusCode, bytes);
    }

    private static StatsHttpResponse DefaultRequest(string method, string url, string? body, IReadOnlyDictionary<string, string> headers)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), url);
        foreach (KeyValuePair<string, string> header in headers)
        {
            if (header.Key == "Content-Type")
            {
                continue; // set on the StringContent below
            }

            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (body != null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        using HttpResponseMessage response = SharedClient.Send(request);
        string text = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        return new StatsHttpResponse((int)response.StatusCode, text, ReadRetryAfterSeconds(response));
    }

    /// <summary>The response's <c>Retry-After</c> in seconds, or null. Only the delta-seconds form is read —
    /// the HTTP-date form would need the server's clock to agree with ours, and this server sends the delta
    /// (nginx's rate limiter does).</summary>
    private static int? ReadRetryAfterSeconds(HttpResponseMessage response) =>
        response.Headers.RetryAfter?.Delta is { } delta && delta > TimeSpan.Zero
            ? (int)Math.Ceiling(delta.TotalSeconds)
            : null;
}

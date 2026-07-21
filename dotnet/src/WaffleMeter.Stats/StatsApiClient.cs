using System.Buffers.Text;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace WaffleMeter.Stats;

/// <summary>One HTTP response (status + body), the unit the injected transport returns.</summary>
public sealed record StatsHttpResponse(int StatusCode, string Body);

/// <summary>Thrown on a non-OK stats response. Carries the HTTP status + raw body so callers can branch
/// on a server error code (e.g. <c>public_requires_ownership</c>) without re-parsing. Derives from
/// <see cref="InvalidOperationException"/> so existing <c>Assert.Throws&lt;InvalidOperationException&gt;</c>
/// call sites keep working.</summary>
public sealed class StatsApiException : InvalidOperationException
{
    public int StatusCode { get; }
    public string? ResponseBody { get; }

    public StatsApiException(string message, int statusCode, string? responseBody) : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }
}

/// <summary>
/// Verbatim port of Kotlin <c>stats.StatsApiClient</c>: talks to the telemetry backend
/// (와터기.kr / punycode xn--ok0b896b9wh.kr, HTTPS-only) for consent status/events and report upload.
/// The low-level request is injected (<see cref="RequestFunc"/>) so the consent/upload logic is
/// unit-testable without a network; the default uses a shared <see cref="HttpClient"/>.
/// Non-2xx and <c>ok=false</c> responses throw, exactly like the Kotlin client.
/// </summary>
public sealed class StatsApiClient
{
    private const string BaseUrl = "https://xn--ok0b896b9wh.kr";
    private const string ReportEndpointUrl = BaseUrl + "/api/v1/reports";
    private const string ConsentStatusEndpoint = BaseUrl + "/api/v1/consent/status";
    private const string ConsentEventsEndpoint = BaseUrl + "/api/v1/consent/events";
    private const int ConnectTimeoutMs = 8_000;
    private const int ReadTimeoutMs = 15_000;

    public delegate StatsHttpResponse RequestFunc(string method, string url, string? body, IReadOnlyDictionary<string, string> headers);

    private static readonly HttpClient SharedClient = new(
        new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromMilliseconds(ConnectTimeoutMs) })
    {
        Timeout = TimeSpan.FromMilliseconds(ReadTimeoutMs),
    };

    private readonly RequestFunc _request;
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
        Func<string>? nonceProvider = null)
    {
        _installIdProvider = installIdProvider;
        _request = request ?? DefaultRequest;
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
            throw new StatsApiException("report_upload_not_ok", response.StatusCode, response.Body);
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

            throw new StatsApiException($"HTTP {response.StatusCode}: {summary}", response.StatusCode, response.Body);
        }

        return response;
    }

    private static string DefaultNonce() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16));

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
        return new StatsHttpResponse((int)response.StatusCode, text);
    }
}

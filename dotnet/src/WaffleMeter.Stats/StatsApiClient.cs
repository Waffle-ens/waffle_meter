using System.Net;
using System.Text;

namespace WaffleMeter.Stats;

/// <summary>One HTTP response (status + body), the unit the injected transport returns.</summary>
public sealed record StatsHttpResponse(int StatusCode, string Body);

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

    public StatsApiClient(Func<string> installIdProvider, RequestFunc? request = null)
    {
        _installIdProvider = installIdProvider;
        _request = request ?? DefaultRequest;
    }

    public string ReportEndpoint() => ReportEndpointUrl;

    /// <summary>Public web URL where a user can view a SINGLE character's own uploaded battle records,
    /// keyed by the anonymous <paramref name="identityHash"/> ("내 캐릭터 검색", Tier A). The hash is
    /// recomputed from server+nickname, so the link is identical across reinstalls and other PCs and
    /// matches every historical upload; it carries no nickname. The separate stats-web project must serve
    /// this route and must NOT render nickname/server for characters that are not marked public.</summary>
    public string CharacterReportUrl(string identityHash) => $"{BaseUrl}/c/{identityHash}";

    public ConsentStatusResponse GetConsentStatus(string identityHash)
    {
        string encoded = WebUtility.UrlEncode(identityHash);
        StatsHttpResponse response = Request("GET", $"{ConsentStatusEndpoint}?identityHash={encoded}", null, null, null, null);
        ConsentStatusResponse parsed = StatsJson.Deserialize<ConsentStatusResponse>(response.Body);
        if (!parsed.Ok)
        {
            throw new InvalidOperationException("consent_status_not_ok");
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
            consentVersion ?? StatsConsentManager.ConsentVersion);
        ConsentStatusResponse parsed = StatsJson.Deserialize<ConsentStatusResponse>(response.Body);
        if (!parsed.Ok)
        {
            throw new InvalidOperationException("consent_event_not_ok");
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
            payload.ConsentVersion);
        ReportUploadResponse parsed = StatsJson.Deserialize<ReportUploadResponse>(response.Body);
        if (!parsed.Ok)
        {
            throw new InvalidOperationException("report_upload_not_ok");
        }

        return parsed;
    }

    private StatsHttpResponse Request(
        string method,
        string url,
        string? body,
        string? clientVersion,
        string? installId,
        string? consentVersion)
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

        StatsHttpResponse response = _request(method, url, body, headers);
        if (response.StatusCode is < 200 or > 299)
        {
            string summary = response.Body.Length > 300 ? response.Body[..300] : response.Body;
            if (string.IsNullOrEmpty(summary))
            {
                summary = "empty_response";
            }

            throw new InvalidOperationException($"HTTP {response.StatusCode}: {summary}");
        }

        return response;
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
        return new StatsHttpResponse((int)response.StatusCode, text);
    }
}

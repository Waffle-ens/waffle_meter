using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using WaffleMeter.Data;

namespace WaffleMeter.Services;

/// <summary>Official character info pulled from the aion2 site (Kotlin <c>OfficialCharacterInfo</c>).</summary>
public sealed record OfficialCharacterInfo(
    string Nickname,
    int Server,
    JobClass? Job,
    int Power,
    IReadOnlyDictionary<int, int> Skills);

/// <summary>
/// Verbatim port of Kotlin <c>official.OfficialCharacterLookup</c>: resolves a character's job,
/// combat power, and equipped skills from the official aion2 site, with a TTL cache (6h hits /
/// 10min misses) and in-flight de-duplication. Used for the INITIAL combat-power value; live power
/// is parsed from packets ([[combat-power-reverify]]).
///
/// HTTP is injected (<c>httpGet</c>: url -&gt; JSON body, throwing on non-2xx) so the parsing is
/// unit-testable without a network; the clock is injected too (TTL determinism), defaulting to wall
/// clock like the rest of the migration's clock seam.
/// </summary>
public sealed class OfficialCharacterLookup
{
    private const string BaseUrl = "https://aion2.plaync.com";
    private const long SuccessTtlMs = 6L * 60 * 60 * 1000;
    private const long MissTtlMs = 10L * 60 * 1000;
    private const int ConnectTimeoutMs = 3_000;
    private const int ReadTimeoutMs = 5_000;

    private static readonly HttpClient SharedClient = new(
        new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromMilliseconds(ConnectTimeoutMs) })
    {
        Timeout = TimeSpan.FromMilliseconds(ReadTimeoutMs),
    };

    private readonly Func<string, string> _httpGet;
    private readonly Func<long> _clock;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
    private readonly ConcurrentDictionary<string, byte> _inFlight = new();

    public OfficialCharacterLookup(Func<string, string>? httpGet = null, Func<long>? clock = null)
    {
        _httpGet = httpGet ?? DefaultHttpGet;
        _clock = clock ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    public void LookupAsync(string? nickname, int server, JobClass? fallbackJob, Action<OfficialCharacterInfo> callback)
    {
        string? normalized = NormalizeNickname(nickname);
        if (normalized == null || server <= 0)
        {
            return;
        }

        string key = CacheKey(normalized, server);
        long now = _clock();
        if (_cache.TryGetValue(key, out CacheEntry? cached))
        {
            if (cached.ExpiresAt > now)
            {
                if (cached.Info != null)
                {
                    callback(cached.Info);
                }

                return;
            }

            _cache.TryRemove(KeyValuePair.Create(key, cached));
        }

        if (!_inFlight.TryAdd(key, 0))
        {
            return;
        }

        Task.Run(() =>
        {
            try
            {
                OfficialCharacterInfo? info = Lookup(normalized, server, fallbackJob);
                _cache[key] = new CacheEntry(info, now + (info == null ? MissTtlMs : SuccessTtlMs));
                if (info != null)
                {
                    callback(info);
                }
            }
            catch
            {
                _cache[key] = new CacheEntry(null, now + MissTtlMs);
            }
            finally
            {
                _inFlight.TryRemove(key, out _);
            }
        });
    }

    public OfficialCharacterInfo? LookupBlocking(string? nickname, int server, JobClass? fallbackJob)
    {
        string? normalized = NormalizeNickname(nickname);
        if (normalized == null || server <= 0)
        {
            return null;
        }

        string key = CacheKey(normalized, server);
        long now = _clock();
        if (_cache.TryGetValue(key, out CacheEntry? cached))
        {
            if (cached.ExpiresAt > now)
            {
                return cached.Info;
            }

            _cache.TryRemove(KeyValuePair.Create(key, cached));
        }

        try
        {
            OfficialCharacterInfo? info = Lookup(normalized, server, fallbackJob);
            _cache[key] = new CacheEntry(info, now + (info == null ? MissTtlMs : SuccessTtlMs));
            return info;
        }
        catch
        {
            _cache[key] = new CacheEntry(null, now + MissTtlMs);
            return null;
        }
    }

    private OfficialCharacterInfo? Lookup(string nickname, int server, JobClass? fallbackJob)
    {
        CharacterSearchResult? character = FindCharacter(nickname, server);
        if (character == null)
        {
            return null;
        }

        IReadOnlyDictionary<int, int> skills = FetchEquippedSkills(character.CharacterId, character.ServerId);
        int power = FetchCombatPower(character.CharacterId, character.ServerId);
        return new OfficialCharacterInfo(
            nickname,
            character.ServerId,
            character.Job ?? fallbackJob,
            power,
            skills);
    }

    private CharacterSearchResult? FindCharacter(string nickname, int server)
    {
        string url = $"{BaseUrl}/api/search/character?{Query(
            ("keyword", nickname),
            ("pcId", ""),
            ("race", ""),
            ("serverId", server.ToString(CultureInfo.InvariantCulture)),
            ("sort", "desc"),
            ("page", "1"),
            ("size", "20"))}";

        using JsonDocument doc = JsonDocument.Parse(_httpGet(url));
        JsonElement root = doc.RootElement;
        if (!root.TryGetProperty("list", out JsonElement list) || list.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        CharacterSearchResult? best = null;
        foreach (JsonElement element in list.EnumerateArray())
        {
            string name = StripHtml(ContentOrNull(element, "name") ?? string.Empty);
            int? serverId = IntOrNull(element, "serverId");
            if (serverId == null || name != nickname || serverId != server)
            {
                continue;
            }

            string? rawId = ContentOrNull(element, "characterId");
            if (rawId == null)
            {
                continue;
            }

            // The search API returns characterId already URL-encoded (e.g. '=' -> '%3D'); decode once
            // here so Query()'s re-encoding does not double it (%3D -> %253D) and break info/equipment.
            var result = new CharacterSearchResult(
                WebUtility.UrlDecode(rawId),
                serverId.Value,
                IntOrNull(element, "level") ?? 0,
                IntOrNull(element, "pcId") is { } pcId ? JobClassInfo.ConvertFromCode(pcId) : null);

            if (best == null || result.Level > best.Level)
            {
                best = result; // maxByOrNull{level}: keep the first maximum
            }
        }

        return best;
    }

    private IReadOnlyDictionary<int, int> FetchEquippedSkills(string characterId, int server)
    {
        string url = $"{BaseUrl}/api/character/equipment?{Query(
            ("lang", "ko"),
            ("characterId", characterId),
            ("serverId", server.ToString(CultureInfo.InvariantCulture)))}";

        using JsonDocument doc = JsonDocument.Parse(_httpGet(url));
        JsonElement root = doc.RootElement;
        if (!root.TryGetProperty("skill", out JsonElement skill) || skill.ValueKind != JsonValueKind.Object ||
            !skill.TryGetProperty("skillList", out JsonElement skillList) || skillList.ValueKind != JsonValueKind.Array)
        {
            return new Dictionary<int, int>();
        }

        var result = new Dictionary<int, int>();
        foreach (JsonElement element in skillList.EnumerateArray())
        {
            int acquired = IntOrNull(element, "acquired") ?? 0;
            int equipped = IntOrNull(element, "equip") ?? 0;
            if (acquired <= 0 || equipped != 1)
            {
                continue;
            }

            int? code = IntOrNull(element, "id");
            if (code == null)
            {
                continue;
            }

            result[code.Value] = IntOrNull(element, "skillLevel") ?? 0;
        }

        return result;
    }

    private int FetchCombatPower(string characterId, int server)
    {
        try
        {
            string url = $"{BaseUrl}/api/character/info?{Query(
                ("lang", "ko"),
                ("characterId", characterId),
                ("serverId", server.ToString(CultureInfo.InvariantCulture)))}";

            using JsonDocument doc = JsonDocument.Parse(_httpGet(url));
            JsonElement root = doc.RootElement;
            if (root.TryGetProperty("profile", out JsonElement profile) && profile.ValueKind == JsonValueKind.Object)
            {
                return IntOrNull(profile, "combatPower") ?? 0;
            }

            return 0;
        }
        catch
        {
            return 0;
        }
    }

    private static string DefaultHttpGet(string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
        request.Headers.TryAddWithoutValidation("User-Agent", "waffle_meter");
        request.Headers.TryAddWithoutValidation("Referer", $"{BaseUrl}/ko-kr/characters/index");

        using HttpResponseMessage response = SharedClient.Send(request);
        string text = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        int status = (int)response.StatusCode;
        if (status is < 200 or > 299)
        {
            string preview = text.Length > 160 ? text[..160] : text;
            throw new InvalidOperationException($"HTTP {status}: {preview}");
        }

        return text;
    }

    private static string Query(params (string Key, string Value)[] parameters) =>
        string.Join("&", parameters.Select(p => $"{WebUtility.UrlEncode(p.Key)}={WebUtility.UrlEncode(p.Value)}"));

    private static string? NormalizeNickname(string? nickname)
    {
        string? trimmed = nickname?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static string CacheKey(string nickname, int server) => $"{server}:{nickname}";

    private static string StripHtml(string value) => Regex.Replace(value, "<[^>]+>", string.Empty).Trim();

    private static bool TryGetPrimitive(JsonElement obj, string name, out JsonElement value)
    {
        if (obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(name, out value) &&
            value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Object or JsonValueKind.Array))
        {
            return true;
        }

        value = default;
        return false;
    }

    private static string? ContentOrNull(JsonElement obj, string name)
    {
        if (!TryGetPrimitive(obj, name, out JsonElement v))
        {
            return null;
        }

        return v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText();
    }

    private static int? IntOrNull(JsonElement obj, string name)
    {
        if (!TryGetPrimitive(obj, name, out JsonElement v))
        {
            return null;
        }

        string raw = v.ValueKind == JsonValueKind.String ? v.GetString()! : v.GetRawText();
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : null;
    }

    private sealed record CharacterSearchResult(string CharacterId, int ServerId, int Level, JobClass? Job);

    private sealed record CacheEntry(OfficialCharacterInfo? Info, long ExpiresAt);
}

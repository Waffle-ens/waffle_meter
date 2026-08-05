using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using WaffleMeter.Services;
using WaffleMeter.Stats;

namespace WaffleMeter.App.Core;

/// <summary>Diagnostics for the settings screen: what we have and how the last fetch went.</summary>
public sealed record TierServiceStatus(
    bool HasArtifact,
    string? ArtifactId,
    long FetchedAtMs,
    int Rows,
    int Mobs,
    int Failures,
    string? LastError,
    int Dungeons = 0);

/// <summary>
/// Owns the tier distribution artifact: fetches it rarely, verifies it, caches it on disk, and hands the parsed
/// ladder to whoever needs a percentile.
/// <para><b>Never called during combat.</b> The meter's live "상위 X.X%" is computed from the cached artifact, so
/// a fight costs zero requests. Polling the manifest more often than this would not make the number fresher —
/// the server rebuilds the distribution on a multi-hour cadence.</para>
/// <para>Every network call runs on a dedicated background thread. <c>StatsApiClient</c> has no async methods and
/// its transport is a synchronous <c>HttpClient.Send</c> with an 8s connect / 15s read timeout, so touching it
/// from the UI or report thread would stall the meter for up to 23 seconds.</para>
/// </summary>
public sealed class TierService : IDisposable
{
    /// <summary>Artifacts are content-addressed and the server rebuilds on a multi-hour cadence.</summary>
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(12);

    /// <summary>Let capture start and the update check finish before adding another socket.</summary>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(8);

    /// <summary>Manual refresh cooldown (settings button), so a bored click cannot become a poll loop.</summary>
    private static readonly TimeSpan ManualCooldown = TimeSpan.FromSeconds(60);

    private const string KeyArtifactId = "tier.artifactId";
    private const string KeyFetchedAt = "tier.fetchedAtMs";
    private const int KeepCachedArtifacts = 2;

    /// <summary>Server hard cap per lookup request.</summary>
    private const int LookupBatchSize = 12;

    /// <summary>Minimum spacing between lookups. The server allows 120/hour per install; at one per minute we
    /// use at most half that even if the roster churns constantly.</summary>
    private static readonly TimeSpan LookupInterval = TimeSpan.FromSeconds(60);

    /// <summary>How long a resolved tier is reused. The server recomputes hourly, so re-asking sooner cannot
    /// produce a different answer.</summary>
    private static readonly TimeSpan LookupTtl = TimeSpan.FromMinutes(30);

    /// <summary>"이 캐릭터에 대해 줄 것이 없다"는 답은 훨씬 오래 캐시한다 — 미동의·표본부족은 분 단위로
    /// 바뀌지 않으므로, 짧게 캐시하면 같은 사람을 계속 다시 묻게 된다.</summary>
    private static readonly TimeSpan EmptyLookupTtl = TimeSpan.FromHours(2);

    /// <summary>Bound on remembered lookups — a busy recruiting session sees a lot of applicants.</summary>
    private const int MaxCachedLookups = 512;

    private readonly ITierApi _api;
    private readonly PropertyHandler _props;
    private readonly Func<long> _clock;
    private readonly ManualResetEventSlim _wake = new(false);
    private readonly Thread? _worker;
    private volatile bool _stopped;

    private TierArtifact? _artifact;
    private long _fetchedAtMs;
    private int _failures;
    private string? _lastError;
    private long _lastManualMs;

    // Career-tier lookups for OTHER characters (party applicants). Written by the worker, read by the UI, so
    // both maps are concurrent. `_pending` holds hashes we have not asked about yet.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, CachedTier> _lookups = new(StringComparer.Ordinal);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _pending = new(StringComparer.Ordinal);
    private long _nextLookupMs;
    private long _nextArtifactMs;
    private string _clientVersion;

    public TierService(ITierApi api, PropertyHandler props, Func<long>? clock = null, bool startWorker = true, string clientVersion = "dev")
    {
        _api = api;
        _clientVersion = clientVersion;
        _props = props;
        _clock = clock ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        LoadFromDisk();

        if (startWorker)
        {
            _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "tier-artifact" };
            _worker.Start();
        }
    }

    /// <summary>The parsed ladder, or null when nothing has been downloaded yet. Reading is lock-free; the
    /// reference is swapped atomically once a fetch fully succeeds, so a caller never sees a half-built one.</summary>
    public TierArtifact? Artifact => _artifact;

    /// <summary>Age of the cached artifact. The UI marks it stale past a few days but keeps SHOWING it — a badge
    /// that silently vanishes reads as a bug, an old one reads as old.</summary>
    public TimeSpan Age => _fetchedAtMs <= 0
        ? TimeSpan.MaxValue
        : TimeSpan.FromMilliseconds(Math.Max(0, _clock() - _fetchedAtMs));

    public TierServiceStatus Status() => new(
        _artifact != null,
        _artifact?.ArtifactId,
        _fetchedAtMs,
        _artifact?.RowCount ?? 0,
        _artifact?.MobCount ?? 0,
        _failures,
        _lastError,
        _artifact?.DungeonCount ?? 0);

    /// <summary>Settings' 티어 갱신 button. Rate-limited; returns false when the cooldown blocks it.</summary>
    public bool RequestManualRefresh()
    {
        long now = _clock();
        if (now - _lastManualMs < ManualCooldown.TotalMilliseconds)
        {
            return false;
        }

        _lastManualMs = now;
        _wake.Set();
        return true;
    }

    public void Dispose()
    {
        _stopped = true;
        _wake.Set();
        _wake.Dispose();
    }

    private void WorkerLoop()
    {
        if (_wake.Wait(StartupDelay) && _stopped)
        {
            return;
        }

        while (!_stopped)
        {
            _wake.Reset();
            long now = _clock();

            if (now >= _nextArtifactMs)
            {
                TryRefresh();
                _nextArtifactMs = _clock() + (long)RefreshInterval.TotalMilliseconds;
            }

            if (!_pending.IsEmpty && now >= _nextLookupMs)
            {
                TryLookupBatch();
                _nextLookupMs = _clock() + (long)LookupInterval.TotalMilliseconds;
            }

            // Sleep exactly until the next thing is due — no idle polling.
            long wakeAt = _pending.IsEmpty ? _nextArtifactMs : Math.Min(_nextArtifactMs, _nextLookupMs);
            int waitMs = (int)Math.Clamp(wakeAt - _clock(), 0, (long)RefreshInterval.TotalMilliseconds);
            if (_wake.Wait(waitMs) && _stopped)
            {
                return;
            }
        }
    }

    /// <summary>One fetch cycle. Failures are swallowed on purpose — a stats outage must never surface as an
    /// error in a combat overlay; the counter is exposed through <see cref="Status"/> for the settings screen.</summary>
    internal void TryRefresh()
    {
        try
        {
            TierManifestResponse manifest = _api.GetTierManifest();
            if (!TierArtifact.IsSupportedSchemaVersion(manifest.SchemaVersion))
            {
                // A newer document shape: keep serving the cached one rather than guessing at its meaning.
                Fail($"unsupported_schema_{manifest.SchemaVersion}");
                return;
            }

            if (_artifact != null && string.Equals(_artifact.ArtifactId, manifest.ArtifactId, StringComparison.Ordinal))
            {
                _fetchedAtMs = _clock();
                _props.SetProperty(KeyFetchedAt, _fetchedAtMs.ToString(CultureInfo.InvariantCulture));
                _failures = 0;
                _lastError = null;
                return; // unchanged — no download
            }

            StatsBinaryResponse response = _api.GetTierArtifactGzip(manifest.Url);
            if (!VerifyDigest(response.Body, manifest.Sha256))
            {
                Fail("sha256_mismatch");
                return;
            }

            string json = Decompress(response.Body);
            TierArtifact? parsed = TierArtifact.Parse(json);
            if (parsed == null || !string.Equals(parsed.ArtifactId, manifest.ArtifactId, StringComparison.Ordinal))
            {
                Fail("artifact_parse_failed");
                return;
            }

            SaveToDisk(manifest.ArtifactId, response.Body);
            _artifact = parsed;
            _fetchedAtMs = _clock();
            _props.SetProperty(KeyArtifactId, manifest.ArtifactId);
            _props.SetProperty(KeyFetchedAt, _fetchedAtMs.ToString(CultureInfo.InvariantCulture));
            _failures = 0;
            _lastError = null;
        }
        catch (Exception ex)
        {
            Fail(ex is StatsApiException api ? $"http_{api.StatusCode}" : ex.GetType().Name);
        }
    }

    private void Fail(string reason)
    {
        _failures++;
        _lastError = reason;
    }

    /// <summary>A remembered lookup answer. <see cref="Rank"/> 0 means the server had nothing for this
    /// character — not consented, revoked, unknown, or too few battles. Those cases are deliberately
    /// indistinguishable on the wire, so we do not try to tell them apart here either.</summary>
    private readonly record struct CachedTier(int Rank, string Name, double TopPercent, long ExpiresAtMs);

    /// <summary>
    /// The career tier for another character, or null when we do not (yet) have one.
    /// <para>Pure cache read — safe to call from the UI thread every tick. A miss quietly schedules a lookup,
    /// so a caller can just ask again on its next frame.</para>
    /// </summary>
    public (int Rank, string Name, double TopPercent)? CareerTier(string? identityHash)
    {
        if (string.IsNullOrEmpty(identityHash))
        {
            return null;
        }

        if (_lookups.TryGetValue(identityHash!, out CachedTier cached))
        {
            if (_clock() < cached.ExpiresAtMs)
            {
                return cached.Rank > 0 ? (cached.Rank, cached.Name, cached.TopPercent) : null;
            }

            _lookups.TryRemove(identityHash!, out _);
        }

        return null;
    }

    /// <summary>
    /// Ask about these characters' career tiers. Cheap and idempotent: hashes we already know (or have already
    /// queued) are dropped, so calling this on every roster change costs nothing extra.
    /// <para>The request itself is batched and rate-limited on the worker — the panel never waits on it.</para>
    /// </summary>
    public void RequestCareerTiers(IEnumerable<string?> identityHashes)
    {
        bool queued = false;
        long now = _clock();
        foreach (string? hash in identityHashes)
        {
            if (string.IsNullOrEmpty(hash) || hash!.Length != 64)
            {
                continue;
            }

            if (_lookups.TryGetValue(hash, out CachedTier cached) && now < cached.ExpiresAtMs)
            {
                continue; // still fresh, including a remembered "nothing to show"
            }

            queued |= _pending.TryAdd(hash, 0);
        }

        if (queued)
        {
            _wake.Set();
        }
    }

    /// <summary>Run one pending lookup batch synchronously (tests only — the worker owns this in the app).</summary>
    internal void DrainLookupsForTest() => TryLookupBatch();

    /// <summary>One batched lookup. Everything we asked about gets an entry — including the ones the server
    /// declined to describe — so a non-consenting applicant is asked about once every couple of hours rather
    /// than on every roster change.</summary>
    private void TryLookupBatch()
    {
        string[] batch = _pending.Keys.Take(LookupBatchSize).ToArray();
        if (batch.Length == 0)
        {
            return;
        }

        try
        {
            TierLookupResponse response = _api.PostTierLookup(new TierLookupRequest(batch), _clientVersion);
            long now = _clock();
            var answered = new HashSet<string>(StringComparer.Ordinal);

            foreach (TierLookupResult result in response.Results ?? [])
            {
                answered.Add(result.IdentityHash);
                bool ok = string.Equals(result.Status, "ok", StringComparison.Ordinal) && result.TierRank is >= 1 and <= 8;
                Remember(result.IdentityHash, ok
                    ? new CachedTier(result.TierRank, result.TierName ?? string.Empty, result.TopPercent, now + (long)LookupTtl.TotalMilliseconds)
                    : new CachedTier(0, string.Empty, 0, now + (long)EmptyLookupTtl.TotalMilliseconds));
            }

            // A hash the server did not mention at all is treated the same as "nothing to show" — otherwise it
            // would sit in the queue forever and every batch would re-send it.
            foreach (string hash in batch)
            {
                if (!answered.Contains(hash))
                {
                    Remember(hash, new CachedTier(0, string.Empty, 0, now + (long)EmptyLookupTtl.TotalMilliseconds));
                }
            }
        }
        catch (Exception ex)
        {
            // 429 (rate limited) included. The batch stays QUEUED on purpose — dropping it here would mean a
            // rate limit silently costs those applicants their tier until the panel happens to ask again,
            // which it only does when the roster changes.
            Fail(ex is StatsApiException api ? $"lookup_http_{api.StatusCode}" : $"lookup_{ex.GetType().Name}");
            return;
        }

        // Only a completed request retires its hashes; every one of them now has a cache entry.
        foreach (string hash in batch)
        {
            _pending.TryRemove(hash, out _);
        }
    }

    private void Remember(string hash, CachedTier entry)
    {
        _lookups[hash] = entry;
        if (_lookups.Count <= MaxCachedLookups)
        {
            return;
        }

        // Bounded: drop whatever expires soonest. A recruiting session can see hundreds of applicants and this
        // map must not become the reason the meter's memory grows all evening.
        foreach (KeyValuePair<string, CachedTier> oldest in _lookups.OrderBy(kv => kv.Value.ExpiresAtMs).Take(_lookups.Count - MaxCachedLookups))
        {
            _lookups.TryRemove(oldest.Key, out _);
        }
    }

    /// <summary>🔑 The digest is over the COMPRESSED bytes exactly as received. The server sends
    /// <c>Content-Encoding: gzip</c> regardless of <c>Accept-Encoding</c> and hashes what it stored, so a transport
    /// that transparently inflates makes this check impossible to pass.</summary>
    private static bool VerifyDigest(byte[] gzip, string? expectedHex)
    {
        if (string.IsNullOrEmpty(expectedHex) || gzip.Length == 0)
        {
            return false;
        }

        string actual = Convert.ToHexString(SHA256.HashData(gzip)).ToLowerInvariant();
        return string.Equals(actual, expectedHex!.Trim().ToLowerInvariant(), StringComparison.Ordinal);
    }

    private static string Decompress(byte[] gzip)
    {
        using var input = new MemoryStream(gzip, writable: false);
        using var gz = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(gz, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>%APPDATA%\waffle_meter.v1.4\tier — a sub-folder of the CURRENT user-data namespace.
    /// The folder name is load-bearing: renaming it orphans every user's settings/consent/replays.</summary>
    private string CacheDirectory() => Path.Combine(_props.AppDirectory(), "tier");

    /// <summary>Cache the compressed bytes, not the JSON: it is what we verified, and ~4x smaller.
    /// <para>The artifact never goes into settings.properties — that file is rewritten in full on every
    /// SetProperty and re-decoded through a EUC-KR quirk that corrupts non-ASCII. Only the ASCII pointer does.</para></summary>
    private void SaveToDisk(string artifactId, byte[] gzip)
    {
        try
        {
            string dir = CacheDirectory();
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, $"{artifactId}.json.gz"), gzip);

            FileInfo[] stale = new DirectoryInfo(dir).GetFiles("*.json.gz")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Skip(KeepCachedArtifacts)
                .ToArray();
            foreach (FileInfo file in stale)
            {
                try
                {
                    file.Delete();
                }
                catch
                {
                    // a locked leftover is harmless; it is bounded by the keep count on the next success
                }
            }
        }
        catch
        {
            // Disk trouble must not lose the in-memory artifact — it just will not survive a restart.
        }
    }

    private void LoadFromDisk()
    {
        try
        {
            string? id = _props.GetProperty(KeyArtifactId);
            if (string.IsNullOrEmpty(id))
            {
                return;
            }

            string path = Path.Combine(CacheDirectory(), $"{id}.json.gz");
            if (!File.Exists(path))
            {
                return;
            }

            TierArtifact? parsed = TierArtifact.Parse(Decompress(File.ReadAllBytes(path)));
            if (parsed == null)
            {
                return;
            }

            _artifact = parsed;
            if (long.TryParse(_props.GetProperty(KeyFetchedAt), NumberStyles.Integer, CultureInfo.InvariantCulture, out long ms))
            {
                _fetchedAtMs = ms;
            }
        }
        catch
        {
            // A truncated/corrupt cache just means we start without one and fetch on the next cycle.
        }
    }
}

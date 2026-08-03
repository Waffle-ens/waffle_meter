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
    string? LastError);

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

    public TierService(ITierApi api, PropertyHandler props, Func<long>? clock = null, bool startWorker = true)
    {
        _api = api;
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
        _lastError);

    /// <summary>Settings' "등급 기준표 새로고침". Rate-limited; returns false when the cooldown blocks it.</summary>
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
            TryRefresh();
            if (_wake.Wait(RefreshInterval) && _stopped)
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
            if (manifest.SchemaVersion != TierArtifact.SupportedSchemaVersion)
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

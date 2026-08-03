using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WaffleMeter.App.Core;
using WaffleMeter.Services;
using WaffleMeter.Stats;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

/// <summary>
/// Spec for fetching + caching the tier distribution artifact. The integrity check is the reason this class
/// exists: the artifact arrives gzip-encoded and its digest covers the COMPRESSED bytes, so a transport that
/// silently inflates would make verification impossible — a trap worth a permanent test.
/// </summary>
public sealed class TierServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "waffle_tier_tests_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // best effort
        }
    }

    [Fact]
    public void Downloads_verifies_and_caches_the_artifact()
    {
        byte[] gzip = GzipArtifact("abc0123456789def");
        var api = FakeApi(gzip, Sha256Hex(gzip), "abc0123456789def");
        var props = new PropertyHandler(_dir);

        using var service = new TierService(api, props, startWorker: false);
        Assert.Null(service.Artifact);

        service.TryRefresh();

        Assert.NotNull(service.Artifact);
        Assert.Equal("abc0123456789def", service.Artifact!.ArtifactId);
        Assert.Equal("abc0123456789def", props.GetProperty("tier.artifactId"));
        Assert.True(File.Exists(Path.Combine(props.AppDirectory(), "tier", "abc0123456789def.json.gz")));

        TierServiceStatus status = service.Status();
        Assert.True(status.HasArtifact);
        Assert.Equal(0, status.Failures);
        Assert.Null(status.LastError);
    }

    [Fact]
    public void Rejects_an_artifact_whose_digest_does_not_match_the_manifest()
    {
        byte[] gzip = GzipArtifact("abc0123456789def");
        // The manifest advertises a different digest — a corrupted or substituted body.
        var api = FakeApi(gzip, Sha256Hex(Encoding.UTF8.GetBytes("something else")), "abc0123456789def");

        using var service = new TierService(api, new PropertyHandler(_dir), startWorker: false);
        service.TryRefresh();

        Assert.Null(service.Artifact);
        Assert.Equal("sha256_mismatch", service.Status().LastError);
    }

    [Fact]
    public void Digest_is_taken_over_the_compressed_bytes_not_the_json()
    {
        // If a future change enables automatic decompression, the digest would be computed over the plaintext
        // and never match. Pin the direction: hashing the JSON must NOT satisfy the manifest.
        byte[] gzip = GzipArtifact("abc0123456789def");
        string plaintextDigest = Sha256Hex(Encoding.UTF8.GetBytes(ArtifactJson("abc0123456789def")));

        var api = FakeApi(gzip, plaintextDigest, "abc0123456789def");
        using var service = new TierService(api, new PropertyHandler(_dir), startWorker: false);
        service.TryRefresh();

        Assert.Null(service.Artifact);
        Assert.Equal("sha256_mismatch", service.Status().LastError);
    }

    [Fact]
    public void Skips_the_download_when_the_manifest_points_at_the_artifact_we_already_have()
    {
        byte[] gzip = GzipArtifact("abc0123456789def");
        int downloads = 0;
        var api = FakeApi(gzip, Sha256Hex(gzip), "abc0123456789def", onDownload: () => downloads++);
        var props = new PropertyHandler(_dir);

        using var service = new TierService(api, props, startWorker: false);
        service.TryRefresh();
        service.TryRefresh();
        service.TryRefresh();

        Assert.Equal(1, downloads); // the artifact is content-addressed; an unchanged id means nothing to fetch
    }

    [Fact]
    public void Refuses_a_schema_version_it_does_not_understand_and_keeps_the_cached_one()
    {
        byte[] good = GzipArtifact("abc0123456789def");
        var api = FakeApi(good, Sha256Hex(good), "abc0123456789def");
        var props = new PropertyHandler(_dir);
        using var service = new TierService(api, props, startWorker: false);
        service.TryRefresh();
        Assert.NotNull(service.Artifact);

        // Server moves to a shape this build does not know: keep serving the old ladder rather than guessing.
        api.ManifestSchemaVersion = 2;
        service.TryRefresh();

        Assert.NotNull(service.Artifact);
        Assert.Equal("abc0123456789def", service.Artifact!.ArtifactId);
        Assert.Equal("unsupported_schema_2", service.Status().LastError);
    }

    [Fact]
    public void Survives_the_stats_backend_being_down()
    {
        var api = new FakeTierApi { ThrowOnManifest = new StatsApiException("boom", 503, null) };
        using var service = new TierService(api, new PropertyHandler(_dir), startWorker: false);

        service.TryRefresh(); // must not throw — an outage cannot surface in a combat overlay

        Assert.Null(service.Artifact);
        Assert.Equal(1, service.Status().Failures);
        Assert.Equal("http_503", service.Status().LastError);
    }

    [Fact]
    public void Restores_the_cached_artifact_on_the_next_start_without_a_network()
    {
        byte[] gzip = GzipArtifact("abc0123456789def");
        var props = new PropertyHandler(_dir);
        using (var first = new TierService(FakeApi(gzip, Sha256Hex(gzip), "abc0123456789def"), props, startWorker: false))
        {
            first.TryRefresh();
            Assert.NotNull(first.Artifact);
        }

        // Fresh process, backend unreachable: the ladder still comes up from disk.
        var offline = new FakeTierApi { ThrowOnManifest = new StatsApiException("offline", 0, null) };
        using var second = new TierService(offline, new PropertyHandler(_dir), startWorker: false);

        Assert.NotNull(second.Artifact);
        Assert.Equal("abc0123456789def", second.Artifact!.ArtifactId);
    }

    [Fact]
    public void Ignores_a_corrupt_cache_file_instead_of_crashing()
    {
        var props = new PropertyHandler(_dir);
        props.SetProperty("tier.artifactId", "abc0123456789def");
        Directory.CreateDirectory(Path.Combine(props.AppDirectory(), "tier"));
        File.WriteAllBytes(Path.Combine(props.AppDirectory(), "tier", "abc0123456789def.json.gz"), [0x1f, 0x8b, 0x00, 0x01, 0x02]);

        var offline = new FakeTierApi { ThrowOnManifest = new StatsApiException("offline", 0, null) };
        using var service = new TierService(offline, props, startWorker: false);

        Assert.Null(service.Artifact); // no exception, feature simply stays off
    }

    [Fact]
    public void Rate_limits_the_manual_refresh_button()
    {
        long now = 1_000_000;
        byte[] gzip = GzipArtifact("abc0123456789def");
        using var service = new TierService(FakeApi(gzip, Sha256Hex(gzip), "abc0123456789def"),
            new PropertyHandler(_dir), clock: () => now, startWorker: false);

        Assert.True(service.RequestManualRefresh());
        Assert.False(service.RequestManualRefresh()); // still inside the cooldown
        now += 61_000;
        Assert.True(service.RequestManualRefresh());
    }

    // ---- helpers -------------------------------------------------------------------------------------

    private static FakeTierApi FakeApi(byte[] gzip, string sha256, string artifactId, Action? onDownload = null) => new()
    {
        ArtifactId = artifactId,
        Gzip = gzip,
        Sha256 = sha256,
        OnDownload = onDownload,
    };

    private static string Sha256Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static byte[] GzipArtifact(string artifactId)
    {
        byte[] raw = Encoding.UTF8.GetBytes(ArtifactJson(artifactId));
        using var output = new MemoryStream();
        using (var gz = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            gz.Write(raw, 0, raw.Length);
        }

        return output.ToArray();
    }

    private static string ArtifactJson(string artifactId)
    {
        var grid = new List<double>();
        for (int i = 0; i < 31; i++)
        {
            grid.Add(100 - (i * 3.3));
        }

        var cuts = new List<long>();
        long previous = 0;
        for (int i = 0; i < 31; i++)
        {
            long quantised = 100 + i;
            cuts.Add(quantised - previous);
            previous = quantised;
        }

        return JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            artifactId,
            windowDays = 30,
            generatedAt = "2026-08-03T04:00:11.000Z",
            grid,
            tierCuts = new[] { 1, 5, 10, 30, 50, 70, 90 },
            jobs = new[] { "검성", "수호성", "살성", "궁성", "마도성", "정령성", "치유성", "호법성", "권성" },
            dungeons = new object[] { new { ord = 19, key = "sanctuary-muspels-holy-grail", name = "무스펠의 성배", category = "성역" } },
            variants = new object[] { new { dungeonOrd = 19, ord = 2, label = "어려움" } },
            mobs = new Dictionary<string, int[]> { ["2301060"] = [19, 2, 2] },
            rows = new object[]
            {
                new { r = 0, m = "dps", k = "성역", d = 19, v = 2, b = 2, j = "검성", s = 3, p = 5, n = 900, c = cuts },
            },
        });
    }

    /// <summary>Canned manifest + bytes through the ITierApi seam — no sockets, no real transport.</summary>
    private sealed class FakeTierApi : ITierApi
    {
        public string ArtifactId { get; set; } = "abc0123456789def";
        public byte[] Gzip { get; set; } = [];
        public string Sha256 { get; set; } = string.Empty;
        public int ManifestSchemaVersion { get; set; } = 1;
        public Exception? ThrowOnManifest { get; set; }
        public Action? OnDownload { get; set; }

        public TierManifestResponse GetTierManifest()
        {
            if (ThrowOnManifest != null)
            {
                throw ThrowOnManifest;
            }

            return new TierManifestResponse(
                Ok: true,
                ArtifactId: ArtifactId,
                SchemaVersion: ManifestSchemaVersion,
                WindowDays: 30,
                Url: $"/api/v1/tiers/artifact/{ArtifactId}.json",
                ByteSize: Gzip.Length,
                Sha256: Sha256);
        }

        public StatsBinaryResponse GetTierArtifactGzip(string path)
        {
            OnDownload?.Invoke();
            return new StatsBinaryResponse(200, Gzip);
        }
    }
}

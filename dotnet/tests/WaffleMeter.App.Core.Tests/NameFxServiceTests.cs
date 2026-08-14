using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using WaffleMeter.App.Core;
using WaffleMeter.Services;
using WaffleMeter.Stats;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

/// <summary>
/// Spec for the supporter/ranker grant channel. Most of these exist because the tier service — the thing this is
/// modelled on — gets them wrong, and copying it wholesale would have shipped the same defects here.
/// </summary>
public sealed class NameFxServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "waffle_namefx_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private static bool KnownEffect(string id) => id is "syrup" or "goldleaf";

    private static bool KnownGauge(string id) => id is "prism" or "ember";

    [Fact]
    public void Downloads_verifies_and_caches_the_roster()
    {
        byte[] gzip = GzipRoster("""{"schemaVersion":1,"entries":[{"h":"AAAA","e":"syrup","k":"supporter"}]}""");
        var api = FakeApi(gzip, Sha256Hex(gzip), "cafe0123");
        var props = new PropertyHandler(_dir);

        using var service = new NameFxService(api, props, KnownEffect, KnownGauge, startWorker: false);
        Assert.Equal(0, service.Roster.Count);

        service.TryRefresh();

        Assert.Equal(1, service.Roster.Count);
        Assert.Equal("syrup", service.Roster.Find("AAAA")!.EffectId);
        Assert.Equal("cafe0123", props.GetProperty("namefx.artifactId"));
        Assert.True(File.Exists(Path.Combine(props.AppDirectory(), "namefx", "cafe0123.json.gz")));

        NameFxServiceStatus status = service.Status();
        Assert.True(status.HasArtifact);
        Assert.Equal(1, status.Grants);
        Assert.Equal(0, status.Failures);
        Assert.False(status.UsingLocalFile);
    }

    [Fact]
    public void A_roster_that_arrives_corrupted_is_refused_and_nothing_is_cached()
    {
        byte[] gzip = GzipRoster("""{"schemaVersion":1,"entries":[{"h":"AAAA","e":"syrup","k":"supporter"}]}""");
        var api = FakeApi(gzip, Sha256Hex(Encoding.UTF8.GetBytes("something else")), "cafe0123");
        var props = new PropertyHandler(_dir);

        using var service = new NameFxService(api, props, KnownEffect, KnownGauge, startWorker: false);
        service.TryRefresh();

        Assert.Equal(0, service.Roster.Count);
        Assert.Equal("sha256_mismatch", service.Status().LastError);
        Assert.False(Directory.Exists(Path.Combine(props.AppDirectory(), "namefx")));
    }

    [Fact]
    public void A_newer_document_version_is_refused_rather_than_guessed_at()
    {
        byte[] gzip = GzipRoster("""{"schemaVersion":2,"entries":[]}""");
        var api = FakeApi(gzip, Sha256Hex(gzip), "cafe0123", schemaVersion: 2);
        var props = new PropertyHandler(_dir);

        using var service = new NameFxService(api, props, KnownEffect, KnownGauge, startWorker: false);
        service.TryRefresh();

        Assert.Equal("unsupported_schema_2", service.Status().LastError);
        Assert.Null(props.GetProperty("namefx.artifactId"));
    }

    [Fact]
    public void An_unchanged_artifact_costs_no_download()
    {
        byte[] gzip = GzipRoster("""{"schemaVersion":1,"entries":[{"h":"AAAA","e":"syrup","k":"supporter"}]}""");
        int downloads = 0;
        var api = FakeApi(gzip, Sha256Hex(gzip), "cafe0123", onDownload: () => downloads++);
        var props = new PropertyHandler(_dir);

        using var service = new NameFxService(api, props, KnownEffect, KnownGauge, startWorker: false);
        service.TryRefresh();
        service.TryRefresh();
        service.TryRefresh();

        Assert.Equal(1, downloads);
        Assert.Equal(1, service.Roster.Count);
    }

    [Fact]
    public void The_poll_matches_the_servers_hourly_rebuild()
    {
        // 서버는 티어 갱신(매시) 끝에서 명단을 다시 발행한다. 폴링이 그보다 굵으면 새 랭커가
        // 붙기까지 이유 없이 기다리게 된다. 매시가 감당되는 건 문서가 콘텐츠 주소라서다 —
        // 안 바뀐 시각에는 매니페스트 한 번이고 다운로드가 없다.
        byte[] gzip = GzipRoster("""{"schemaVersion":1,"entries":[]}""");
        var props = new PropertyHandler(_dir);
        long now = 1_000_000;

        using var service = new NameFxService(FakeApi(gzip, Sha256Hex(gzip), "cafe0123"), props, KnownEffect, KnownGauge,
            clock: () => now, startWorker: false);
        service.TryRefresh();

        long untilNext = service.NextArtifactCheckAtMs - now;
        Assert.Equal((long)TimeSpan.FromHours(1).TotalMilliseconds, untilNext);
    }

    [Fact]
    public void A_failure_retries_sooner_than_the_normal_cadence()
    {
        // The tier service has no backoff: a 503 there costs a full interval, and the retry is the only way back.
        var api = new ThrowingApi();
        var props = new PropertyHandler(_dir);
        long now = 1_000_000;

        using var service = new NameFxService(api, props, KnownEffect, KnownGauge, clock: () => now, startWorker: false);
        service.TryRefresh();

        long firstRetryIn = service.NextArtifactCheckAtMs - now;
        Assert.InRange(firstRetryIn, 1, (long)TimeSpan.FromMinutes(5).TotalMilliseconds);

        service.TryRefresh();
        long secondRetryIn = service.NextArtifactCheckAtMs - now;
        Assert.True(secondRetryIn > firstRetryIn, $"백오프가 늘지 않았다: {firstRetryIn} → {secondRetryIn}");

        for (int i = 0; i < 20; i++)
        {
            service.TryRefresh();
        }

        // ...but never past the normal cadence, or a long outage turns into a permanently dead channel.
        Assert.True(service.NextArtifactCheckAtMs - now <= (long)TimeSpan.FromHours(1).TotalMilliseconds);
    }

    [Fact]
    public void Manual_refresh_makes_the_fetch_due_rather_than_only_waking_the_thread()
    {
        byte[] gzip = GzipRoster("""{"schemaVersion":1,"entries":[]}""");
        var api = FakeApi(gzip, Sha256Hex(gzip), "cafe0123");
        var props = new PropertyHandler(_dir);
        long now = 1_000_000;

        using var service = new NameFxService(api, props, KnownEffect, KnownGauge, clock: () => now, startWorker: false);
        service.TryRefresh();
        Assert.True(service.NextArtifactCheckAtMs > now);

        now += 61_000; // 쿨다운 밖
        Assert.True(service.RequestManualRefresh());
        Assert.True(service.NextArtifactCheckAtMs <= now);
    }

    [Fact]
    public void The_cached_artifact_comes_back_without_a_network_call()
    {
        byte[] gzip = GzipRoster("""{"schemaVersion":1,"entries":[{"h":"AAAA","e":"syrup","k":"supporter"}]}""");
        var props = new PropertyHandler(_dir);
        using (var first = new NameFxService(FakeApi(gzip, Sha256Hex(gzip), "cafe0123"), props, KnownEffect, KnownGauge, startWorker: false))
        {
            first.TryRefresh();
        }

        var offline = new ThrowingApi();
        using var restarted = new NameFxService(offline, new PropertyHandler(_dir), KnownEffect, KnownGauge, startWorker: false);

        Assert.Equal(1, restarted.Roster.Count);
        Assert.Equal(0, offline.Calls);
    }

    [Fact]
    public void The_plain_local_file_still_works_when_no_artifact_has_been_fetched()
    {
        // 서버 채널이 붙기 전부터 있던 개발/데모 경로. 서버가 없는 빌드에서도 화면이 나오게 남겨 둔다.
        var props = new PropertyHandler(_dir);
        Directory.CreateDirectory(Path.Combine(props.AppDirectory(), "namefx"));
        File.WriteAllText(
            NameFxRoster.FilePath(props.AppDirectory()),
            """{"schemaVersion":1,"entries":[{"h":"BBBB","e":"goldleaf","k":"ranker","g":"prism"}]}""");

        using var service = new NameFxService(new ThrowingApi(), props, KnownEffect, KnownGauge, startWorker: false);

        Assert.Equal(1, service.Roster.Count);
        Assert.True(service.Status().UsingLocalFile);
    }

    [Fact]
    public void Sweeping_old_artifacts_does_not_eat_the_plain_local_file()
    {
        // 캐시 스윕은 같은 폴더에 사는 supporters.json 을 지우면 안 된다. 티어 쪽 스윕을 그대로 베끼면
        // 확장자만 보고 지우므로, 여기서 확장자 범위를 못박아 둔다.
        var props = new PropertyHandler(_dir);
        Directory.CreateDirectory(Path.Combine(props.AppDirectory(), "namefx"));
        string plain = NameFxRoster.FilePath(props.AppDirectory());
        File.WriteAllText(plain, """{"schemaVersion":1,"entries":[]}""");

        byte[] gzip = GzipRoster("""{"schemaVersion":1,"entries":[]}""");
        using var service = new NameFxService(FakeApi(gzip, Sha256Hex(gzip), "cafe0123"), props, KnownEffect, KnownGauge, startWorker: false);
        service.TryRefresh();

        Assert.True(File.Exists(plain));
    }

    [Fact]
    public void Replacing_the_roster_announces_it()
    {
        // 이 이벤트가 유일한 반영 경로다. 오버레이가 부여를 (서버, 닉네임)으로 메모하고 있어서, 다운로드가
        // 성공해도 아무도 SetNameFxRoster 를 부르지 않으면 화면은 다음 실행까지 그대로다 — 로그도 없이.
        byte[] gzip = GzipRoster("""{"schemaVersion":1,"entries":[{"h":"AAAA","e":"syrup","k":"supporter"}]}""");
        var props = new PropertyHandler(_dir);
        var seen = new List<int>();

        using var service = new NameFxService(FakeApi(gzip, Sha256Hex(gzip), "cafe0123"), props, KnownEffect, KnownGauge, startWorker: false);
        service.Changed += r => seen.Add(r.Count);
        service.TryRefresh();

        Assert.Equal(new[] { 1 }, seen);
    }

    [Fact]
    public void An_effect_id_this_build_cannot_draw_is_dropped()
    {
        byte[] gzip = GzipRoster("""
            {"schemaVersion":1,"entries":[
              {"h":"AAAA","e":"from_a_newer_build","k":"supporter"},
              {"h":"BBBB","e":"goldleaf","k":"ranker","g":"unknown_gauge"}]}
            """);
        var props = new PropertyHandler(_dir);

        using var service = new NameFxService(FakeApi(gzip, Sha256Hex(gzip), "cafe0123"), props, KnownEffect, KnownGauge, startWorker: false);
        service.TryRefresh();

        Assert.Null(service.Roster.Find("AAAA"));
        // 모르는 게이지 하나가 닉네임 효과까지 데리고 사라지면 안 된다.
        Assert.Equal("goldleaf", service.Roster.Find("BBBB")!.EffectId);
        Assert.Null(service.Roster.Find("BBBB")!.GaugeId);
    }

    private static byte[] GzipRoster(string json)
    {
        using var output = new MemoryStream();
        using (var gz = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            gz.Write(bytes, 0, bytes.Length);
        }

        return output.ToArray();
    }

    private static string Sha256Hex(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static FakeNameFxApi FakeApi(byte[] gzip, string sha, string artifactId, int schemaVersion = 1, Action? onDownload = null) =>
        new(gzip, sha, artifactId, schemaVersion, onDownload);

    private sealed class FakeNameFxApi(byte[] gzip, string sha, string artifactId, int schemaVersion, Action? onDownload) : INameFxApi
    {
        public NameFxManifestResponse GetNameFxManifest() =>
            new(true, artifactId, schemaVersion, $"/api/v1/supporters/artifact/{artifactId}.json", gzip.Length, sha);

        public StatsBinaryResponse GetNameFxArtifactGzip(string path)
        {
            onDownload?.Invoke();
            return new StatsBinaryResponse(200, gzip);
        }
    }

    private sealed class ThrowingApi : INameFxApi
    {
        public int Calls { get; private set; }

        public NameFxManifestResponse GetNameFxManifest()
        {
            Calls++;
            throw new StatsApiException("namefx_manifest_http_503", 503, null);
        }

        public StatsBinaryResponse GetNameFxArtifactGzip(string path)
        {
            Calls++;
            throw new StatsApiException("namefx_artifact_http_503", 503, null);
        }
    }
}

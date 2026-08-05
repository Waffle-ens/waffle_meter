using WaffleMeter.Capture;
using WaffleMeter.Data;
using WaffleMeter.Services;
using WaffleMeter.Stats;
using Xunit;

namespace WaffleMeter.Stats.Tests;

/// <summary>
/// Spec for retrying a transient upload failure.
/// <para>Before this, one 502 or one dropped connection lost a battle permanently — the queue has no spool and
/// nothing ever re-sent it. Observed twice: six uploads on 2026-08-04 when nginx marked both upstreams down
/// over slow responses, two more on 08-05 to the same cause.</para>
/// </summary>
public sealed class StatsUploadRetryTests : IDisposable
{
    private readonly string _temp;
    private readonly PropertyHandler _props;
    private readonly DataManager _dm;
    private readonly List<int> _sleeps = [];
    private long _now = 1_000;

    public StatsUploadRetryTests()
    {
        _temp = Path.Combine(Path.GetTempPath(), "wm_retry_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_temp);
        _props = new PropertyHandler(_temp);
        _dm = new DataManager();
        _dm.SaveNickname(1, "Me", isExecutor: true, server: 3, jobByte: 5);
        _dm.SaveUserPower(1, 500_000);
        _dm.SaveNickname(2, "Ally", isExecutor: false, server: 3, jobByte: 25);
        _dm.SaveUserPower(2, 400_000);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_temp, recursive: true);
        }
        catch
        {
            // best effort
        }
    }

    /// <summary>An API whose report endpoint answers from a scripted queue; everything else succeeds.</summary>
    private StatsApiClient ScriptedApi(params StatsHttpResponse[] reportResponses)
    {
        var remaining = new Queue<StatsHttpResponse>(reportResponses);
        return new StatsApiClient(() => "install-1", (_, url, _, _) =>
        {
            if (!url.Contains("/api/v1/reports"))
            {
                return new StatsHttpResponse(200, """{"ok":true,"identityHash":"h","exists":true,"consentState":"accepted","public":false,"consentVersion":"2026-06-04"}""");
            }

            Attempts++;
            return remaining.Count > 0
                ? remaining.Dequeue()
                : new StatsHttpResponse(200, """{"ok":true,"reportId":"r1","duplicate":false}""");
        });
    }

    private int Attempts { get; set; }

    private static readonly StatsHttpResponse Ok = new(200, """{"ok":true,"reportId":"r1","duplicate":false}""");

    private StatsUploadQueue NewQueue(StatsApiClient api)
    {
        var builder = new StatsPayloadBuilder(_dm, () => false);
        var consent = new StatsConsentManager(_props, _dm, api, () => builder.OwnCharacter());
        consent.Set("accepted", uploadEnabled: true, publicCharacter: false);
        return new StatsUploadQueue(consent, builder, api, _dm, _props,
            dispatch: job => job(),
            killRecheckDelay: () => { },
            clock: () => _now,
            retryDelay: ms => _sleeps.Add(ms));
    }

    private DpsLog BossLog(int mobCode = 12345)
    {
        User me = _dm.User(1)!;
        User ally = _dm.User(2)!;
        return new DpsLog
        {
            Report = new DpsReport
            {
                Contributors = [me, ally],
                BattleStart = 1_000_000,
                BattleEnd = 1_030_000,
                Target = new MobInfo(100, new Mob(mobCode, "보스", true), remainHp: 0, maxHp: 1_000_000),
                Information = new Dictionary<int, DpsInformation>
                {
                    [1] = new DpsInformation(1_000_000, 50_000, 60.0, 40.0),
                    [2] = new DpsInformation(600_000, 30_000, 40.0, 24.0),
                },
            },
            SkillDetails = new Dictionary<int, Dictionary<string, AnalyzedSkill>>
            {
                [1] = new() { ["11020001"] = new AnalyzedSkill { SkillCode = 11020001, Name = "강타", DamageAmount = 1_000_000, Times = 100 } },
            },
            BuffRates = new Dictionary<int, List<OperatingData>>(),
            BossBuffRates = [],
        };
    }

    // ── what gets retried ────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(429)]   // nginx limit_req_status — a code we actually receive, and retrying IS the answer
    [InlineData(408)]
    public void A_transient_status_is_retried_and_can_succeed(int status)
    {
        using StatsUploadQueue queue = NewQueue(ScriptedApi(new StatsHttpResponse(status, "down"), Ok));

        queue.OfferIfEligible(BossLog());

        Assert.Equal(2, Attempts);
        Assert.Equal(1, queue.Status().Uploaded);
        Assert.Equal(0, queue.Status().Failed);
    }

    /// <summary>A verdict on the request itself. Re-sending returns the same answer, so it must not be
    /// re-sent — 400 unsupported_encounter is the common one and would be retried forever.</summary>
    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(409)]
    [InlineData(413)]
    public void A_permanent_status_is_not_retried(int status)
    {
        using StatsUploadQueue queue = NewQueue(ScriptedApi(new StatsHttpResponse(status, "nope"), Ok));

        queue.OfferIfEligible(BossLog());

        Assert.Equal(1, Attempts);
        Assert.Equal(1, queue.Status().Failed);
        Assert.Empty(_sleeps);
    }

    [Fact]
    public void Attempts_are_bounded_and_the_failure_is_still_reported()
    {
        using StatsUploadQueue queue = NewQueue(ScriptedApi(
            new StatsHttpResponse(503, "a"), new StatsHttpResponse(503, "b"), new StatsHttpResponse(503, "c"),
            new StatsHttpResponse(503, "d")));

        queue.OfferIfEligible(BossLog());

        Assert.Equal(3, Attempts);                       // initial + 2 retries
        Assert.Equal(1, queue.Status().Failed);
        Assert.StartsWith("upload_failed:", queue.Status().LastReason);
    }

    // ── backoff ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Backoff_doubles()
    {
        using StatsUploadQueue queue = NewQueue(ScriptedApi(
            new StatsHttpResponse(503, "a"), new StatsHttpResponse(503, "b"), Ok));

        queue.OfferIfEligible(BossLog());

        Assert.Equal([1_000, 2_000], _sleeps);
    }

    /// <summary>The server saying how long it needs beats guessing.</summary>
    [Fact]
    public void A_retry_after_header_wins_over_the_backoff()
    {
        using StatsUploadQueue queue = NewQueue(ScriptedApi(
            new StatsHttpResponse(429, "slow down", RetryAfterSeconds: 3), Ok));

        queue.OfferIfEligible(BossLog());

        Assert.Equal([3_000], _sleeps);
    }

    /// <summary>…but a rate limit measured in minutes must not park the single upload worker for minutes.</summary>
    [Fact]
    public void A_huge_retry_after_is_capped()
    {
        using StatsUploadQueue queue = NewQueue(ScriptedApi(
            new StatsHttpResponse(429, "slow down", RetryAfterSeconds: 600), Ok));

        queue.OfferIfEligible(BossLog());

        Assert.Equal([10_000], _sleeps);
    }

    // ── the queue must not seize up during an outage ─────────────────────────────────────────────

    /// <summary>🔑 Retries run on the single upload worker, so their sleeping delays every battle behind them.
    /// After one battle burns its whole budget the retries pause, and the next battles fail fast instead of
    /// each adding the full retry cost to the queue.</summary>
    [Fact]
    public void After_a_battle_exhausts_its_retries_the_next_ones_fail_fast()
    {
        using StatsUploadQueue queue = NewQueue(ScriptedApi(
            [.. Enumerable.Repeat(new StatsHttpResponse(503, "down"), 10)]));

        queue.OfferIfEligible(BossLog(mobCode: 111));
        int afterFirst = Attempts;
        _sleeps.Clear();

        queue.OfferIfEligible(BossLog(mobCode: 222));

        Assert.Equal(3, afterFirst);                 // the first battle paid full price
        Assert.Equal(afterFirst + 1, Attempts);      // the second tried once
        Assert.Empty(_sleeps);                       // and never slept
    }

    /// <summary>The pause is time-boxed, not permanent — the server coming back must not need a restart.</summary>
    [Fact]
    public void The_pause_expires()
    {
        using StatsUploadQueue queue = NewQueue(ScriptedApi(
            new StatsHttpResponse(503, "a"), new StatsHttpResponse(503, "b"), new StatsHttpResponse(503, "c"),
            new StatsHttpResponse(503, "d"), Ok));

        queue.OfferIfEligible(BossLog(mobCode: 111));
        _now += 61_000;
        queue.OfferIfEligible(BossLog(mobCode: 222));

        Assert.Equal(1, queue.Status().Uploaded);
        Assert.NotEmpty(_sleeps);
    }

    /// <summary>A success clears the pause immediately.</summary>
    [Fact]
    public void A_success_re_enables_retrying()
    {
        using StatsUploadQueue queue = NewQueue(ScriptedApi(
            new StatsHttpResponse(503, "a"), new StatsHttpResponse(503, "b"), new StatsHttpResponse(503, "c"),
            Ok,                                           // battle 2 succeeds on its single allowed attempt
            new StatsHttpResponse(503, "e"), Ok));        // battle 3 retries again

        queue.OfferIfEligible(BossLog(mobCode: 111));
        queue.OfferIfEligible(BossLog(mobCode: 222));
        _sleeps.Clear();
        queue.OfferIfEligible(BossLog(mobCode: 333));

        Assert.Equal(2, queue.Status().Uploaded);
        Assert.Equal([1_000], _sleeps);
    }
}

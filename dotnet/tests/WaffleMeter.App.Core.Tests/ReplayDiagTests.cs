using WaffleMeter.App.Core;
using WaffleMeter.Capture;
using WaffleMeter.Data;
using WaffleMeter.Replay;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

public sealed class ReplayDiagTests
{
    [Fact]
    public void Format_reports_wipe_coverage_and_self_density()
    {
        var report = new DpsReport
        {
            Contributors =
            [
                new User(1) { Nickname = "본인", IsExecutor = true },
                new User(2) { Nickname = "파티원" },
            ],
            Target = new MobInfo(99, new Mob(2301059, "무스펠", Boss: true), remainHp: 4321, maxHp: 9999),
        };

        var rec = new ReplayRecording
        {
            StartMs = 1_000,
            EndMs = 61_000,
            BossDefeated = false,
            TargetCode = 2301059,
            TargetName = "무스펠",
            Tracks =
            [
                new ReplayTrack { Uid = 1, IsSelf = true, Points = [new(0, 1, 1, 1), new(100, 2, 1, 1), new(400, 3, 1, 1)] },
                new ReplayTrack { Uid = 2, Points = [new(0, 5, 5, 1)] }, // participated, no real path (AoI gap)
                new ReplayTrack { Uid = 99, IsTarget = true, Points = [new(0, 9, 9, 1), new(50, 9, 8, 1), new(90, 9, 7, 1)] },
            ],
        };

        string line = ReplayDiag.Format(report, rec, rosterCount: 4);

        Assert.Contains("roster=4", line);       // the party scoping the replay was built with
        Assert.Contains("defeated=False", line); // the wipe marker — proves the line fires on a wipe
        Assert.Contains("hp=4321/9999", line);   // raw HP rides along to audit the inference
        Assert.Contains("target=무스펠(2301059)", line);
        Assert.Contains("contributors=2", line);
        Assert.Contains("path=1/2", line);       // AoI coverage: 1 of 2 player tracks has a real path
        Assert.Contains("self=3pt/p90=300ms", line);
        Assert.Contains("boss=3pt", line);
        Assert.Contains("dur=60s", line);
    }

    [Fact]
    public void Format_marks_a_missing_self_track()
    {
        var report = new DpsReport { Contributors = [new User(2) { Nickname = "파티원" }] };
        var rec = new ReplayRecording
        {
            StartMs = 0,
            EndMs = 10_000,
            Tracks = [new ReplayTrack { Uid = 2, Points = [new(0, 1, 1, 1), new(100, 2, 1, 1), new(200, 3, 1, 1)] }],
        };

        string line = ReplayDiag.Format(report, rec);

        Assert.Contains("self=MISSING", line);
        Assert.Contains("hp=-", line); // no target captured
        Assert.Contains("path=1/1", line);
        Assert.DoesNotContain("roster=", line); // unknown roster count stays out of the line
    }
}

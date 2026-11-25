using System.Text.Json;
using WaffleMeter.App.Core;
using WaffleMeter.Capture;
using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

/// <summary>
/// 1인 모드(클라 134~) 전투에는 등급을 매기지 않는다. 컷은 파티 전투로 만든 분포인데 1인 모드는 보스 체력·
/// 피해가 다르고 직업 버프가 전방/후방 피해 증폭을 바꾼다. 보스 코드는 파티 판과 같아서 아티팩트의 몹 맵은
/// 둘을 같은 좌표로 놓는다 — 그래서 리포트의 플래그로 막아야 한다.
/// </summary>
public sealed class TierSoloModeTests
{
    private const int BossCode = 2301601;

    private static readonly double[] Grid =
    [
        100, 99.5, 99, 98, 96, 93, 90, 85, 80, 75, 70, 65, 60, 55, 50, 45, 40, 35, 30, 25, 20, 15, 12.5, 10, 7.5,
        5, 3, 2, 1, 0.5, 0.1,
    ];

    private static TierArtifact Artifact()
    {
        var encoded = new long[Grid.Length];
        long previous = 0;
        for (int i = 0; i < Grid.Length; i++)
        {
            long quantised = (100_000 + 10_000L * i) / 100;
            encoded[i] = quantised - previous;
            previous = quantised;
        }

        var row = new Dictionary<string, object>
        {
            ["r"] = 0, ["m"] = "dps", ["k"] = "원정", ["d"] = 11, ["v"] = 3, ["b"] = 1,
            ["j"] = "살성", ["s"] = 0, ["p"] = 5, ["n"] = 500, ["c"] = encoded,
        };

        var document = new Dictionary<string, object?>
        {
            ["schemaVersion"] = 3,
            ["artifactId"] = "solo-mode-fixture",
            ["windowDays"] = 31,
            ["generatedAt"] = "2026-11-25T00:00:00.000Z",
            ["grid"] = Grid,
            ["jobs"] = new[] { "검성", "수호성", "살성", "궁성", "마도성", "정령성", "치유성", "호법성", "권성" },
            ["dungeons"] = new object[]
            {
                new { ord = 11, key = "expedition-fallen-deva-castle", name = "타락한 데바의 성", category = "원정" },
            },
            ["variants"] = new object[] { new { dungeonOrd = 11, ord = 3, label = "어려움" } },
            ["mobs"] = new Dictionary<string, int[]> { [BossCode.ToString()] = [11, 3, 1] },
            ["rows"] = new object[] { row },
        };

        TierArtifact? artifact = TierArtifact.Parse(JsonSerializer.Serialize(document));
        Assert.NotNull(artifact);
        return artifact!;
    }

    private static DpsReport Report(bool soloMode)
    {
        var report = new DpsReport
        {
            Target = new MobInfo(1, new Mob(BossCode, "보스", Boss: true)),
            BattleStart = 1_000_000,
            BattleEnd = 1_060_000,
            SoloMode = soloMode,
        };
        report.Contributors.Add(new User(7, "본인", 2003, JobClass.ASSASSIN, power: 900_000));
        report.Information[7] = new DpsInformation(200_000 * 60.0, 200_000, 100, 100);
        return report;
    }

    [Fact]
    public void A_party_run_of_the_same_boss_is_evaluated() =>
        Assert.NotEmpty(TierEvaluator.Evaluate(Report(soloMode: false), Artifact()));

    [Fact]
    public void A_solo_mode_run_gets_no_tier_at_all()
    {
        var careers = new Dictionary<string, int> { ["hash"] = 3 };

        Assert.Empty(TierEvaluator.Evaluate(Report(soloMode: true), Artifact(), careers, _ => "hash"));
    }
}

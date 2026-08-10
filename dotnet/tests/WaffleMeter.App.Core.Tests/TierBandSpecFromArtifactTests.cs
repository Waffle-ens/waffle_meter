using System.Text.Json;
using WaffleMeter.App.Core;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

/// <summary>
/// 밴드 규격(폭·하한)은 아티팩트가 선언하고 미터가 따른다.
/// <para>배경: 폭이 양쪽에 상수로 박혀 있으면 서버가 그것을 바꾸는 순간 미터가 존재하지 않는 행 키를 조회하고,
/// <b>오류 없이</b> 전 사용자가 전체 기준으로 떨어진다. 그래서 폭은 바꿀 수 없는 값이었다. 폭은 표본이
/// 쌓이는 만큼 좁아질(50k → 20k) 예정이므로 이 배선이 그 전제다.</para>
/// </summary>
public sealed class TierBandSpecFromArtifactTests
{
    /// <summary>서버가 싣는 31칸 그리드(TierPowerBandTests와 동일 형태).</summary>
    private static readonly double[] Grid =
    [
        100, 99.5, 99, 98, 96, 93, 90, 85, 80, 75, 70, 65, 60, 55, 50, 45, 40, 35, 30, 25, 20, 15, 12.5, 10, 7.5,
        5, 3, 2, 1, 0.5, 0.1,
    ];

    private static object Row() => new Dictionary<string, object>
    {
        ["r"] = 0, ["m"] = "dps", ["k"] = "원정", ["d"] = 11, ["v"] = 3, ["b"] = 1,
        ["j"] = "검성", ["s"] = 3, ["p"] = 5, ["n"] = 500, ["w"] = 1,
        ["c"] = System.Linq.Enumerable.Repeat(1000, Grid.Length).ToArray(),
    };

    /// <summary>파서가 요구하는 최소 문서 + 밴드 규격만 바꿔 끼운다(TierPowerBandTests의 픽스처와 같은 형태).</summary>
    private static TierArtifact? Build(int? bandSize, int? bandFloor)
    {
        var document = new Dictionary<string, object?>
        {
            ["schemaVersion"] = 2,
            ["artifactId"] = "band-spec-fixture",
            ["windowDays"] = 7,
            ["generatedAt"] = "2026-08-10T00:00:00.000Z",
            ["grid"] = Grid,
            ["jobs"] = new[] { "검성", "수호성", "살성", "궁성", "마도성", "정령성", "치유성", "호법성", "권성" },
            ["dungeons"] = new object[]
            {
                new { ord = 11, key = "expedition-fallen-deva-castle", name = "타락한 데바의 성", category = "원정" },
            },
            ["variants"] = new object[] { new { dungeonOrd = 11, ord = 3, label = "어려움" } },
            ["mobs"] = new Dictionary<string, int[]> { ["2301601"] = [11, 3, 1] },
            ["rows"] = new object[] { Row() },
        };

        if (bandSize is { } size)
        {
            document["powerBandSize"] = size;
        }

        if (bandFloor is { } floor)
        {
            document["powerBandFloor"] = floor;
        }

        return TierArtifact.Parse(JsonSerializer.Serialize(document));
    }

    [Fact]
    public void An_artifact_that_declares_the_spec_is_obeyed()
    {
        TierArtifact? a = Build(20_000, 300_000);

        Assert.NotNull(a);
        Assert.Equal(20_000, a!.PowerBandSize);
        Assert.Equal(300_000, a.PowerBandFloor);
        Assert.Equal(820_000, a.BandFor(835_000));   // 20k 격자
        Assert.Equal(300_000, a.BandFor(250_000));   // 하한 아래는 하한으로
    }

    [Fact]
    public void An_artifact_without_the_spec_keeps_the_values_it_was_built_with()
    {
        // 이 필드가 생기기 전에 발행된 아티팩트 — 그때 쓰던 50k/400k가 곧 정답이다.
        TierArtifact? a = Build(null, null);

        Assert.NotNull(a);
        Assert.Equal(TierArtifact.DefaultPowerBandSize, a!.PowerBandSize);
        Assert.Equal(TierArtifact.DefaultPowerBandFloor, a.PowerBandFloor);
        Assert.Equal(850_000, a.BandFor(875_000));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-50_000)]
    public void A_broken_spec_falls_back_instead_of_dividing_by_zero(int broken)
    {
        TierArtifact? a = Build(broken, null);

        Assert.NotNull(a);
        Assert.Equal(TierArtifact.DefaultPowerBandSize, a!.PowerBandSize);
        Assert.Equal(850_000, a.BandFor(875_000));
    }

    [Fact]
    public void The_comparison_label_reads_the_declared_width_not_a_constant()
    {
        // 🔑 폭이 바뀌었는데 라벨이 상수를 쓰면 "800k–850k 기준"이라 적어 놓고 실제로는 800k–820k 분포를
        // 보여준다. 숫자가 아니라 문장이 틀리는 종류의 버그라 화면만 봐서는 못 잡는다.
        Assert.Equal("전투력 800k–820k 미만 기준", TierLadder.FormatComparisonBasis(800_000, 20_000));
        Assert.Equal("전투력 800k–850k 미만 기준", TierLadder.FormatComparisonBasis(800_000, 50_000));
        Assert.Equal("전체 전투력 기준", TierLadder.FormatComparisonBasis(TierArtifact.WholeCohortBand, 20_000));
    }

    [Fact]
    public void An_evaluation_carries_the_width_that_produced_it()
    {
        // 표시 시점에 상수를 다시 가져오면, 아티팩트가 갱신된 뒤 라벨만 옛 폭으로 남는다.
        var evaluation = new TierEvaluation(0, 0, 3.2, 2, PowerBand: 600_000, PowerBandSize: 20_000);

        Assert.Equal("전투력 600k–620k 미만 기준", evaluation.ComparisonBasis);
    }
}

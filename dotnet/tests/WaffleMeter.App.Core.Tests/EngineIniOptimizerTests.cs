using WaffleMeter.App.Core;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

/// <summary>
/// 게임 최적화(Engine.ini) 순수 로직 고정: VRAM→티어 매핑, 블록 생성, 그리고 무엇보다 <b>우리 블록만</b>
/// 안전하게 넣고/교체하고/빼는 것 — 사용자의 나머지 ini는 절대 건드리지 않아야 되돌리기가 "100% 복원"이 된다.
/// </summary>
public sealed class EngineIniOptimizerTests
{
    private const long GiB = 1024L * 1024 * 1024;

    [Theory]
    [InlineData(24, 1)]
    [InlineData(16, 1)]
    [InlineData(12, 2)]
    [InlineData(10, 2)]
    [InlineData(8, 3)]
    [InlineData(6, 4)]
    [InlineData(4, 5)]
    [InlineData(2, 5)]
    [InlineData(0, 5)] // 감지 실패 → 가장 보수적
    public void TierForVram_maps_by_dedicated_vram(int gib, int expectedLevel) =>
        Assert.Equal(expectedLevel, EngineIniOptimizer.TierForVram(gib * GiB).Level);

    [Fact]
    public void Slightly_under_nominal_vram_still_lands_on_the_right_tier()
    {
        // GPU가 공칭보다 조금 적게 보고해도(8GB 카드가 7.9GiB 등) 티어가 밀리지 않아야 한다.
        Assert.Equal(3, EngineIniOptimizer.TierForVram((long)(7.9 * GiB)).Level);
        Assert.Equal(4, EngineIniOptimizer.TierForVram((long)(5.9 * GiB)).Level);
        Assert.Equal(1, EngineIniOptimizer.TierForVram((long)(15.5 * GiB)).Level);
    }

    [Fact]
    public void BuildBlock_includes_the_core_keys_and_is_marker_wrapped()
    {
        string block = EngineIniOptimizer.BuildBlock(EngineIniOptimizer.TierForVram(16 * GiB));
        Assert.StartsWith(EngineIniOptimizer.StartMarker, block);
        Assert.Contains(EngineIniOptimizer.EndMarker, block);
        Assert.Contains("[SystemSettings]", block);
        Assert.Contains("r.Streaming.PoolSize=10240", block);          // tier1 pool
        Assert.Contains("r.Streaming.MaxTempMemoryAllowed=2048", block); // tier1 temp
        Assert.Contains("gc.TimeBetweenPurgingPendingKillObjects=120", block);
        Assert.Contains("r.TextureStreaming=1", block);
    }

    [Fact]
    public void HLODStrategy_is_only_for_the_two_high_vram_tiers()
    {
        Assert.Contains("r.Streaming.HLODStrategy=2", EngineIniOptimizer.BuildBlock(EngineIniOptimizer.TierForVram(16 * GiB)));
        Assert.Contains("r.Streaming.HLODStrategy=2", EngineIniOptimizer.BuildBlock(EngineIniOptimizer.TierForVram(12 * GiB)));
        Assert.DoesNotContain("HLODStrategy", EngineIniOptimizer.BuildBlock(EngineIniOptimizer.TierForVram(8 * GiB)));
        Assert.DoesNotContain("HLODStrategy", EngineIniOptimizer.BuildBlock(EngineIniOptimizer.TierForVram(4 * GiB)));
    }

    [Fact]
    public void Advanced_lines_only_appear_when_opted_in()
    {
        var tier = EngineIniOptimizer.TierForVram(8 * GiB);
        Assert.DoesNotContain("s.AsyncLoadingThreadEnabled", EngineIniOptimizer.BuildBlock(tier, includeAdvanced: false));
        Assert.Contains("s.AsyncLoadingThreadEnabled=1", EngineIniOptimizer.BuildBlock(tier, includeAdvanced: true));
        Assert.Contains("r.Streaming.Boost=1", EngineIniOptimizer.BuildBlock(tier, includeAdvanced: true));
    }

    [Fact]
    public void Apply_onto_an_empty_or_missing_file_writes_just_the_block()
    {
        string block = EngineIniOptimizer.BuildBlock(EngineIniOptimizer.TierForVram(8 * GiB));
        Assert.Equal(block, EngineIniOptimizer.ApplyBlock(null, block));
        Assert.Equal(block, EngineIniOptimizer.ApplyBlock(string.Empty, block));
        Assert.True(EngineIniOptimizer.HasBlock(EngineIniOptimizer.ApplyBlock(null, block)));
    }

    [Fact]
    public void Apply_preserves_the_users_existing_content_above_the_block()
    {
        const string user = "[Core.System]\nPaths=../../../Content\n\n[/Script/Engine.RendererSettings]\nr.Custom=7\n";
        string block = EngineIniOptimizer.BuildBlock(EngineIniOptimizer.TierForVram(6 * GiB));

        string applied = EngineIniOptimizer.ApplyBlock(user, block);

        Assert.Contains("Paths=../../../Content", applied);
        Assert.Contains("r.Custom=7", applied);
        Assert.Contains(EngineIniOptimizer.StartMarker, applied);
        // 사용자 내용이 우리 블록보다 앞에 온다
        Assert.True(applied.IndexOf("r.Custom=7", System.StringComparison.Ordinal)
                    < applied.IndexOf(EngineIniOptimizer.StartMarker, System.StringComparison.Ordinal));
    }

    [Fact]
    public void Reapplying_replaces_the_old_block_rather_than_stacking_duplicates()
    {
        const string user = "[Core.System]\nPaths=x\n";
        string first = EngineIniOptimizer.ApplyBlock(user, EngineIniOptimizer.BuildBlock(EngineIniOptimizer.TierForVram(4 * GiB)));
        string second = EngineIniOptimizer.ApplyBlock(first, EngineIniOptimizer.BuildBlock(EngineIniOptimizer.TierForVram(16 * GiB)));

        // 마커는 정확히 한 번씩만
        Assert.Equal(1, CountOccurrences(second, EngineIniOptimizer.StartMarker));
        Assert.Equal(1, CountOccurrences(second, EngineIniOptimizer.EndMarker));
        // 새 티어 값이 남고 옛 티어 값은 사라진다
        Assert.Contains("r.Streaming.PoolSize=10240", second);
        Assert.DoesNotContain("r.Streaming.PoolSize=2048", second);
        Assert.Contains("Paths=x", second); // 사용자 내용 보존
    }

    [Fact]
    public void Revert_restores_the_file_to_exactly_its_pre_apply_content()
    {
        const string user = "[Core.System]\nPaths=../../../Content\nGc=1\n";
        string block = EngineIniOptimizer.BuildBlock(EngineIniOptimizer.TierForVram(12 * GiB));

        string applied = EngineIniOptimizer.ApplyBlock(user, block);
        Assert.True(EngineIniOptimizer.HasBlock(applied));

        string reverted = EngineIniOptimizer.StripBlock(applied);
        Assert.False(EngineIniOptimizer.HasBlock(reverted));
        Assert.Equal(user, reverted); // 100% 복원(줄바꿈 포함)
    }

    [Fact]
    public void Strip_is_a_no_op_when_our_block_is_absent()
    {
        const string user = "[Core.System]\nPaths=x\n";
        Assert.Equal(user, EngineIniOptimizer.StripBlock(user));
        Assert.Equal(string.Empty, EngineIniOptimizer.StripBlock(null));
    }

    [Fact]
    public void Strip_removes_even_duplicated_blocks_a_user_may_have_pasted_twice()
    {
        string block = EngineIniOptimizer.BuildBlock(EngineIniOptimizer.TierForVram(8 * GiB));
        string doubled = "[Core.System]\nPaths=x\n\n" + block + "\n" + block;
        string stripped = EngineIniOptimizer.StripBlock(doubled);
        Assert.False(EngineIniOptimizer.HasBlock(stripped));
        Assert.Contains("Paths=x", stripped);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0, i = 0;
        while ((i = haystack.IndexOf(needle, i, System.StringComparison.Ordinal)) >= 0)
        {
            count++;
            i += needle.Length;
        }

        return count;
    }
}

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

    // ── 게임이 Engine.ini를 다시 쓰면서 우리 마커(주석)를 지운 뒤의 파일 모양 ──────────────────────────
    // 실측(2026-07-28): 적용 후 게임을 한 번 켜면 마커 2줄은 사라지고 우리 키만 [SystemSettings]에 병합돼
    // 남는다. 이 형태를 못 알아보면 UI가 "미적용"으로 뜨고, 되돌리기가 무동작이며, 재적용이 키를 중복시킨다.
    private const string GameRewritten =
        "[Core.System]\nPaths=../../../Content\n\n" +
        "[SystemSettings]\n" +
        "r.TextureStreaming=1\nr.Streaming.PoolSize=10240\nr.Streaming.LimitPoolSizeToVRAM=1\n" +
        "r.Streaming.MaxTempMemoryAllowed=2048\nr.Streaming.FullyLoadUsedTextures=0\nr.Streaming.HLODStrategy=2\n" +
        "r.OneFrameThreadLag=1\nr.FinishCurrentFrame=0\nr.RHICmdBypass=0\nr.RenderThread.Enable=1\n" +
        "r.HZBOcclusion=1\nr.AllowOcclusionQueries=1\ngc.TimeBetweenPurgingPendingKillObjects=120\n" +
        "s.ForceGCAfterLevelStreamedOut=0\n\n" +
        "[WindowsApplication.Accessibility]\nStickyKeysHotkey=True\n";

    [Fact]
    public void Every_key_BuildBlock_writes_is_listed_in_ManagedKeys()
    {
        // 두 목록이 어긋나면 되돌리기가 그 키를 남긴다 — 주석으로만 적어둔 규칙은 회귀한다.
        string block = EngineIniOptimizer.BuildBlock(EngineIniOptimizer.TierForVram(24 * GiB), includeAdvanced: true);
        foreach (string line in block.Split('\n'))
        {
            string t = line.Trim();
            if (t.Length == 0 || t[0] == ';' || t[0] == '[')
            {
                continue;
            }

            string key = t[..t.IndexOf('=')];
            Assert.Contains(key, EngineIniOptimizer.ManagedKeys);
        }
    }

    [Fact]
    public void Applied_state_is_still_detected_after_the_game_strips_our_markers()
    {
        Assert.False(EngineIniOptimizer.HasBlock(GameRewritten)); // 마커는 실제로 사라졌다
        Assert.True(EngineIniOptimizer.IsApplied(GameRewritten)); // 그래도 "적용됨"으로 읽어야 한다
        Assert.Equal(14, EngineIniOptimizer.ManagedKeyCount(GameRewritten));
    }

    [Fact]
    public void A_clean_user_file_is_not_mistaken_for_applied()
    {
        const string clean = "[Core.System]\nPaths=x\n\n[SystemSettings]\nr.TextureStreaming=1\nr.HZBOcclusion=1\n";
        Assert.False(EngineIniOptimizer.IsApplied(clean)); // 우리 키 2개뿐 = 사용자 설정
        Assert.False(EngineIniOptimizer.IsApplied("[Core.System]\nPaths=x\n"));
        Assert.False(EngineIniOptimizer.IsApplied(null));
    }

    [Fact]
    public void Revert_works_even_after_the_game_stripped_our_markers()
    {
        string reverted = EngineIniOptimizer.Remove(GameRewritten);

        Assert.False(EngineIniOptimizer.IsApplied(reverted));
        Assert.Equal(0, EngineIniOptimizer.ManagedKeyCount(reverted));
        // 우리 키만 있던 섹션은 빈 헤더를 남기지 않고 통째로 사라진다
        Assert.DoesNotContain("[SystemSettings]", reverted);
        // 사용자의 다른 섹션·키는 그대로
        Assert.Contains("Paths=../../../Content", reverted);
        Assert.Contains("[WindowsApplication.Accessibility]", reverted);
        Assert.Contains("StickyKeysHotkey=True", reverted);
    }

    [Fact]
    public void Reapplying_after_a_game_rewrite_does_not_stack_duplicate_keys()
    {
        string block = EngineIniOptimizer.BuildBlock(EngineIniOptimizer.TierForVram(16 * GiB));
        string reapplied = EngineIniOptimizer.ApplyBlock(GameRewritten, block);

        Assert.Equal(1, CountOccurrences(reapplied, "[SystemSettings]"));
        Assert.Equal(1, CountOccurrences(reapplied, "r.Streaming.PoolSize="));
        Assert.Equal(1, CountOccurrences(reapplied, "r.TextureStreaming="));
        Assert.Contains("Paths=../../../Content", reapplied);
    }

    [Fact]
    public void A_user_key_in_SystemSettings_survives_the_revert()
    {
        // 우리 키 옆에 사용자가 직접 넣은 키가 있으면 섹션 헤더와 그 키는 남아야 한다.
        string mixed = GameRewritten.Replace(
            "s.ForceGCAfterLevelStreamedOut=0\n", "s.ForceGCAfterLevelStreamedOut=0\nr.MyOwnSetting=42\n");
        string reverted = EngineIniOptimizer.Remove(mixed);

        Assert.Contains("[SystemSettings]", reverted);
        Assert.Contains("r.MyOwnSetting=42", reverted);
        Assert.Equal(0, EngineIniOptimizer.ManagedKeyCount(reverted));
    }

    [Fact]
    public void Managed_key_matching_ignores_case_and_unreal_prefixes()
    {
        const string odd = "[systemsettings]\n+R.TEXTURESTREAMING=1\nr.streaming.PoolSize = 10240\n";
        Assert.Equal(2, EngineIniOptimizer.ManagedKeyCount(odd));
        Assert.Equal(0, EngineIniOptimizer.ManagedKeyCount(EngineIniOptimizer.Remove(odd)));
    }

    [Fact]
    public void Applying_onto_a_file_that_does_not_end_in_a_newline_still_starts_the_block_on_its_own_line()
    {
        // 실사용 Engine.ini는 손으로 편집되면 개행 없이 끝날 수 있다. 그때 마커가 마지막 줄에 들러붙으면
        // 언리얼이 그 줄을 통째로 주석으로 먹어 사용자 설정 한 줄이 사라진다.
        const string user = "[Core.System]\nPaths=x"; // 끝 개행 없음
        string applied = EngineIniOptimizer.ApplyBlock(user, EngineIniOptimizer.BuildBlock(EngineIniOptimizer.TierForVram(8 * GiB)));

        Assert.Contains("Paths=x\n", applied);                        // 사용자 마지막 줄이 온전하다
        Assert.Contains("\n" + EngineIniOptimizer.StartMarker, applied); // 마커가 줄 맨 앞에서 시작
        Assert.DoesNotContain("Paths=x" + EngineIniOptimizer.StartMarker, applied);
        Assert.EndsWith("\n", applied);                               // 파일도 개행으로 끝난다
    }

    [Fact]
    public void Revert_keeps_a_trailing_blank_line_the_user_already_had()
    {
        // 종전 구현은 줄 분해·재조립 과정에서 파일 끝 빈 줄을 함께 삼켜(실측 5273→5271바이트)
        // "원본 100% 복원"이 실제로는 어긋났다.
        const string user = "[Core.System]\nPaths=x\n\n"; // 끝에 빈 줄 하나
        string applied = EngineIniOptimizer.ApplyBlock(user, EngineIniOptimizer.BuildBlock(EngineIniOptimizer.TierForVram(8 * GiB)));

        Assert.Equal(user, EngineIniOptimizer.Remove(applied));
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

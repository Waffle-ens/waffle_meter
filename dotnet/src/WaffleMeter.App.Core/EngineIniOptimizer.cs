using System.Text;

namespace WaffleMeter.App.Core;

/// <summary>
/// AION2(언리얼 엔진) <c>Engine.ini</c> 렉 감소 최적화를 생성/적용/제거하는 <b>순수</b> 로직 — 파일 I/O·GPU
/// 감지는 여기 없다(호출부 담당). VRAM→티어 매핑, ini 블록 문자열 생성, 우리 블록만 골라 삽입/교체/제거.
///
/// <para>기준: 커뮤니티 "화질 무영향" 세트(텍스처 스트리밍 풀·스레드 lag·occlusion·GC 튜닝, 전부
/// <c>[SystemSettings]</c>). 사용자가 그래픽카드를 직접 고르는 대신 우리는 전용 VRAM을 자동 감지해 5티어로
/// 매핑한다. 우리가 넣는 줄은 <see cref="StartMarker"/>~<see cref="EndMarker"/> 마커로 감싸 두어, 재적용은
/// 옛 블록을 교체하고 되돌리기는 그 구간만 들어내 <b>사용자 파일의 나머지는 절대 건드리지 않는다</b>.</para>
///
/// <para>줄바꿈은 내부적으로 <c>\n</c>으로 정규화한다(호출부가 파일에 쓸 때 Windows면 CRLF로 바꾼다).</para>
/// </summary>
public static class EngineIniOptimizer
{
    /// <summary>우리 블록의 시작 경계. 사용자가 지우지 않도록 안내 문구 포함. 정확 매칭에 쓰이니 바꾸면
    /// 이미 적용된 블록을 못 찾는다(마이그레이션 필요).</summary>
    public const string StartMarker =
        "; ===== waffle_meter 최적화 시작 — 아래 END 줄까지 자동 관리됨 (직접 수정/삭제하지 마세요) =====";

    public const string EndMarker = "; ===== waffle_meter 최적화 끝 =====";

    /// <summary>VRAM 티어. <see cref="PoolMiB"/>/<see cref="TempMiB"/>=MiB, <see cref="GcSeconds"/>=초,
    /// <see cref="Hlod"/>=<c>r.Streaming.HLODStrategy=2</c> 포함 여부(상위 티어만).</summary>
    public readonly record struct Tier(int Level, string Label, int PoolMiB, int TempMiB, bool Hlod, int GcSeconds);

    private const long GiB = 1024L * 1024 * 1024;

    /// <summary>전용 VRAM(바이트) → 5티어. GPU 보고 값이 공칭보다 살짝 작을 수 있어 경계에 여유를 뒀다
    /// (8GB 카드=tier3, 6GB=tier4, 4GB↓=tier5). 0/음수(감지 실패)는 가장 보수적인 tier5.</summary>
    public static Tier TierForVram(long dedicatedVramBytes)
    {
        double g = dedicatedVramBytes / (double)GiB;
        if (g >= 14) return new Tier(1, "16GB 이상", 10240, 2048, true, 120);
        if (g >= 9.5) return new Tier(2, "10~12GB", 7168, 1536, true, 120);
        if (g >= 7) return new Tier(3, "8GB", 4608, 1024, false, 90);
        if (g >= 5) return new Tier(4, "6GB", 3072, 512, false, 90);
        return new Tier(5, "4GB 이하", 2048, 512, false, 60);
    }

    /// <summary>우리 최적화 블록(마커 포함, <c>\n</c> 개행). <paramref name="includeAdvanced"/>면 저위험
    /// anti-hitch 몇 줄을 덧붙인다(로딩 히칭 완화, 기본 off — 사용자 opt-in).</summary>
    public static string BuildBlock(Tier tier, bool includeAdvanced = false)
    {
        var sb = new StringBuilder();
        sb.Append(StartMarker).Append('\n');
        sb.Append("; 아이온2 렉 감소 · waffle_meter 자동 적용 (VRAM ").Append(tier.Label)
          .Append(" 기준) · 인게임 그래픽 옵션과 화질에는 영향 없음").Append('\n');
        sb.Append("[SystemSettings]").Append('\n');
        sb.Append("r.TextureStreaming=1").Append('\n');
        sb.Append("r.Streaming.PoolSize=").Append(tier.PoolMiB).Append('\n');
        sb.Append("r.Streaming.LimitPoolSizeToVRAM=1").Append('\n');
        sb.Append("r.Streaming.MaxTempMemoryAllowed=").Append(tier.TempMiB).Append('\n');
        sb.Append("r.Streaming.FullyLoadUsedTextures=0").Append('\n');
        if (tier.Hlod)
        {
            sb.Append("r.Streaming.HLODStrategy=2").Append('\n');
        }

        sb.Append("r.OneFrameThreadLag=1").Append('\n');
        sb.Append("r.FinishCurrentFrame=0").Append('\n');
        sb.Append("r.RHICmdBypass=0").Append('\n');
        sb.Append("r.RenderThread.Enable=1").Append('\n');
        sb.Append("r.HZBOcclusion=1").Append('\n');
        sb.Append("r.AllowOcclusionQueries=1").Append('\n');
        sb.Append("gc.TimeBetweenPurgingPendingKillObjects=").Append(tier.GcSeconds).Append('\n');
        sb.Append("s.ForceGCAfterLevelStreamedOut=0").Append('\n');
        if (includeAdvanced)
        {
            sb.Append("; --- 고급(선택): 로딩/구역이동 히칭 완화 ---").Append('\n');
            sb.Append("s.AsyncLoadingThreadEnabled=1").Append('\n');
            sb.Append("r.Streaming.Boost=1").Append('\n');
        }

        sb.Append(EndMarker).Append('\n');
        return sb.ToString();
    }

    /// <summary>우리 블록이 이미 들어 있는가(적용됨 여부).</summary>
    public static bool HasBlock(string? iniContent) =>
        iniContent is not null && iniContent.Contains(StartMarker, StringComparison.Ordinal);

    /// <summary>우리 블록(START~END, 바로 앞 빈 줄 포함)을 통째로 들어낸다. 마커가 없으면 원문 그대로.
    /// 되돌리기 = 이 결과를 파일에 쓰기. 중복/중첩 블록도 방어적으로 모두 제거한다.</summary>
    public static string StripBlock(string? iniContent)
    {
        if (string.IsNullOrEmpty(iniContent) || !iniContent.Contains(StartMarker, StringComparison.Ordinal))
        {
            return iniContent ?? string.Empty;
        }

        string[] lines = iniContent.Replace("\r\n", "\n").Split('\n');
        var kept = new List<string>(lines.Length);
        bool inBlock = false;
        foreach (string line in lines)
        {
            if (!inBlock && line.TrimEnd() == StartMarker)
            {
                inBlock = true;
                // 우리가 사용자 내용과 블록 사이에 넣었던 빈 줄들을 함께 정리
                while (kept.Count > 0 && kept[^1].Trim().Length == 0)
                {
                    kept.RemoveAt(kept.Count - 1);
                }

                continue;
            }

            if (inBlock)
            {
                if (line.TrimEnd() == EndMarker)
                {
                    inBlock = false;
                }

                continue;
            }

            kept.Add(line);
        }

        string result = string.Join('\n', kept).TrimEnd('\n');
        return result.Length == 0 ? string.Empty : result + "\n";
    }

    /// <summary>기존 우리 블록을 걷어낸 뒤 새 블록을 파일 끝에 붙인다(적용/재적용 공통 진입점).
    /// <paramref name="iniContent"/>는 파일 원문(파일이 없으면 빈 문자열/ null).</summary>
    public static string ApplyBlock(string? iniContent, string block)
    {
        string basePart = StripBlock(iniContent);
        var sb = new StringBuilder();
        if (basePart.Length > 0)
        {
            sb.Append(basePart);
            if (!basePart.EndsWith('\n'))
            {
                sb.Append('\n');
            }

            sb.Append('\n'); // 사용자 내용과 우리 블록 사이 한 줄 띄움
        }

        sb.Append(block);
        return sb.ToString();
    }
}

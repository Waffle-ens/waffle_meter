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
/// <para>⚠️ 마커는 주석이라 <b>영구적이지 않다</b> — 게임이 Engine.ini를 다시 쓰면 주석이 날아가고 우리 키만
/// <c>[SystemSettings]</c>에 병합돼 남는다(실측). 그래서 감지·제거는 마커에만 기대면 안 되고
/// <see cref="ManagedKeys"/> 폴백을 함께 본다 — <see cref="IsApplied"/>·<see cref="Remove"/>가 그 진입점이다.</para>
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

    /// <summary>우리가 <c>[SystemSettings]</c>에 쓰는 키 이름 전체(고급 옵션 포함, 비교는 대소문자 무시).
    /// <para>🔑 왜 필요한가: 마커는 <c>;</c> 주석이라 <b>게임이 Engine.ini를 다시 쓰면 사라진다</b> — 언리얼의
    /// 설정 직렬화가 주석을 보존하지 않는다. 실측(2026-07-28): 적용 뒤 게임을 한 번 실행하자 우리 키 14줄만
    /// <c>[SystemSettings]</c>에 남고 마커 2줄은 소멸했다. 마커만 보고 판정하면 그 뒤로 ①UI가 "미적용"으로
    /// 표시하고 ②되돌리기가 아무 일도 못 하며 ③다시 적용하면 같은 키가 한 벌 더 쌓인다. 그래서 마커가 없을
    /// 때는 이 키 목록이 감지·제거의 근거가 된다.</para>
    /// <para>⚠️ <see cref="BuildBlock"/>에 키를 추가하면 여기에도 반드시 추가한다 — 빠지면 그 키는 되돌리기
    /// 때 남는다. 테스트가 두 목록의 일치를 강제한다.</para></summary>
    public static readonly string[] ManagedKeys =
    [
        "r.TextureStreaming",
        "r.Streaming.PoolSize",
        "r.Streaming.LimitPoolSizeToVRAM",
        "r.Streaming.MaxTempMemoryAllowed",
        "r.Streaming.FullyLoadUsedTextures",
        "r.Streaming.HLODStrategy",
        "r.OneFrameThreadLag",
        "r.FinishCurrentFrame",
        "r.RHICmdBypass",
        "r.RenderThread.Enable",
        "r.HZBOcclusion",
        "r.AllowOcclusionQueries",
        "gc.TimeBetweenPurgingPendingKillObjects",
        "s.ForceGCAfterLevelStreamedOut",
        "s.AsyncLoadingThreadEnabled", // 고급
        "r.Streaming.Boost",           // 고급
    ];

    /// <summary>마커 없이 키만 남은 파일을 "적용됨"으로 볼 최소 키 수. 우리 블록은 항상 13개 이상을 쓴다.
    /// 이 수를 우연히 넘는 사용자 파일은 사실상 우리 프리셋과 같은 내용이므로, 그때 "적용됨"으로 보이고
    /// 되돌리기가 그 줄들을 걷어내는 것이 오히려 사용자가 기대하는 동작이다(백업도 함께 남긴다).</summary>
    private const int MinManagedKeysForApplied = 10;

    /// <summary>우리 블록이 <b>마커째로</b> 들어 있는가. 적용 여부 판정에는 <see cref="IsApplied"/>를 쓸 것 —
    /// 게임이 파일을 다시 쓰면 마커가 사라지므로 이 값만으로는 "미적용"이라는 오답이 나온다.</summary>
    public static bool HasBlock(string? iniContent) =>
        iniContent is not null && iniContent.Contains(StartMarker, StringComparison.Ordinal);

    /// <summary><c>[SystemSettings]</c> 안에 들어 있는 우리 관리 키의 개수(중복 제외).</summary>
    public static int ManagedKeyCount(string? iniContent)
    {
        if (string.IsNullOrEmpty(iniContent))
        {
            return 0;
        }

        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool inSystemSettings = false;
        foreach (string line in iniContent.Replace("\r\n", "\n").Split('\n'))
        {
            if (IsSectionHeader(line))
            {
                inSystemSettings = IsSystemSettingsHeader(line);
                continue;
            }

            if (inSystemSettings && ManagedKeyOf(line) is { } key)
            {
                found.Add(key);
            }
        }

        return found.Count;
    }

    /// <summary>최적화가 적용된 상태인가. 마커가 살아 있으면 그걸로, 게임이 마커를 지웠으면 남아 있는
    /// 관리 키 수로 판정한다.</summary>
    public static bool IsApplied(string? iniContent) =>
        HasBlock(iniContent) || ManagedKeyCount(iniContent) >= MinManagedKeysForApplied;

    /// <summary>우리 블록(START~END, 우리가 넣은 구분 빈 줄 1개 포함)을 통째로 들어낸다. 마커가 없으면 원문
    /// 그대로. 중복/중첩 블록도 방어적으로 모두 제거한다.
    /// <para>구간을 문자열에서 그대로 잘라내므로 <b>나머지는 바이트 단위로 보존</b>된다 — 종전의 줄 분해·재조립
    /// 방식은 파일 끝 빈 줄을 함께 삼켜 "원본 100% 복원"이 실제로는 어긋났다(실측 5273→5271바이트).</para></summary>
    public static string StripBlock(string? iniContent)
    {
        if (string.IsNullOrEmpty(iniContent))
        {
            return string.Empty;
        }

        string content = iniContent.Replace("\r\n", "\n");
        while (true)
        {
            int start = IndexOfMarkerLine(content, StartMarker);
            if (start < 0)
            {
                break;
            }

            int endMarker = IndexOfMarkerLine(content, EndMarker, start + StartMarker.Length);
            int end;
            if (endMarker < 0)
            {
                end = content.Length; // 끝 마커가 유실된 블록: 파일 끝까지가 우리 구간
            }
            else
            {
                int nl = content.IndexOf('\n', endMarker);
                end = nl < 0 ? content.Length : nl + 1;
            }

            // 우리가 사용자 내용과 블록 사이에 넣은 빈 줄은 정확히 1개다. 딱 그만큼만 걷어내고 사용자가
            // 원래 갖고 있던 빈 줄은 남긴다.
            if (start >= 2 && content[start - 1] == '\n' && content[start - 2] == '\n')
            {
                start--;
            }

            content = content.Remove(start, end - start);
        }

        return content;
    }

    /// <summary>마커가 사라진 파일용 폴백: <c>[SystemSettings]</c>에서 우리 관리 키 줄만 지운다. 그 섹션에
    /// 우리 키밖에 없었으면 빈 헤더가 남지 않도록 헤더까지 함께 걷어낸다. 사용자의 다른 섹션·다른 키는
    /// 건드리지 않는다.
    /// <para>⚠️ 사용자가 손으로 넣은 같은 키와는 구분할 수 없다(와이어에 출처가 없다). 그래서 호출부는 쓰기
    /// 전에 반드시 백업을 남긴다.</para></summary>
    public static string StripManagedKeys(string? iniContent)
    {
        if (string.IsNullOrEmpty(iniContent))
        {
            return string.Empty;
        }

        string[] lines = iniContent.Replace("\r\n", "\n").Split('\n');
        var drop = new bool[lines.Length];
        for (int i = 0; i < lines.Length; i++)
        {
            if (!IsSystemSettingsHeader(lines[i]))
            {
                continue;
            }

            int end = i + 1;
            while (end < lines.Length && !IsSectionHeader(lines[end]))
            {
                end++;
            }

            bool anyUserLineKept = false;
            for (int j = i + 1; j < end; j++)
            {
                if (ManagedKeyOf(lines[j]) is not null)
                {
                    drop[j] = true;
                }
                else if (lines[j].Trim().Length > 0)
                {
                    anyUserLineKept = true;
                }
            }

            if (!anyUserLineKept)
            {
                drop[i] = true; // 우리 키뿐이던 섹션 — 빈 헤더를 남기지 않는다
                for (int j = i + 1; j < end; j++)
                {
                    // 파일 끝 개행이 만든 마지막 빈 원소는 남긴다(지우면 파일이 개행 없이 끝난다)
                    if (j == lines.Length - 1 && lines[j].Length == 0)
                    {
                        continue;
                    }

                    drop[j] = true;
                }
            }

            i = end - 1;
        }

        var sb = new StringBuilder();
        bool first = true;
        for (int i = 0; i < lines.Length; i++)
        {
            if (drop[i])
            {
                continue;
            }

            if (!first)
            {
                sb.Append('\n');
            }

            sb.Append(lines[i]);
            first = false;
        }

        return sb.ToString();
    }

    /// <summary>어떤 형태로 남아 있든(마커 블록 / 게임이 병합해 버린 키) 우리 최적화를 모두 걷어낸다.
    /// 되돌리기와 재적용이 공통으로 쓰는 진입점.</summary>
    public static string Remove(string? iniContent) => StripManagedKeys(StripBlock(iniContent));

    private static bool IsSectionHeader(string line)
    {
        string t = line.Trim();
        return t.Length >= 2 && t[0] == '[' && t[^1] == ']';
    }

    private static bool IsSystemSettingsHeader(string line) =>
        line.Trim().Equals("[SystemSettings]", StringComparison.OrdinalIgnoreCase);

    /// <summary>이 줄이 우리 관리 키의 할당이면 그 정규 키 이름, 아니면 null. 언리얼 ini의 접두사
    /// (<c>+ - . !</c>)와 키 이름 뒤 공백을 허용하고, 키 비교는 대소문자를 무시한다.</summary>
    private static string? ManagedKeyOf(string line)
    {
        string t = line.Trim();
        if (t.Length == 0 || t[0] == ';' || t[0] == '[')
        {
            return null;
        }

        if (t[0] is '+' or '-' or '.' or '!')
        {
            t = t[1..];
        }

        int eq = t.IndexOf('=');
        if (eq <= 0)
        {
            return null;
        }

        string key = t[..eq].TrimEnd();
        foreach (string managed in ManagedKeys)
        {
            if (string.Equals(managed, key, StringComparison.OrdinalIgnoreCase))
            {
                return managed;
            }
        }

        return null;
    }

    /// <summary>줄 맨 앞에서 시작하고 그 뒤가 공백뿐인 <paramref name="marker"/>의 위치. 마커 문자열이 값의
    /// 일부로 들어간 경우를 배제한다.</summary>
    private static int IndexOfMarkerLine(string content, string marker, int from = 0)
    {
        int i = from;
        while (i <= content.Length - marker.Length)
        {
            int idx = content.IndexOf(marker, i, StringComparison.Ordinal);
            if (idx < 0)
            {
                return -1;
            }

            int lineEnd = content.IndexOf('\n', idx);
            if (lineEnd < 0)
            {
                lineEnd = content.Length;
            }

            bool atLineStart = idx == 0 || content[idx - 1] == '\n';
            bool restIsBlank = content.AsSpan(idx + marker.Length, lineEnd - idx - marker.Length).IsWhiteSpace();
            if (atLineStart && restIsBlank)
            {
                return idx;
            }

            i = idx + marker.Length;
        }

        return -1;
    }

    /// <summary>남아 있는 우리 최적화를 (마커 블록이든 게임이 병합한 키든) 모두 걷어낸 뒤 새 블록을 파일 끝에
    /// 붙인다(적용/재적용 공통 진입점). <paramref name="iniContent"/>는 파일 원문(파일이 없으면 빈 문자열/null).</summary>
    public static string ApplyBlock(string? iniContent, string block)
    {
        string basePart = Remove(iniContent);
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

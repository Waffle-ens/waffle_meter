using System.Buffers.Binary;
using System.Text.RegularExpressions;
using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

/// <summary>
/// 인게임 버프 아이콘(<c>Assets/BuffIcons</c>)도 스킬 아이콘처럼 <b>두 곳이 맞아야</b> 뜬다:
/// <c>BuffIconManifest.ByAbnormal</c> 의 값(표시 base)이 있어야 <c>JoinIcons.BuffSlot</c> 이 BuffIcons pack URI 를
/// 만들고, 그 PNG 가 실려 있어야 그림이 나온다. 매니페스트에만 있으면 버프 오버레이 슬롯과 picker 행이 빈칸이
/// 되고(picker 는 아이콘이 null 이면 행을 통째로 뺀다), PNG 만 있으면 아무도 안 찾는 파일이 실린다.
///
/// <para>매니페스트는 App.Wpf 의 <c>internal</c> 이라 <see cref="ShippedSkillIconAssetsTests"/> 와 같은 방식으로
/// 소스 텍스트에서 읽는다.</para>
/// </summary>
public sealed class ShippedBuffIconAssetsTests
{
    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "dotnet", "Assets", "BuffIcons")))
        {
            dir = dir.Parent;
        }

        return dir ?? throw new InvalidOperationException("dotnet/Assets/BuffIcons 를 찾지 못했습니다.");
    }

    private static string RepoPath(params string[] parts) => Path.Combine(RepoRoot().FullName, Path.Combine(parts));

    private static string IconsDir() => RepoPath("dotnet", "Assets", "BuffIcons");

    /// <summary>초기화자부터 파일 끝까지. 앵커 <c>ByAbnormal = new()</c> 는 ShippedVoicePackTests 도 쓴다.</summary>
    private static string ManifestBody()
    {
        string src = File.ReadAllText(RepoPath("dotnet", "src", "WaffleMeter.App.Wpf", "BuffIconManifest.cs"));
        int at = src.IndexOf("ByAbnormal = new()", StringComparison.Ordinal);
        Assert.True(at >= 0, "BuffIconManifest 에서 'ByAbnormal = new()' 앵커를 찾지 못했습니다.");
        return src[at..];
    }

    private static string StripComments(string body) => Regex.Replace(body, @"//[^\n]*", string.Empty);

    /// <summary>런타임 어노멀 코드 → 표시 base. 주석을 지운 본문에서 읽는다.</summary>
    private static Dictionary<int, int> Manifest()
    {
        var map = new Dictionary<int, int>();
        foreach (Match m in Regex.Matches(StripComments(ManifestBody()), @"\[(\d{7,9})\]\s*=\s*(\d{7,9})"))
        {
            int key = int.Parse(m.Groups[1].Value);
            Assert.True(map.TryAdd(key, int.Parse(m.Groups[2].Value)), $"매니페스트에 키 {key} 가 두 번 있습니다.");
        }

        Assert.NotEmpty(map);
        return map;
    }

    private static HashSet<int> ShippedBases() =>
        Directory.GetFiles(IconsDir(), "*.png")
            .Select(f => int.Parse(Path.GetFileNameWithoutExtension(f)))
            .ToHashSet();

    [Fact]
    public void The_manifest_bases_and_the_shipped_pngs_are_the_same_set()
    {
        HashSet<int> manifest = Manifest().Values.ToHashSet();
        HashSet<int> shipped = ShippedBases();

        int[] blank = manifest.Except(shipped).Order().ToArray();
        int[] orphan = shipped.Except(manifest).Order().ToArray();

        Assert.True(blank.Length == 0,
            $"매니페스트에 있는데 PNG 가 없는 표시 base {blank.Length}건 — 버프 슬롯이 빈칸으로 뜹니다: "
            + string.Join(", ", blank));
        Assert.True(orphan.Length == 0,
            $"PNG 는 실렸는데 매니페스트에 없는 표시 base {orphan.Length}건 — 아무도 찾지 않는 파일입니다: "
            + string.Join(", ", orphan));
    }

    /// <summary>주석 안의 숫자가 매핑이나 코드로 읽히지 않는다 — 주석을 지우기 전후로 본문의 7~9자리 숫자가 같아야 한다.</summary>
    [Fact]
    public void No_number_in_the_manifest_comments_can_pass_for_a_code()
    {
        static HashSet<int> Numbers(string text) =>
            Regex.Matches(text, @"\b(\d{7,9})\b").Select(m => int.Parse(m.Groups[1].Value)).ToHashSet();

        string body = ManifestBody();
        int[] ghosts = Numbers(body).Except(Numbers(StripComments(body))).Order().ToArray();
        Assert.True(ghosts.Length == 0,
            $"주석 안의 숫자가 코드로 읽히고 있습니다 {ghosts.Length}건: {string.Join(", ", ghosts)}");
    }

    [Fact]
    public void Every_shipped_buff_icon_is_48_by_48()
    {
        string[] wrong = Directory.GetFiles(IconsDir(), "*.png")
            .Where(f => PngSize(f) != (48, 48))
            .Select(f => $"{Path.GetFileName(f)} {PngSize(f)}")
            .ToArray();

        Assert.True(wrong.Length == 0, $"48x48 이 아닌 버프 아이콘 {wrong.Length}개: {string.Join(", ", wrong)}");
    }

    /// <summary>값은 그 런타임 코드가 오버레이에서 접히는 표시 base 여야 한다. 어긋나면 그 버프 슬롯은 버프
    /// 아이콘이 아니라 스킬 아이콘으로 그려진다(조용한 실패). 심판의 권능은 표시 코드 분리(override)를 탄다.</summary>
    [Fact]
    public void Each_value_is_the_display_base_of_its_runtime_code()
    {
        string[] wrong = Manifest()
            .Where(kv => DataManager.BuffDisplayBase(kv.Key) != kv.Value)
            .Select(kv => $"{kv.Key} → {kv.Value} (실제 {DataManager.BuffDisplayBase(kv.Key)})")
            .ToArray();

        Assert.True(wrong.Length == 0, $"표시 base 와 다른 매핑 {wrong.Length}건: {string.Join(", ", wrong)}");
    }

    /// <summary>오버레이와 picker 는 카탈로그 행만 그린다 — 카탈로그 밖 base 의 버프 아이콘은 쓰일 곳이 없다.</summary>
    [Fact]
    public void Each_value_is_a_catalogued_buff()
    {
        HashSet<int> catalog = ReferenceJson
            .LoadBuffCatalog(RepoPath("dotnet", "Assets", "json", "buff_catalog.json")).Catalog
            .Select(x => x.Code)
            .ToHashSet();

        int[] stray = Manifest().Values.Distinct().Where(b => !catalog.Contains(b)).Order().ToArray();
        Assert.True(stray.Length == 0, $"카탈로그에 없는 표시 base {stray.Length}건: {string.Join(", ", stray)}");
    }

    /// <summary>매니페스트 키는 디버프가 아니라 버프여야 한다 — 디버프가 버프 아이콘을 받지 않게 하려고 키를
    /// 런타임 코드로 둔 것이니, 그 키 자체가 buff.json 의 Buff 행인지 확인한다.</summary>
    [Fact]
    public void Each_key_is_a_buff_row_in_buff_json()
    {
        using var doc = System.Text.Json.JsonDocument.Parse(
            File.ReadAllText(RepoPath("dotnet", "Assets", "json", "buff.json")));
        System.Text.Json.JsonElement root = doc.RootElement;

        string[] wrong = Manifest().Keys
            .Where(k => !root.TryGetProperty(k.ToString(System.Globalization.CultureInfo.InvariantCulture), out var row)
                        || !row.TryGetProperty("type", out var type)
                        || type.GetString() != "Buff")
            .Select(k => k.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();

        Assert.True(wrong.Length == 0, $"buff.json 의 Buff 행이 아닌 키 {wrong.Length}건: {string.Join(", ", wrong)}");
    }

    private static (int Width, int Height) PngSize(string path)
    {
        byte[] head = new byte[24];
        using FileStream fs = File.OpenRead(path);
        Assert.Equal(head.Length, fs.ReadAtLeast(head, head.Length, throwOnEndOfStream: false));
        return (BinaryPrimitives.ReadInt32BigEndian(head.AsSpan(16, 4)),
                BinaryPrimitives.ReadInt32BigEndian(head.AsSpan(20, 4)));
    }
}

using System.IO;
using System.Linq;
using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>
/// 출하 buff_catalog.json(picker·오버레이·음성 팩의 버프 목록)과 buff_names.json(이름표)의 정합.
/// <para>앱은 buff_names의 이름을 먼저 싣고, 카탈로그는 이름이 비어 있는 코드만 채운다(<c>LoadBuffCatalog</c>).
/// 그래서 카탈로그 이름만 고치면 picker·오버레이·TTS는 buff_names의 옛 이름을 그대로 쓰고, 음성 팩은 카탈로그
/// 이름으로 구워져 둘이 어긋난다(2026-11-11 이전의 '폭렬권[폭주]' ↔ '폭렬권'이 그 예).</para>
/// </summary>
public sealed class ShippedBuffCatalogTests
{
    private static string FindAssetsJsonDir()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "Assets", "json");
            if (File.Exists(Path.Combine(candidate, "buff_catalog.json")))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Assets/json/buff_catalog.json not found above " + AppContext.BaseDirectory);
    }

    private static (List<(int Code, string Name, string Job)> Catalog, List<int> DefaultOff) Catalog() =>
        ReferenceJson.LoadBuffCatalog(Path.Combine(FindAssetsJsonDir(), "buff_catalog.json"));

    private static Dictionary<int, string> Names() =>
        ReferenceJson.LoadBuffNames(Path.Combine(FindAssetsJsonDir(), "buff_names.json"))
            .ToDictionary(n => n.Code, n => n.Name);

    [Fact]
    public void Catalog_names_agree_with_buff_names()
    {
        Dictionary<int, string> names = Names();
        string[] disagree = Catalog().Catalog
            .Where(c => names.TryGetValue(c.Code, out string? n) && n != c.Name)
            .Select(c => $"{c.Code}: catalog '{c.Name}' vs buff_names '{names[c.Code]}'")
            .ToArray();
        Assert.Empty(disagree);
    }

    [Fact]
    public void The_shipped_default_off_set_is_the_two_remaining_toggle_auras()
    {
        // 폭풍의 주문(구 질주의 진언)은 2026-11-11부터 토글이 아니라 기본으로 켜진다.
        Assert.Equal(new[] { 17410000, 18190000 }, Catalog().DefaultOff.OrderBy(c => c));
    }

    [Theory]
    [InlineData(17400058, "대지의 축복")]
    [InlineData(12240052, "심판의 권능")]
    [InlineData(12110037, "보호의 방패")]
    [InlineData(13080037, "회피 자세")]
    public void Every_split_display_code_is_catalogued_with_its_in_game_name(int displayCode, string name)
    {
        Assert.Contains(Catalog().Catalog, c => c.Code == displayCode && c.Name == name);
        Assert.Equal(name, Names()[displayCode]);
    }
}

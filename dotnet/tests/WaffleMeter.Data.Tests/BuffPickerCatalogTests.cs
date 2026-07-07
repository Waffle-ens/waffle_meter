using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>The per-job buff picker catalog: curated (bundled) self-buffs are listed up front, unioned with
/// anything observed live; the default-off toggle set is exposed for the app's first-run apply.</summary>
public sealed class BuffPickerCatalogTests
{
    [Fact]
    public void Curated_catalog_is_listed_before_observation()
    {
        var dm = new DataManager();
        dm.LoadBuffCatalog(
            new (int, string, string)[]
            {
                (18250000, "질풍의 권능", "호법성"),
                (15400000, "원소 강화", "마도성"),
                (19070000, "질풍격", "권성"),
            },
            new[] { 18160000, 18190000 });

        var catalog = dm.BuffPickerCatalog();
        Assert.Contains(catalog, c => c.BaseCode == 18250000 && c.Job == "호법성" && c.Name == "질풍의 권능");
        Assert.Contains(catalog, c => c.BaseCode == 15400000 && c.Job == "마도성");
        Assert.Contains(catalog, c => c.BaseCode == 19070000 && c.Job == "권성");
        Assert.Equal(new[] { 18160000, 18190000 }.OrderBy(x => x), dm.DefaultOffBuffBases().OrderBy(x => x));
    }

    [Fact]
    public void Explicit_buff_names_win_over_catalog_names()
    {
        var dm = new DataManager();
        dm.LoadBuffNames(new (int, string, string)[] { (18250000, "질풍의 권능", "호법성") });
        dm.LoadBuffCatalog(new (int, string, string)[] { (18250000, "WRONG", "기타") }, System.Array.Empty<int>());

        Assert.Contains(dm.BuffPickerCatalog(), c => c.BaseCode == 18250000 && c.Name == "질풍의 권능" && c.Job == "호법성");
    }
}

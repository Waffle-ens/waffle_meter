using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>The per-job buff picker catalog: the curated (bundled) list IS the picker, and the overlay draws
/// nothing outside it; the default-off toggle set is exposed for the app's first-run apply.</summary>
public sealed class BuffPickerCatalogTests
{
    [Fact]
    public void An_observed_buff_outside_the_catalog_is_neither_listed_nor_drawn()
    {
        var dm = new DataManager();
        dm.LoadBuffCatalog(new (int, string, string)[] { (18250000, "질풍의 권능", "호법성") }, System.Array.Empty<int>());
        dm.SeedObservedBuffBases(new[] { 11810000 });

        Assert.DoesNotContain(dm.BuffPickerCatalog(), c => c.BaseCode == 11810000);
    }

    /// <summary>MeterServices only loads the catalogue when the JSON is actually on disk, so a publish that
    /// dropped the asset must degrade to showing everything rather than to a permanently empty overlay.
    /// </summary>
    [Fact]
    public void With_no_catalog_loaded_nothing_is_filtered_out()
    {
        var dm = new DataManager();
        dm.SeedObservedBuffBases(new[] { 11810000 });

        Assert.Contains(dm.BuffPickerCatalog(), c => c.BaseCode == 11810000);
    }

    /// <summary>The overlay follows the same list. A buff the game broadcasts but the catalogue does not carry
    /// has no picker row, so drawing it would put a slot on screen the user cannot switch off.</summary>
    [Fact]
    public void The_overlay_draws_only_catalogued_buffs()
    {
        const int Me = 900;
        long t0 = 1_000_000;
        var dm = new DataManager { Clock = () => t0 };
        dm.SaveNickname(Me, "본인", isExecutor: true, server: 3, jobByte: 0);
        dm.LoadBuffCatalog(new (int, string, string)[] { (18250000, "질풍의 권능", "호법성") }, System.Array.Empty<int>());

        dm.SaveUseBuff(Me, 182500511, t0, t0 + 30_000, 30_000, Me, level: 5);  // 카탈로그에 있음
        dm.SaveUseBuff(Me, 118100511, t0, t0 + 30_000, 30_000, Me, level: 5);  // 없음

        int[] codes = dm.ActiveOwnerBuffs(t0 + 1_000).Select(b => b.Code).ToArray();
        Assert.Equal(new[] { 18250000 }, codes);
    }

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

using WaffleMeter.App.Core;
using WaffleMeter.Services;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

public sealed class MeterColorThemeTests : IDisposable
{
    private readonly string _temp;
    private readonly PropertyHandler _props;

    // Verbatim JSON.stringify(DEFAULT_THEME) from useSettingsStore.ts.
    private const string DefaultThemeJson =
        "{\"userBar\":[\"#15c98f\",\"#0b8f72\"],\"normalBar\":[\"#f6c65b\",\"#d68a21\"]," +
        "\"warningBar\":[\"#ff9f45\",\"#d96d19\"],\"errorBar\":[\"#ef4444\",\"#991b1b\"]," +
        "\"bossBar\":[\"#e11d48\",\"#7f1d1d\"],\"serverAColor\":\"#7dd3fc\",\"serverBColor\":\"#f0abfc\"," +
        "\"serverDefaultColor\":\"#ffffff\",\"meterStatAmount\":\"#f8d66d\",\"meterStatDps\":\"#f8fafc\"," +
        "\"meterStatPercent\":\"#8ee6cf\",\"bossRightValue\":\"#fecdd3\",\"combatTimeColor\":\"#cbd5e1\"}";

    public MeterColorThemeTests()
    {
        _temp = Path.Combine(Path.GetTempPath(), "wm_theme_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_temp);
        _props = new PropertyHandler(_temp);
    }

    public void Dispose()
    {
        try { Directory.Delete(_temp, true); } catch { /* best effort */ }
    }

    [Fact]
    public void Defaults_match_react()
    {
        var theme = new MeterColorTheme(_props);
        Assert.Equal("#15c98f", theme.UserBarFrom);
        Assert.Equal("#0b8f72", theme.UserBarTo);
        Assert.Equal("#f8d66d", theme.MeterStatAmount);
        Assert.Equal("#ffffff", theme.ServerDefaultColor);
        Assert.Equal("#cbd5e1", theme.CombatTimeColor);
    }

    [Fact]
    public void Persisted_json_is_byte_identical_to_react_default_theme()
    {
        var theme = new MeterColorTheme(_props);
        theme.Reset(); // forces a persist of all defaults
        Assert.Equal(DefaultThemeJson, _props.GetProperty("theme"));
    }

    [Fact]
    public void Setting_a_color_persists_and_reloads()
    {
        var theme = new MeterColorTheme(_props) { ServerAColor = "#123456" };
        // a fresh instance reads it back from the same props
        var reloaded = new MeterColorTheme(_props);
        Assert.Equal("#123456", reloaded.ServerAColor);
        Assert.Equal("#0b8f72", reloaded.UserBarTo); // untouched -> default
    }

    [Fact]
    public void Partial_saved_theme_merges_over_defaults()
    {
        _props.SetProperty("theme", "{\"serverAColor\":\"#abcdef\"}");
        var theme = new MeterColorTheme(_props);
        Assert.Equal("#abcdef", theme.ServerAColor);            // from saved
        Assert.Equal("#15c98f", theme.UserBarFrom);             // default (missing key)
        Assert.Equal("#f0abfc", theme.ServerBColor);            // default
    }

    [Fact]
    public void Malformed_theme_falls_back_to_defaults()
    {
        _props.SetProperty("theme", "not json {");
        var theme = new MeterColorTheme(_props);
        Assert.Equal("#15c98f", theme.UserBarFrom);
    }

    [Fact]
    public void Changed_fires_on_set_and_reset()
    {
        var theme = new MeterColorTheme(_props);
        int fired = 0;
        theme.Changed += (_, _) => fired++;
        theme.UserBarFrom = "#000000";
        theme.Reset();
        Assert.Equal(2, fired);
    }
}

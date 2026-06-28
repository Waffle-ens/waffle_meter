using WaffleMeter.App.Core;
using WaffleMeter.Services;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

public sealed class HotkeyHandlerTests : IDisposable
{
    private readonly string _temp;

    public HotkeyHandlerTests()
    {
        _temp = Path.Combine(Path.GetTempPath(), "wm_hotkey_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_temp);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_temp, recursive: true);
        }
        catch
        {
            // best effort
        }
    }

    [Fact]
    public void Combo_round_trips_through_to_string_and_parse()
    {
        var combo = new HotkeyCombo(HotkeyHandler.ModControl, 0x52);
        Assert.Equal("modifiers=2,vkCode=82", combo.ToString());
        Assert.Equal(combo, HotkeyCombo.TryParse(combo.ToString()));
    }

    [Fact]
    public void Combo_parse_trims_whitespace()
    {
        Assert.Equal(new HotkeyCombo(2, 82), HotkeyCombo.TryParse("modifiers = 2 , vkCode = 82"));
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("modifiers=2")]            // missing vkCode
    [InlineData("modifiers=x,vkCode=82")]  // non-numeric
    [InlineData("")]
    public void Combo_parse_returns_null_on_bad_input(string input)
    {
        Assert.Null(HotkeyCombo.TryParse(input));
    }

    [Fact]
    public void Defaults_are_ctrl_r_h_t()
    {
        var handler = new HotkeyHandler(new PropertyHandler(_temp));
        Assert.Equal(new HotkeyCombo(HotkeyHandler.ModControl, 0x52), handler.Reset);        // Ctrl+R
        Assert.Equal(new HotkeyCombo(HotkeyHandler.ModControl, 0x48), handler.Visibility);   // Ctrl+H
        Assert.Equal(new HotkeyCombo(HotkeyHandler.ModControl, 0x54), handler.ClickThrough); // Ctrl+T
    }

    [Fact]
    public void Set_persists_and_reloads()
    {
        var handler = new HotkeyHandler(new PropertyHandler(_temp));
        handler.SetReset(new HotkeyCombo(HotkeyHandler.ModAlt, 0x41)); // Alt+A — not started, so no listener

        var reopened = new HotkeyHandler(new PropertyHandler(_temp));
        Assert.Equal(new HotkeyCombo(HotkeyHandler.ModAlt, 0x41), reopened.Reset);
    }

    [Fact]
    public void Corrupt_property_falls_back_to_default()
    {
        var props = new PropertyHandler(_temp);
        props.SetProperty("hotkey", "not-a-combo");

        var handler = new HotkeyHandler(props);
        Assert.Equal(new HotkeyCombo(HotkeyHandler.ModControl, 0x52), handler.Reset);
    }

    [Fact]
    public void Unassigned_persists_and_reloads_as_null()
    {
        var handler = new HotkeyHandler(new PropertyHandler(_temp));
        handler.SetReset(null); // 미지정 — no global hotkey for reset

        var reopened = new HotkeyHandler(new PropertyHandler(_temp));
        Assert.Null(reopened.Reset);
        // the other two stay at their defaults (only reset was unassigned)
        Assert.Equal(new HotkeyCombo(HotkeyHandler.ModControl, 0x48), reopened.Visibility);
        Assert.Equal(new HotkeyCombo(HotkeyHandler.ModControl, 0x54), reopened.ClickThrough);
    }

    [Fact]
    public void Unassigned_marker_does_not_fall_back_to_default()
    {
        // "none" is the explicit-unassigned marker: it must NOT be treated like a corrupt value and
        // revert to the default (that would make a hotkey impossible to turn off).
        var props = new PropertyHandler(_temp);
        props.SetProperty("hotkey", "none");

        var handler = new HotkeyHandler(props);
        Assert.Null(handler.Reset);
    }
}

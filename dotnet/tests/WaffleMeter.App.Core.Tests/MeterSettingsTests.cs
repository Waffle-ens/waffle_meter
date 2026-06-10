using WaffleMeter.App.Core;
using WaffleMeter.Services;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

public sealed class MeterSettingsTests : IDisposable
{
    private readonly string _temp;

    public MeterSettingsTests()
    {
        _temp = Path.Combine(Path.GetTempPath(), "wm_settings_" + Guid.NewGuid().ToString("N"));
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
    public void Defaults_match_react_store()
    {
        var s = new MeterSettings(new PropertyHandler(_temp));
        Assert.Equal("dps_percent", s.DisplayMode);
        Assert.Equal("dps", s.DamageValueMode);
        Assert.Equal("contribution", s.ContributionMode);
        Assert.Equal("all", s.NameDisplay);
        Assert.Equal(36, s.RowHeight);
        Assert.Equal(0.4, s.MeterOpacity);
        Assert.False(s.IsMinimal);
        Assert.False(s.MultiMonitorMode);
        Assert.Equal("dark", s.OverlayTheme);
    }

    [Fact]
    public void Persists_with_byte_compatible_encoding()
    {
        var props = new PropertyHandler(_temp);
        var s = new MeterSettings(props)
        {
            IsMinimal = true,
            RowHeight = 48,
            MeterOpacity = 0.7,
            DisplayMode = "amount_percent",
        };

        // booleans lowercase, numbers invariant, enums raw — like the Kotlin/React store.
        Assert.Equal("true", props.GetProperty("isMinimal"));
        Assert.Equal("48", props.GetProperty("rowHeight"));
        Assert.Equal("0.7", props.GetProperty("meterOpacity"));
        Assert.Equal("amount_percent", props.GetProperty("displayMode"));

        var reopened = new MeterSettings(new PropertyHandler(_temp));
        Assert.True(reopened.IsMinimal);
        Assert.Equal(48, reopened.RowHeight);
        Assert.Equal(0.7, reopened.MeterOpacity);
        Assert.Equal("amount_percent", reopened.DisplayMode);
    }

    [Fact]
    public void Coerces_unknown_enum_to_default()
    {
        var props = new PropertyHandler(_temp);
        props.SetProperty("displayMode", "garbage");
        props.SetProperty("nameDisplay", "me_only");

        var s = new MeterSettings(props);
        Assert.Equal("dps_percent", s.DisplayMode); // coerced
        Assert.Equal("me_only", s.NameDisplay);      // valid
        Assert.Equal(NameDisplay.MeOnly, s.NameDisplayMode);
    }

    [Fact]
    public void Raises_property_changed_on_csharp_name()
    {
        var s = new MeterSettings(new PropertyHandler(_temp));
        string? changed = null;
        s.PropertyChanged += (_, e) => changed = e.PropertyName;
        s.RowHeight = 50;
        Assert.Equal(nameof(MeterSettings.RowHeight), changed);
    }
}

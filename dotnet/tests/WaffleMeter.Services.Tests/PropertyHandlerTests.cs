using System.Text;
using WaffleMeter.Services;
using Xunit;

namespace WaffleMeter.Services.Tests;

public sealed class PropertyHandlerTests : IDisposable
{
    private const string AppName = "waffle_meter.v1.4";
    private readonly string _tempAppData;

    public PropertyHandlerTests()
    {
        _tempAppData = Path.Combine(Path.GetTempPath(), "wm_ph_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempAppData);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempAppData, recursive: true);
        }
        catch
        {
            // best effort
        }
    }

    [Fact]
    public void SetProperty_persists_across_instances()
    {
        var first = new PropertyHandler(_tempAppData);
        first.SetProperty("opacity", "0.8");
        first.SetProperty("isAutoHide", "true");

        var second = new PropertyHandler(_tempAppData);
        Assert.Equal("0.8", second.GetProperty("opacity"));
        Assert.Equal("true", second.GetProperty("isAutoHide"));
        Assert.Equal(Path.Combine(_tempAppData, AppName), second.AppDirectory());
    }

    [Fact]
    public void GetProperty_returns_default_when_missing()
    {
        var ph = new PropertyHandler(_tempAppData);
        Assert.Null(ph.GetProperty("nope"));
        Assert.Equal("fallback", ph.GetProperty("nope", "fallback"));
    }

    [Fact]
    public void Legacy_settings_are_copied_forward_once()
    {
        string legacyDir = Path.Combine(_tempAppData, "waffle_meter.v1.3");
        Directory.CreateDirectory(legacyDir);
        File.WriteAllText(Path.Combine(legacyDir, "settings.properties"), "carried=over\n", Encoding.Latin1);

        var ph = new PropertyHandler(_tempAppData);

        Assert.Equal("over", ph.GetProperty("carried"));
        Assert.True(File.Exists(Path.Combine(_tempAppData, AppName, "settings.properties")));
    }

    [Fact]
    public void Ascii_values_are_unaffected_by_the_euckr_requantize()
    {
        var ph = new PropertyHandler(_tempAppData);
        ph.SetProperty("server.ip", "206.127.156.0/24");
        ph.SetProperty("server.port", "13328");

        var reopened = new PropertyHandler(_tempAppData);
        Assert.Equal("206.127.156.0/24", reopened.GetProperty("server.ip"));
        Assert.Equal("13328", reopened.GetProperty("server.port"));
    }

    [Fact]
    public void Raw_euckr_bytes_in_file_are_recovered_as_korean()
    {
        // Simulate a legacy value written as raw EUC-KR bytes (not \u escaped). Java's load reads it
        // as ISO-8859-1, and getProperty re-decodes those bytes as EUC-KR — the preserved quirk.
        Directory.CreateDirectory(Path.Combine(_tempAppData, AppName));
        byte[] korean = Encoding.GetEncoding(51949).GetBytes("가"); // 가 -> B0 A1
        using (var fs = File.Create(Path.Combine(_tempAppData, AppName, "settings.properties")))
        {
            fs.Write(Encoding.Latin1.GetBytes("nick="));
            fs.Write(korean);
            fs.Write(Encoding.Latin1.GetBytes("\n"));
        }

        var ph = new PropertyHandler(_tempAppData);
        Assert.Equal("가", ph.GetProperty("nick"));
    }
}

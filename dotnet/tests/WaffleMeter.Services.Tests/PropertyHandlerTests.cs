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
    [Fact]
    public void RunBatched_writes_the_file_once_and_persists_everything()
    {
        var ph = new PropertyHandler(_tempAppData);
        ph.SetProperty("seed", "1");
        int before = ph.SaveCount;

        ph.RunBatched(() =>
        {
            for (int i = 0; i < 20; i++)
            {
                ph.SetProperty("k" + i, i.ToString());
            }
        });

        // The point of batching. Counted, not timed: 20 rewrites of a small file all land inside one filesystem
        // timestamp tick, so a before/after timestamp cannot tell the two apart.
        Assert.Equal(before + 1, ph.SaveCount);

        var reloaded = new PropertyHandler(_tempAppData);
        for (int i = 0; i < 20; i++)
        {
            Assert.Equal(i.ToString(), reloaded.GetProperty("k" + i));
        }
    }

    [Fact]
    public void RunBatched_still_saves_when_the_body_throws()
    {
        // A half-applied import must not also be an unsaved one: whatever did land has to survive a restart,
        // or the user is left with a state that no backup file describes.
        var ph = new PropertyHandler(_tempAppData);
        Assert.Throws<InvalidOperationException>(() => ph.RunBatched(() =>
        {
            ph.SetProperty("before", "yes");
            throw new InvalidOperationException("boom");
        }));

        Assert.Equal("yes", new PropertyHandler(_tempAppData).GetProperty("before"));
    }

    [Fact]
    public void Writes_after_a_batch_save_immediately_again()
    {
        // The failure mode this API exists to prevent: a batch that never closes would make every later write
        // memory-only, with no symptom until restart.
        var ph = new PropertyHandler(_tempAppData);
        ph.RunBatched(() => ph.SetProperty("inside", "1"));
        ph.SetProperty("outside", "2");

        Assert.Equal("2", new PropertyHandler(_tempAppData).GetProperty("outside"));
    }

    [Fact]
    public void RawEntries_returns_the_stored_value_without_the_EucKr_re_decode()
    {
        // GetProperty always runs Latin-1 -> EUC-KR on the way out, which is a documented quirk the app relies
        // on. Export needs the untouched value for keys no live model owns, so the two must differ here.
        var ph = new PropertyHandler(_tempAppData);
        ph.SetProperty("theme", "{\"name\":\"가\"}");

        var reloaded = new PropertyHandler(_tempAppData);
        Assert.Equal("{\"name\":\"가\"}", reloaded.RawEntries()["theme"]);
    }

    [Fact]
    public void RawEntries_is_a_snapshot_not_a_live_view()
    {
        var ph = new PropertyHandler(_tempAppData);
        ph.SetProperty("a", "1");
        var snap = ph.RawEntries();
        ph.SetProperty("a", "2");

        Assert.Equal("1", snap["a"]);
        Assert.Equal("2", ph.RawEntries()["a"]);
    }

    [Fact]
    public void Save_leaves_no_temp_file_behind()
    {
        var ph = new PropertyHandler(_tempAppData);
        ph.SetProperty("a", "1");
        Assert.Empty(Directory.GetFiles(Path.Combine(_tempAppData, AppName), "*.tmp"));
    }
}

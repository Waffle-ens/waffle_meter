using System.Text;
using WaffleMeter.App.Core;
using WaffleMeter.Services;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

/// <summary>
/// Export/import round-trips against a REAL <see cref="PropertyHandler"/> on disk, because the interesting
/// failures all live in the storage layer: values are re-decoded on the way out, so a bundle that reads through
/// one representation and writes through another looks right until the next restart.
/// </summary>
public sealed class SettingsBundleBuilderTests : IDisposable
{
    private const string AppName = "waffle_meter.v1.4";
    private readonly string _tempAppData;
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 0, 0, 0, TimeSpan.Zero);

    public SettingsBundleBuilderTests()
    {
        _tempAppData = Path.Combine(Path.GetTempPath(), "wm_bundle_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempAppData);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempAppData, recursive: true); } catch { /* temp */ }
    }

    private PropertyHandler NewHandler() => new(_tempAppData);

    private string AppDir => Path.Combine(_tempAppData, AppName);

    [Fact]
    public void Carries_only_catalogued_keys()
    {
        PropertyHandler props = NewHandler();
        props.SetProperty("rowHeight", "44");
        props.SetProperty("statsInstallId", "SECRET-GUID");
        props.SetProperty("uiX", "1234");
        props.SetProperty("aether.lastValue", "840,120,1");

        SettingsBundle b = SettingsBundleBuilder.Build(props, SettingsProfile.Full, "test", Now);

        Assert.Equal("44", b.Data["rowHeight"]);
        Assert.DoesNotContain("statsInstallId", b.Data.Keys);
        Assert.DoesNotContain("uiX", b.Data.Keys);
        Assert.DoesNotContain("aether.lastValue", b.Data.Keys);
    }

    [Fact]
    public void The_whole_code_contains_no_excluded_key_name_or_value()
    {
        // The property that makes handing a code to a stranger safe. Checked against the encoded string, not
        // the object, because that is what actually leaves the machine.
        PropertyHandler props = NewHandler();
        props.SetProperty("statsInstallKeyPkcs8DpapiV1", "AAAA-PRIVATE-KEY-AAAA");
        props.SetProperty("statsConsentIdentityHash", "deadbeefdeadbeef");
        props.SetProperty("rowHeight", "44");

        string code = SettingsBundleCodec.Encode(SettingsBundleBuilder.Build(props, SettingsProfile.Full, "test", Now));
        Assert.True(SettingsBundleCodec.TryDecode(code, out SettingsBundle back, out _));

        string json = string.Join('', back.Data.Select(kv => kv.Key + "=" + kv.Value));
        Assert.DoesNotContain("PRIVATE-KEY", json, StringComparison.Ordinal);
        Assert.DoesNotContain("deadbeef", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Design_code_is_a_subset_of_the_full_code()
    {
        PropertyHandler props = NewHandler();
        foreach (SettingsKey k in SettingsKeyCatalog.All)
        {
            props.SetProperty(k.Key, "x");
        }

        var full = SettingsBundleBuilder.Build(props, SettingsProfile.Full, "test", Now);
        var design = SettingsBundleBuilder.Build(props, SettingsProfile.Design, "test", Now);

        Assert.True(design.Data.Count < full.Data.Count);
        Assert.All(design.Data.Keys, k => Assert.Contains(k, full.Data.Keys));
        Assert.DoesNotContain("alarms.custom", design.Data.Keys);
    }

    [Fact]
    public void A_key_that_was_never_written_is_left_out_rather_than_exported_as_a_default()
    {
        // Exporting defaults would make the code overwrite the receiver's deliberate choices with "whatever the
        // sender never touched" — the opposite of what sharing a look is for.
        PropertyHandler props = NewHandler();
        props.SetProperty("rowHeight", "44");

        SettingsBundle b = SettingsBundleBuilder.Build(props, SettingsProfile.Full, "test", Now);
        Assert.DoesNotContain("barStyle", b.Data.Keys);
    }

    [Fact]
    public void Plan_reports_changes_unchanged_unknown_and_missing_separately()
    {
        PropertyHandler props = NewHandler();
        props.SetProperty("rowHeight", "36");
        props.SetProperty("barStyle", "fill");

        var bundle = new SettingsBundle { Profile = "D" };
        bundle.Data["rowHeight"] = "50";        // changes
        bundle.Data["barStyle"] = "fill";       // unchanged
        bundle.Data["someFutureKey"] = "1";     // unknown

        SettingsBundlePlan plan = SettingsBundleBuilder.Plan(props, bundle);

        Assert.Single(plan.Changes);
        Assert.Equal("행 높이", plan.Changes[0].Label);
        Assert.Equal("36", plan.Changes[0].From);
        Assert.Equal("50", plan.Changes[0].To);
        Assert.Equal(1, plan.UnchangedCount);
        Assert.Equal(1, plan.UnknownCount);
        Assert.True(plan.MissingCount > 0);
        Assert.True(plan.HasWork);
    }

    [Fact]
    public void Plan_writes_nothing()
    {
        PropertyHandler props = NewHandler();
        props.SetProperty("rowHeight", "36");

        var bundle = new SettingsBundle();
        bundle.Data["rowHeight"] = "50";
        SettingsBundleBuilder.Plan(props, bundle);

        Assert.Equal("36", NewHandler().GetProperty("rowHeight"));
    }

    [Fact]
    public void A_korean_value_survives_export_import_AND_a_restart()
    {
        // THE test for this feature. The storage layer re-decodes on read, so a bundle that exports the decoded
        // value and imports it back writes a different string than it read. That renders correctly for the rest
        // of the session and changes the next time the app starts — which is why the check reopens the handler.
        PropertyHandler source = NewHandler();
        source.SetProperty("theme", "{\"이름\":\"기본 테마\"}");
        source.SetProperty("fontFamily", "나눔손글씨 붓");

        string code = SettingsBundleCodec.Encode(
            SettingsBundleBuilder.Build(source, SettingsProfile.Full, "test", Now));

        // A different machine: same folder wiped, fresh handler.
        Directory.Delete(AppDir, recursive: true);
        PropertyHandler target = NewHandler();
        Assert.True(SettingsBundleCodec.TryDecode(code, out SettingsBundle bundle, out _));
        target.RunBatched(() =>
        {
            foreach ((string k, string v) in bundle.Data)
            {
                target.SetProperty(k, v);
            }
        });

        PropertyHandler afterRestart = NewHandler();
        Assert.Equal(source.GetProperty("theme"), afterRestart.GetProperty("theme"));
        Assert.Equal(source.GetProperty("fontFamily"), afterRestart.GetProperty("fontFamily"));
    }

    [Fact]
    public void MeterSettings_picks_up_an_import_without_a_restart()
    {
        PropertyHandler props = NewHandler();
        var settings = new MeterSettings(props);
        Assert.Equal(36, settings.RowHeight);

        props.SetProperty("rowHeight", "52");
        settings.Reload();

        Assert.Equal(52, settings.RowHeight);
    }

    [Fact]
    public void Reload_announces_that_everything_may_have_changed()
    {
        // WPF reads an empty/null property name as "rebind everything". Listing names instead would silently
        // miss whichever key gets added next.
        PropertyHandler props = NewHandler();
        var settings = new MeterSettings(props);
        string? seen = "unset";
        settings.PropertyChanged += (_, e) => seen = e.PropertyName;

        settings.Reload();

        Assert.True(string.IsNullOrEmpty(seen));
    }

    [Fact]
    public void Backup_is_written_before_an_import_and_can_be_read_back()
    {
        PropertyHandler props = NewHandler();
        props.SetProperty("rowHeight", "44");

        string? path = SettingsBackupStore.Save(props, "test", Now);
        Assert.NotNull(path);
        Assert.True(File.Exists(path));

        string? code = SettingsBackupStore.ReadNewest(props.AppDirectory());
        Assert.True(SettingsBundleCodec.TryDecode(code, out SettingsBundle back, out _));
        Assert.Equal("44", back.Data["rowHeight"]);
    }

    [Fact]
    public void Backups_are_capped_and_the_newest_wins()
    {
        PropertyHandler props = NewHandler();
        for (int i = 0; i < 14; i++)
        {
            props.SetProperty("rowHeight", (30 + i).ToString());
            SettingsBackupStore.Save(props, "test", Now.AddSeconds(i));
        }

        Assert.Equal(10, SettingsBackupStore.List(props.AppDirectory()).Count);
        Assert.True(SettingsBundleCodec.TryDecode(SettingsBackupStore.ReadNewest(props.AppDirectory()), out SettingsBundle newest, out _));
        Assert.Equal("43", newest.Data["rowHeight"]);
    }

    [Fact]
    public void Reading_a_backup_when_none_exists_is_not_an_error()
    {
        Assert.Empty(SettingsBackupStore.List(Path.Combine(_tempAppData, "nope")));
        Assert.Null(SettingsBackupStore.ReadNewest(Path.Combine(_tempAppData, "nope")));
    }
}

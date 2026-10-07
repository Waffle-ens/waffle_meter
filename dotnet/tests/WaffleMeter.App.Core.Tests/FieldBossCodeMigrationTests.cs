using WaffleMeter.App.Core;
using WaffleMeter.Capture;
using WaffleMeter.Services;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

/// <summary>
/// Spec for <see cref="FieldBossCodeMigration"/>: the 2026-10-07 어비스 중층 slot fix retired 2600150 / 2600156
/// (two "분노한 수호신장 나흐마" rows that were really M_AR2 NPCs) for 2600479 / 2600480. A picker choice saved
/// under an old code must keep meaning the row the user unticked.
/// </summary>
public sealed class FieldBossCodeMigrationTests : IDisposable
{
    private readonly string _temp;

    public FieldBossCodeMigrationTests()
    {
        _temp = Path.Combine(Path.GetTempPath(), "wm_fbmig_" + Guid.NewGuid().ToString("N"));
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

    [Theory]
    [InlineData("2600150", "2600479")]
    [InlineData("2600156", "2600480")]
    [InlineData("2600150,2600156", "2600479,2600480")]
    [InlineData("2600156,2600150", "2600480,2600479")]                          // order is the user's, kept
    [InlineData("2406034,2600150,2600084", "2406034,2600479,2600084")]          // the rest untouched, in place
    [InlineData("2600150,2600520,2600521,2600522", "2600479,2600520,2600521,2600522")] // 드라모스/듀칼/마라카 keep their codes
    [InlineData(" 2600150 , 2406034 ", "2600479,2406034")]
    public void Retired_codes_move_to_their_replacements_in_place(string stored, string expected)
    {
        Assert.Equal(expected, FieldBossCodeMigration.MigrateDisabledCsv(stored));
    }

    /// <summary>The value is a set: an old code whose replacement is already there collapses into it.</summary>
    [Fact]
    public void A_replacement_already_present_is_not_listed_twice()
    {
        Assert.Equal("2600479,2406034", FieldBossCodeMigration.MigrateDisabledCsv("2600479,2406034,2600150"));
        Assert.Equal("2600480", FieldBossCodeMigration.MigrateDisabledCsv("2600156,2600480"));
    }

    /// <summary>Nothing to migrate → the very same string back, which is what lets the caller skip the write.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("2406034")]
    [InlineData("2600479,2600480,2600520")]
    [InlineData("2406034, 2600084,,junk")]
    public void A_value_without_retired_codes_comes_back_unchanged(string stored)
    {
        Assert.Same(stored, FieldBossCodeMigration.MigrateDisabledCsv(stored));
    }

    [Theory]
    [InlineData("2600150,2600156,2406034")]
    [InlineData("2600156,junk,2600150")]
    public void Migrating_twice_is_migrating_once(string stored)
    {
        string once = FieldBossCodeMigration.MigrateDisabledCsv(stored);
        Assert.Equal(once, FieldBossCodeMigration.MigrateDisabledCsv(once));
    }

    /// <summary>A non-numeric entry is not a code, so it is not this migration's to drop.</summary>
    [Fact]
    public void A_non_numeric_entry_is_kept()
    {
        Assert.Equal("junk,2600479", FieldBossCodeMigration.MigrateDisabledCsv("junk,2600150"));
    }

    /// <summary>Retired codes are gone from the catalog, and each replacement is a 중층 row that reads the same name
    /// the user saw on the old one — that is the whole basis of the pairing.</summary>
    [Fact]
    public void Each_replacement_is_a_middle_floor_row_with_the_same_name()
    {
        foreach ((int retired, int current) in FieldBossCodeMigration.Replaced)
        {
            Assert.False(FieldBossCatalog.IsKnown(retired));
            Assert.True(FieldBossCatalog.IsKnown(current));
            Assert.Equal("분노한 수호신장 나흐마", FieldBossCatalog.Name(current));
            Assert.Contains(
                FieldBossCatalog.All(),
                b => b.Code == current && b.MapId == FieldBossCatalog.AbyssMiddleMapId);
        }

        Assert.Equal([2600479, 2600480], FieldBossCodeMigration.Replaced.Values.Order());
    }

    /// <summary>Loading the settings migrates the stored key and writes it back once; the picker's set reads the new
    /// codes, and a second load finds nothing left to do.</summary>
    [Fact]
    public void Loading_settings_migrates_and_persists_the_disabled_list()
    {
        var props = new PropertyHandler(_temp);
        props.SetProperty("alarms.fieldBossDisabled", "2406034,2600156,2600150,2600521");

        var settings = new MeterSettings(props);

        Assert.Equal("2406034,2600480,2600479,2600521", settings.FieldBossDisabled);
        Assert.Equal([2406034, 2600479, 2600480, 2600521], settings.FieldBossDisabledCodes.Order());
        Assert.Equal("2406034,2600480,2600479,2600521", props.GetProperty("alarms.fieldBossDisabled"));
        Assert.Equal("2406034,2600480,2600479,2600521", new PropertyHandler(_temp).GetProperty("alarms.fieldBossDisabled"));

        Assert.Equal("2406034,2600480,2600479,2600521", new MeterSettings(new PropertyHandler(_temp)).FieldBossDisabled);
    }

    /// <summary>The settings import writes the raw stored value and calls <see cref="MeterSettings.Reload"/> — an
    /// old backup restored onto a new build migrates the same way.</summary>
    [Fact]
    public void Reload_after_an_import_migrates_too()
    {
        var props = new PropertyHandler(_temp);
        var settings = new MeterSettings(props);

        props.SetProperty("alarms.fieldBossDisabled", "2600150");
        settings.Reload();

        Assert.Equal("2600479", settings.FieldBossDisabled);
        Assert.Equal("2600479", props.GetProperty("alarms.fieldBossDisabled"));
    }

    /// <summary>No stored key → nothing written: a fresh install must not grow an empty key out of the migration.</summary>
    [Fact]
    public void An_absent_key_stays_absent()
    {
        var props = new PropertyHandler(_temp);

        Assert.Equal("", new MeterSettings(props).FieldBossDisabled);
        Assert.Null(props.GetProperty("alarms.fieldBossDisabled"));
    }
}

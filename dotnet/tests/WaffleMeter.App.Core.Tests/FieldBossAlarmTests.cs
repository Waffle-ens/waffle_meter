using WaffleMeter.App.Core;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

/// <summary>Spec for the pure field-boss reminder schedule (<see cref="FieldBossAlarm"/>).</summary>
public class FieldBossAlarmTests
{
    private const long Now = 1_783_000_000_000L;

    [Fact]
    public void Catalog_lists_every_boss_split_by_realm()
    {
        var all = FieldBossCatalog.All();
        Assert.Equal(24, all.Count);
        Assert.Equal(12, all.Count(b => b.Realm == "천족"));
        Assert.Equal(12, all.Count(b => b.Realm == "마족"));
        Assert.All(all, b => Assert.False(string.IsNullOrWhiteSpace(b.Name)));
    }

    [Fact]
    public void A_lead_is_due_inside_its_one_minute_window()
    {
        var timers = new Dictionary<int, long> { [2406034] = Now + 10 * 60_000L - 5_000 }; // 9m55s out
        var due = FieldBossAlarm.DueAlerts(timers, Now, new[] { 10 });

        FieldBossAlarm.Due d = Assert.Single(due);
        Assert.Equal(2406034, d.Code);
        Assert.Equal(10, d.LeadMinutes);
    }

    [Fact]
    public void A_lead_is_not_due_before_or_after_its_window()
    {
        var timers = new Dictionary<int, long> { [2406034] = Now + 12 * 60_000L }; // 12m out
        Assert.Empty(FieldBossAlarm.DueAlerts(timers, Now, new[] { 10 }));         // before the (9,10] window

        var past = new Dictionary<int, long> { [2406034] = Now - 60_000L };        // already spawned
        Assert.Empty(FieldBossAlarm.DueAlerts(past, Now, new[] { 10 }));
    }

    [Fact]
    public void Multiple_leads_can_each_fire()
    {
        var timers = new Dictionary<int, long>
        {
            [2406034] = Now + 5 * 60_000L - 1_000,   // in the 5-min window
            [2101217] = Now + 30 * 60_000L - 1_000,  // in the 30-min window
        };
        var due = FieldBossAlarm.DueAlerts(timers, Now, new[] { 5, 10, 30 });
        Assert.Equal(2, due.Count);
        Assert.Contains(due, d => d.Code == 2406034 && d.LeadMinutes == 5);
        Assert.Contains(due, d => d.Code == 2101217 && d.LeadMinutes == 30);
    }

    [Fact]
    public void Key_is_stable_per_boss_respawn_lead()
    {
        var d = new FieldBossAlarm.Due(2406034, Now, 10);
        Assert.Equal(FieldBossAlarm.Key(d), FieldBossAlarm.Key(new FieldBossAlarm.Due(2406034, Now, 10)));
        Assert.NotEqual(FieldBossAlarm.Key(d), FieldBossAlarm.Key(new FieldBossAlarm.Due(2406034, Now, 5)));
    }

    [Fact]
    public void Catalog_resolves_known_and_unknown_codes()
    {
        Assert.Equal("파르곤", FieldBossCatalog.Name(2406034));
        Assert.True(FieldBossCatalog.IsKnown(2406034));
        Assert.False(FieldBossCatalog.IsKnown(9999999));
        Assert.Contains("9999999", FieldBossCatalog.Name(9999999));
    }
}

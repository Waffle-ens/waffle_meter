using WaffleMeter.App.Core;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

/// <summary>
/// Locks the 슈고 페스타 reminder schedule (ShugoAlarm.DueLead): the lead fires at the matching minute before
/// the top of the hour (HH:50 = 10분 전, HH:55 = 5분 전, HH:59 = 1분 전, HH:00 = 시작), only when that lead is
/// enabled, and nothing fires mid-hour.
/// </summary>
public sealed class ShugoAlarmTests
{
    private static readonly int[] AllLeads = { 10, 5, 1, 0 };

    private static DateTime At(int minute) => new(2026, 6, 25, 13, minute, 0);

    [Fact]
    public void Lead10_DueAtFiftyPast() => Assert.Equal(10, ShugoAlarm.DueLead(At(50), AllLeads));

    [Fact]
    public void Lead5_DueAtFiftyFivePast() => Assert.Equal(5, ShugoAlarm.DueLead(At(55), AllLeads));

    [Fact]
    public void Lead1_DueAtFiftyNinePast() => Assert.Equal(1, ShugoAlarm.DueLead(At(59), AllLeads));

    [Fact]
    public void Start_DueOnTheHour() => Assert.Equal(0, ShugoAlarm.DueLead(At(0), AllLeads));

    [Fact]
    public void NothingDue_MidHour() => Assert.Null(ShugoAlarm.DueLead(At(30), AllLeads));

    [Fact]
    public void DisabledLead_DoesNotFire()
    {
        // 10-min lead not enabled: at :50 nothing is due.
        Assert.Null(ShugoAlarm.DueLead(At(50), new[] { 5, 1, 0 }));
    }

    [Fact]
    public void OnlyStartEnabled_FiresOnlyOnTheHour()
    {
        Assert.Equal(0, ShugoAlarm.DueLead(At(0), new[] { 0 }));
        Assert.Null(ShugoAlarm.DueLead(At(55), new[] { 0 }));
    }
}

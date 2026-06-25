using WaffleMeter.App.Core;
using WaffleMeter.Services;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

/// <summary>
/// Covers user custom alarms: the Base64(JSON) codec round-trips Korean titles + day sets, the whole list
/// survives a write/reopen through the Java-.properties settings store (the EUC-KR/escaping hazard), and the
/// schedule fires only at the matching time/weekday.
/// </summary>
public sealed class CustomAlarmTests : IDisposable
{
    private readonly string _temp;

    public CustomAlarmTests()
    {
        _temp = Path.Combine(Path.GetTempPath(), "wm_alarm_" + Guid.NewGuid().ToString("N"));
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
    public void Codec_RoundTrips_KoreanTitleAndDays()
    {
        var alarms = new List<CustomAlarm>
        {
            new() { Id = "a1", Title = "출근 알림", Hour = 9, Minute = 5, Days = new[] { 1, 2, 3, 4, 5 } },
        };

        IReadOnlyList<CustomAlarm> back = CustomAlarmCodec.Decode(CustomAlarmCodec.Encode(alarms));

        Assert.Single(back);
        Assert.Equal("출근 알림", back[0].Title);
        Assert.Equal(9, back[0].Hour);
        Assert.Equal(5, back[0].Minute);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, back[0].Days);
    }

    [Fact]
    public void Codec_Decode_EmptyOrGarbage_ReturnsEmpty()
    {
        Assert.Empty(CustomAlarmCodec.Decode(null));
        Assert.Empty(CustomAlarmCodec.Decode(""));
        Assert.Empty(CustomAlarmCodec.Decode("not-base64!!"));
    }

    [Fact]
    public void Settings_RoundTrip_CustomAlarms_ThroughPropertiesStore_WithKorean()
    {
        var s1 = new MeterSettings(new PropertyHandler(_temp))
        {
            CustomAlarms = new List<CustomAlarm>
            {
                new() { Id = "x", Title = "저녁 공대", Hour = 20, Minute = 30, Days = new[] { 5, 6 } },
            },
        };

        var s2 = new MeterSettings(new PropertyHandler(_temp));

        Assert.Single(s2.CustomAlarms);
        Assert.Equal("저녁 공대", s2.CustomAlarms[0].Title);
        Assert.Equal(20, s2.CustomAlarms[0].Hour);
        Assert.Equal(30, s2.CustomAlarms[0].Minute);
        Assert.Equal(new[] { 5, 6 }, s2.CustomAlarms[0].Days);
    }

    // 2026-06-25's actual weekday is read at runtime, so this returns a date with the requested weekday
    // regardless of which day that is.
    private static DateTime On(DayOfWeek dow, int hour, int minute)
    {
        var baseDate = new DateTime(2026, 6, 25);
        int delta = (int)dow - (int)baseDate.DayOfWeek;
        return baseDate.AddDays(delta).AddHours(hour).AddMinutes(minute);
    }

    [Fact]
    public void IsDue_AtMatchingTimeAndDay()
    {
        var a = new CustomAlarm { Enabled = true, Hour = 9, Minute = 5, Days = new[] { (int)DayOfWeek.Monday } };
        Assert.True(CustomAlarmSchedule.IsDue(a, On(DayOfWeek.Monday, 9, 5)));
        Assert.False(CustomAlarmSchedule.IsDue(a, On(DayOfWeek.Monday, 9, 6)));  // wrong minute
        Assert.False(CustomAlarmSchedule.IsDue(a, On(DayOfWeek.Tuesday, 9, 5))); // wrong day
    }

    [Fact]
    public void IsDue_EmptyDays_MeansEveryDay()
    {
        var a = new CustomAlarm { Enabled = true, Hour = 9, Minute = 0, Days = Array.Empty<int>() };
        Assert.True(CustomAlarmSchedule.IsDue(a, On(DayOfWeek.Sunday, 9, 0)));
        Assert.True(CustomAlarmSchedule.IsDue(a, On(DayOfWeek.Wednesday, 9, 0)));
    }

    [Fact]
    public void IsDue_Disabled_NeverFires()
    {
        var a = new CustomAlarm { Enabled = false, Hour = 9, Minute = 0 };
        Assert.False(CustomAlarmSchedule.IsDue(a, On(DayOfWeek.Monday, 9, 0)));
    }
}

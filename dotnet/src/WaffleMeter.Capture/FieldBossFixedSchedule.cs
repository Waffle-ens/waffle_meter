namespace WaffleMeter.Capture;

/// <summary>
/// Fixed spawn schedules for the 어비스(혼돈의 에레슈란타) field bosses. Unlike the open-world regions —
/// where the server broadcasts an explicit respawn timestamp — several abyss entries ride the 0x9101 table
/// with no usable time, because their spawn is a fixed content schedule (요새 공성 시간표) rather than a
/// per-kill respawn. This computes the next occurrence so those rows can still raise a reminder.
/// <para>All times are KST (UTC+9). Pure and side-effect free; only consulted when the record itself carried
/// no plausible timestamp, so a server-sent time always wins.</para>
/// <para>⚠️ 이 시간표는 게임 콘텐츠 일정이라 패치로 바뀔 수 있고 패킷으로 검증되지 않았다. 서버가 시간을
/// 실어 보내기 시작하면 그 값이 우선하므로 틀려도 덮어써진다.</para>
/// </summary>
public static class FieldBossFixedSchedule
{
    private static readonly TimeSpan Kst = TimeSpan.FromHours(9);

    private enum Kind
    {
        /// <summary>Every hour, on the hour.</summary>
        Hourly,

        /// <summary>금·일 22:00.</summary>
        FriSun2200,

        /// <summary>수·토 22:30.</summary>
        WedSat2230,
    }

    private static readonly Dictionary<int, Kind> ByBossCode = new()
    {
        [2600089] = Kind.Hourly,       // 감시자 카이라 (하층)

        [2600084] = Kind.FriSun2200,   // 수호신장 나흐마 ×3 (하층)
        [2600093] = Kind.FriSun2200,
        [2600094] = Kind.FriSun2200,
        [2600150] = Kind.FriSun2200,   // 분노한 수호신장 나흐마 ×2 (중층)
        [2600156] = Kind.FriSun2200,

        [2600096] = Kind.WedSat2230,   // 집행자 타마사 (하층)
        [2600097] = Kind.WedSat2230,   // 정령왕 아그로 (하층, 집행자 슬롯)
        [2600098] = Kind.WedSat2230,   // 감시자 카이라 (하층, 집행자 슬롯)
        [2600520] = Kind.WedSat2230,   // 처형관 드라모스 (중층)
        [2600521] = Kind.WedSat2230,   // 반역자 듀칼 (중층)
        [2600522] = Kind.WedSat2230,   // 파멸자 마라카 (중층)
    };

    /// <summary>True when this boss spawns on a fixed schedule rather than a per-kill respawn timer.</summary>
    public static bool HasFixedSchedule(int bossCode) => ByBossCode.ContainsKey(bossCode);

    /// <summary>A short human label for the schedule, for the picker row ("매시 정각" / "금·일 22:00" /
    /// "수·토 22:30"), or null when the boss uses a normal respawn timer.</summary>
    public static string? Describe(int bossCode) => ByBossCode.TryGetValue(bossCode, out Kind k)
        ? k switch
        {
            Kind.Hourly => "매시 정각",
            Kind.FriSun2200 => "금·일 22:00",
            Kind.WedSat2230 => "수·토 22:30",
            _ => null,
        }
        : null;

    /// <summary>Next spawn at or after <paramref name="fromMs"/> (Unix ms) for a fixed-schedule boss.</summary>
    public static bool TryNextSpawn(int bossCode, long fromMs, out long targetMs)
    {
        targetMs = 0;
        if (!ByBossCode.TryGetValue(bossCode, out Kind kind))
        {
            return false;
        }

        DateTimeOffset now = DateTimeOffset.FromUnixTimeMilliseconds(fromMs).ToOffset(Kst);
        if (kind == Kind.Hourly)
        {
            var hour = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, Kst);
            targetMs = hour.AddHours(1).ToUnixTimeMilliseconds();
            return true;
        }

        (DayOfWeek a, DayOfWeek b, int h, int m) = kind == Kind.FriSun2200
            ? (DayOfWeek.Friday, DayOfWeek.Sunday, 22, 0)
            : (DayOfWeek.Wednesday, DayOfWeek.Saturday, 22, 30);

        for (int i = 0; i <= 7; i++)
        {
            DateTime day = now.Date.AddDays(i);
            if (day.DayOfWeek != a && day.DayOfWeek != b)
            {
                continue;
            }

            var at = new DateTimeOffset(day.Year, day.Month, day.Day, h, m, 0, Kst);
            if (at >= now)
            {
                targetMs = at.ToUnixTimeMilliseconds();
                return true;
            }
        }

        return false;
    }
}

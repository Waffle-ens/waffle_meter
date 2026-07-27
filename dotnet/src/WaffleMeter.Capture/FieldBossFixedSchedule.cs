namespace WaffleMeter.Capture;

/// <summary>
/// Fixed spawn schedules for the 어비스(혼돈의 에레슈란타) field bosses. Unlike the open-world regions —
/// where every boss has its own per-kill respawn — most abyss entries spawn on a fixed content schedule
/// (요새 공성 시간표): 감시자 카이라 hourly on the hour, one group 금·일 22:05, another 수·토 22:35, all KST.
/// Two things use this: the picker shows the schedule beside the boss (which also tells apart the rows that
/// share a mob name), and a record that arrives with no usable time still gets its next occurrence.
/// <para>Pure and side-effect free, and only consulted as a fallback — a server-sent time always wins. In
/// every capture so far the server DID send real times for all 13 abyss bosses, so the fallback is a safety
/// net rather than the normal path.</para>
/// <para>⚠️ 그룹 배정과 시각은 실캡처(2026-07-27 하층+중층)에서 읽었지만, 와이어엔 <b>다음 1회</b>만 오므로
/// 각 쌍의 두 번째 요일(일/토)은 추정이다. 콘텐츠 일정이라 패치로 바뀔 수 있다.</para>
/// </summary>
public static class FieldBossFixedSchedule
{
    private static readonly TimeSpan Kst = TimeSpan.FromHours(9);

    private enum Kind
    {
        /// <summary>Every hour, on the hour.</summary>
        Hourly,

        /// <summary>금·일 22:05.</summary>
        FriSun2205,

        /// <summary>수·토 22:35.</summary>
        WedSat2235,
    }

    // Which boss is on which schedule was read off a real 하층+중층 capture (2026-07-27, a Monday): the
    // FriSun group's next spawn came back as Fri 22:05 and the WedSat group's as Wed 22:35. That fixes the
    // group each boss belongs to and the time of day; the SECOND day of each pair is inferred, since only
    // the next occurrence is on the wire.
    private static readonly Dictionary<int, Kind> ByBossCode = new()
    {
        // 감시자 카이라 (하층). 이 한 줄만 서버가 타임스탬프를 통째로 0으로 보낸다(클린 캡처 전부에서 동일).
        // 즉 이 보스의 시각은 우리 추정이고 서버가 덮어써 줄 일이 없다 — 그래서 표시도 "추정"으로 단다.
        [2600089] = Kind.Hourly,

        [2600084] = Kind.FriSun2205,   // 수호신장 나흐마 ×3 (하층)
        [2600093] = Kind.FriSun2205,
        [2600094] = Kind.FriSun2205,
        [2600150] = Kind.FriSun2205,   // 분노한 수호신장 나흐마 (중층)
        [2600520] = Kind.FriSun2205,   // 처형관 드라모스 (중층)

        [2600096] = Kind.WedSat2235,   // 집행자 타마사 (하층)
        [2600097] = Kind.WedSat2235,   // 정령왕 아그로 (하층, 집행자 슬롯)
        [2600098] = Kind.WedSat2235,   // 감시자 카이라 (하층, 집행자 슬롯)
        [2600156] = Kind.WedSat2235,   // 분노한 수호신장 나흐마 (중층)
        [2600521] = Kind.WedSat2235,   // 반역자 듀칼 (중층)
        [2600522] = Kind.WedSat2235,   // 파멸자 마라카 (중층)
    };

    /// <summary>True when this boss spawns on a fixed schedule rather than a per-kill respawn timer.</summary>
    public static bool HasFixedSchedule(int bossCode) => ByBossCode.ContainsKey(bossCode);

    /// <summary>A short human label for the schedule, for the picker row ("매시 정각" / "금·일 22:00" /
    /// "수·토 22:30"), or null when the boss uses a normal respawn timer.</summary>
    public static string? Describe(int bossCode) => ByBossCode.TryGetValue(bossCode, out Kind k)
        ? k switch
        {
            Kind.Hourly => "매시 정각(추정)",
            Kind.FriSun2205 => "금·일 22:05",
            Kind.WedSat2235 => "수·토 22:35",
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

        (DayOfWeek a, DayOfWeek b, int h, int m) = kind == Kind.FriSun2205
            ? (DayOfWeek.Friday, DayOfWeek.Sunday, 22, 5)
            : (DayOfWeek.Wednesday, DayOfWeek.Saturday, 22, 35);

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

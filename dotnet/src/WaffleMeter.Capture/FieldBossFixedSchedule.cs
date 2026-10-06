namespace WaffleMeter.Capture;

/// <summary>
/// Fixed spawn schedules for the 어비스(혼돈의 에레슈란타) fortress bosses. Unlike the open-world regions —
/// where every boss has its own per-kill respawn — these spawn on a fixed content schedule, in two groups:
/// 금·일 22:05 KST (수호신장 나흐마 계열), and the 수·토 group that appears <b>after that server's 아티팩트
/// 점령전</b>. Two things use this: the picker shows the schedule beside the boss (which also tells apart the
/// rows that share a mob name), and a 금·일 record that arrives with no usable time still gets its next
/// occurrence.
/// <para>Pure and side-effect free, and only consulted as a fallback — a server-sent time always wins. In
/// every capture so far the server DID send real times for these bosses, so the fallback is a safety net
/// rather than the normal path.</para>
/// <para><b>⚠️ The 수·토 group has no clock here any more (2026-10-07).</b> It used to be a fixed 수·토 22:35.
/// Since the 09-30 patch the war starts at 21:20, 21:50 or 22:20 depending on the server group, and these six
/// bosses (wire slots 2006/2007/2008 하층, 2203/2204/2205 중층 — the ones whose reward reads "아티팩트 점령전 이후에
/// 나타난 강력한 몬스터") follow it: the live client's <c>PeriodSpawn</c> moved them 22:30 → 21:45 in the same patch
/// that moved the war 22:00 → 21:20. One clock is now wrong for two groups out of three, and for a boss that is
/// already up (its record carries a past time, so it lands here) it could invent a same-day 22:35 ghost alarm.
/// The time depends on the server's own war start, which
/// only App.Core can see (<c>ArtifactWarSchedule</c>, from the 0xE305/0xE307 window) — so <see cref="TryNextSpawn"/>
/// refuses this group and the Capture layer never makes a time up for it.</para>
/// <para>그룹 배정은 실캡처(2026-07-27 하층+중층)에서 읽었고, 두 번째 요일(금·일의 일, 수·토의 토)은 클라
/// <c>PeriodSpawn</c> 요일 플래그로 확인됐다. 콘텐츠 일정이라 패치로 바뀔 수 있다.</para>
/// </summary>
public static class FieldBossFixedSchedule
{
    private static readonly TimeSpan Kst = TimeSpan.FromHours(9);

    private enum Kind
    {
        /// <summary>금·일 22:05.</summary>
        FriSun2205,

        /// <summary>수·토, 그 서버의 아티쟁 뒤 — 시각은 여기서 나오지 않는다(<c>ArtifactWarSchedule</c>).
        /// 이름은 고정 22:35 시절의 것이다.</summary>
        WedSat2235,
    }

    // Which boss is in which group was read off a real 하층+중층 capture (2026-07-27, a Monday): the
    // FriSun group's next spawn came back as Fri 22:05 and the WedSat group's as Wed 22:35 (the pre-patch
    // war time). That fixes the group each boss belongs to; only the 금·일 time of day is still used.
    private static readonly Dictionary<int, Kind> ByBossCode = new()
    {
        // 감시자 카이라(2600089)는 여기 없다 — 서버가 시각을 0으로 보내는 유일한 보스라 리젠 타이머로
        // 다룰 수가 없고, KST 1시 기준 4시간 격자 별도 알림(KairaAlarm)으로 뺐다.
        // ⚠️ 확정 출현이 됐다고 여기로 옮기지 마라 — FieldBossCatalog.ScheduledSpawnCode 의 doc 참고.
        [2600084] = Kind.FriSun2205,   // 수호신장 나흐마 ×3 (하층)
        [2600093] = Kind.FriSun2205,
        [2600094] = Kind.FriSun2205,
        [2600150] = Kind.FriSun2205,   // 분노한 수호신장 나흐마 (중층)
        [2600520] = Kind.FriSun2205,   // 처형관 드라모스 (중층)

        [2600096] = Kind.WedSat2235,   // 집행자 타마사 (하층)
        [2600097] = Kind.WedSat2235,   // 정령왕 아그로 (하층, 집행자 슬롯)
        [2600098] = Kind.WedSat2235,   // 집행자 카이라 (하층, 집행자 슬롯)
        [2600156] = Kind.WedSat2235,   // 분노한 수호신장 나흐마 (중층)
        [2600521] = Kind.WedSat2235,   // 반역자 듀칼 (중층)
        [2600522] = Kind.WedSat2235,   // 파멸자 마라카 (중층)
    };

    /// <summary>The bosses that spawn after the server's 아티팩트 점령전, as the meter maps them — wire slots
    /// 2006/2007/2008 and 2203/2204/2205 exactly, in table order.
    /// <para>⚠️ Declared AFTER <see cref="ByBossCode"/> on purpose: static initialisers run in source order, and
    /// moving this above it would read a null dictionary on first use — from the alarm's once-a-second poll.</para></summary>
    public static IReadOnlyList<int> ArtifactWarTiedCodes { get; } =
        ByBossCode.Where(kv => kv.Value == Kind.WedSat2235).Select(kv => kv.Key).ToArray();

    /// <summary>True when this boss spawns on a fixed schedule rather than a per-kill respawn timer.</summary>
    public static bool HasFixedSchedule(int bossCode) => ByBossCode.ContainsKey(bossCode);

    /// <summary>True for the 수·토 group whose spawn follows the server's own 아티팩트 점령전 time.</summary>
    public static bool IsArtifactWarTied(int bossCode) =>
        ByBossCode.TryGetValue(bossCode, out Kind k) && k == Kind.WedSat2235;

    /// <summary>A short human label for the schedule, for the picker row ("금·일 22:05" / "수·토 아티쟁 종료 후"),
    /// or null when the boss uses a normal respawn timer. The 수·토 label names no time on purpose: the time is
    /// per server (its war group), and the picker has no server to name it for.</summary>
    public static string? Describe(int bossCode) => ByBossCode.TryGetValue(bossCode, out Kind k)
        ? k switch
        {
            Kind.FriSun2205 => "금·일 22:05",
            Kind.WedSat2235 => "수·토 아티쟁 종료 후",
            _ => null,
        }
        : null;

    /// <summary>Next spawn at or after <paramref name="fromMs"/> (Unix ms) for a 금·일 boss. False for the
    /// 아티쟁 group (<see cref="IsArtifactWarTied"/>) — its time depends on the server's war start, which this
    /// layer cannot see, and a guessed time is worse than none (see the class doc).</summary>
    public static bool TryNextSpawn(int bossCode, long fromMs, out long targetMs)
    {
        targetMs = 0;
        if (!ByBossCode.TryGetValue(bossCode, out Kind kind) || kind != Kind.FriSun2205)
        {
            return false;
        }

        DateTimeOffset now = DateTimeOffset.FromUnixTimeMilliseconds(fromMs).ToOffset(Kst);
        (DayOfWeek a, DayOfWeek b, int h, int m) = (DayOfWeek.Friday, DayOfWeek.Sunday, 22, 5);

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

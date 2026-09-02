using WaffleMeter.App.Core;
using WaffleMeter.Capture;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

/// <summary>Spec for the pure field-boss reminder schedule (<see cref="FieldBossAlarm"/>) and the boss
/// catalog it reads names and regions from.</summary>
public class FieldBossAlarmTests
{
    private const long Now = 1_783_000_000_000L;

    [Fact]
    public void Catalog_lists_every_boss_split_by_region()
    {
        var all = FieldBossCatalog.All();
        Assert.Equal(85, all.Count);
        Assert.Equal(24, FieldBossCatalog.InRegion(FieldBossRegion.Verteron).Count);
        Assert.Equal(24, FieldBossCatalog.InRegion(FieldBossRegion.Altgard).Count);
        Assert.Equal(12, FieldBossCatalog.InRegion(FieldBossRegion.Eltnen).Count);
        Assert.Equal(12, FieldBossCatalog.InRegion(FieldBossRegion.Morheim).Count);
        Assert.Equal(13, FieldBossCatalog.InRegion(FieldBossRegion.Abyss).Count);
        Assert.All(all, b => Assert.False(string.IsNullOrWhiteSpace(b.Name)));
        Assert.Equal(all.Count, all.Select(b => b.Code).Distinct().Count());
        Assert.Equal(all.Count, all.Select(b => b.WireCode).Distinct().Count());
    }

    [Fact]
    public void Every_region_maps_to_the_world_map_ids_the_broadcast_carries()
    {
        Assert.True(FieldBossCatalog.TryResolveRegionForMap(1010, out FieldBossRegion verteron));
        Assert.Equal(FieldBossRegion.Verteron, verteron);
        Assert.True(FieldBossCatalog.TryResolveRegionForMap(1011, out FieldBossRegion eltnen));
        Assert.Equal(FieldBossRegion.Eltnen, eltnen);
        Assert.True(FieldBossCatalog.TryResolveRegionForMap(1110, out FieldBossRegion altgard));
        Assert.Equal(FieldBossRegion.Altgard, altgard);
        Assert.True(FieldBossCatalog.TryResolveRegionForMap(1111, out FieldBossRegion morheim));
        Assert.Equal(FieldBossRegion.Morheim, morheim);
        Assert.True(FieldBossCatalog.TryResolveRegionForMap(FieldBossCatalog.AbyssLowerMapId, out FieldBossRegion low));
        Assert.Equal(FieldBossRegion.Abyss, low);
        Assert.True(FieldBossCatalog.TryResolveRegionForMap(FieldBossCatalog.AbyssMiddleMapId, out FieldBossRegion mid));
        Assert.Equal(FieldBossRegion.Abyss, mid);

        Assert.False(FieldBossCatalog.TryResolveRegionForMap(9999, out _));
    }

    [Fact]
    public void Wire_codes_resolve_slot_codes_mob_codes_and_the_legacy_alias()
    {
        // 베르테론/알트가르드/어비스 ride a per-map slot code…
        Assert.True(FieldBossCatalog.TryResolveWireCode(101002, out int kutar));
        Assert.Equal(2100040, kutar);
        Assert.True(FieldBossCatalog.TryResolveWireCode(111001, out int danar));
        Assert.Equal(2400017, danar);
        Assert.True(FieldBossCatalog.TryResolveWireCode(2002, out int kaira));
        Assert.Equal(2600089, kaira);

        // …엘테넨/모르헤임 carry the mob code itself.
        Assert.True(FieldBossCatalog.TryResolveWireCode(2406034, out int pargon));
        Assert.Equal(2406034, pargon);

        // the pre-datamine 니호그 code still resolves to the corrected one
        Assert.True(FieldBossCatalog.TryResolveWireCode(2101349, out int nidhogg));
        Assert.Equal(2101343, nidhogg);

        Assert.False(FieldBossCatalog.TryResolveWireCode(424242, out _));
    }

    [Fact]
    public void Map_scoped_resolution_rejects_a_code_from_another_region()
    {
        Assert.True(FieldBossCatalog.TryResolveWireCode(101002, 1010, out int onItsOwnMap));
        Assert.Equal(2100040, onItsOwnMap);
        Assert.False(FieldBossCatalog.TryResolveWireCode(101002, 1111, out _)); // 베르테론 슬롯코드 in a 모르헤임 table
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
        Assert.Equal("경계의 방랑자 파르곤", FieldBossCatalog.Name(2406034));
        Assert.True(FieldBossCatalog.IsKnown(2406034));
        Assert.False(FieldBossCatalog.IsKnown(9999999));
        Assert.Contains("9999999", FieldBossCatalog.Name(9999999));
        Assert.Equal(FieldBossRegion.Morheim, FieldBossCatalog.Region(2406034));
        Assert.Null(FieldBossCatalog.Region(9999999));
    }

    [Fact]
    public void Fixed_schedule_only_covers_the_abyss_fortress_bosses()
    {
        Assert.True(FieldBossFixedSchedule.HasFixedSchedule(2600084));   // 수호신장 나흐마 — 요새 공성
        Assert.False(FieldBossFixedSchedule.HasFixedSchedule(2406034));  // 모르헤임은 일반 리스폰 타이머
        Assert.Equal("금·일 22:05", FieldBossFixedSchedule.Describe(2600520));   // 실캡처: 금 22:05
        Assert.Equal("수·토 22:35", FieldBossFixedSchedule.Describe(2600156));   // 실캡처: 수 22:35
        Assert.Null(FieldBossFixedSchedule.Describe(2406034));

        // 감시자 카이라는 리젠 타이머가 아니라 4시간 격자 출현 알림으로 다룬다 — 여기에도, picker에도 없다.
        Assert.False(FieldBossFixedSchedule.HasFixedSchedule(FieldBossCatalog.ScheduledSpawnCode));
        Assert.True(FieldBossCatalog.HasOwnAlarm(FieldBossCatalog.ScheduledSpawnCode));
        Assert.False(FieldBossCatalog.HasOwnAlarm(2600098));   // 집행자 슬롯 카이라는 일반 알림 대상
    }

    private static readonly TimeSpan Kst = TimeSpan.FromHours(9);

    /// <summary>KST 벽시계 시각을 Unix ms 로. 테스트가 실행 머신 시간대에 좌우되면 안 되므로 오프셋을
    /// 명시한다 — 이 저장소는 CI 에서 테스트를 돌리지 않아 그런 드리프트를 아무도 못 잡는다.</summary>
    private static long KstMs(int h, int m, int day = 2) =>
        new DateTimeOffset(2026, 9, day, h, m, 0, Kst).ToUnixTimeMilliseconds();

    [Fact]
    public void Kaira_leads_are_due_only_before_a_four_hour_slot()
    {
        var leads = new[] { 10, 5, 1 };

        // 20시는 출현 슬롯 — 리드가 맞는 분에만 뜬다.
        Assert.Equal(10, KairaAlarm.DueLead(KstMs(19, 50), leads));
        Assert.Equal(5, KairaAlarm.DueLead(KstMs(19, 55), leads));
        Assert.Equal(1, KairaAlarm.DueLead(KstMs(19, 59), leads));
        Assert.Null(KairaAlarm.DueLead(KstMs(19, 52), leads));   // 리드에 없는 분
        Assert.Null(KairaAlarm.DueLead(KstMs(20, 0), leads));    // 출현 정각 자체는 0 lead → 켜진 리드가 없다

        // 21시는 슬롯이 아니다 — 옛 '매시 정각' 구현이라면 여기서 울렸다. 이게 이번 패치의 핵심 회귀 그물이다.
        Assert.Null(KairaAlarm.DueLead(KstMs(20, 50), leads));
        Assert.Null(KairaAlarm.DueLead(KstMs(20, 55), leads));
        Assert.Null(KairaAlarm.DueLead(KstMs(20, 59), leads));
    }

    [Fact]
    public void Kaira_slots_are_midnight_plus_every_four_hours()
    {
        var leads = new[] { 10 };
        int[] spawnHours = { 0, 4, 8, 12, 16, 20 };

        for (int hour = 0; hour < 24; hour++)
        {
            // 그 시각 정각의 10분 전 = (hour-1):50. 0시의 10분 전은 전날 23:50 이다.
            long tenBefore = KstMs(hour == 0 ? 23 : hour - 1, 50, day: hour == 0 ? 1 : 2);
            int? due = KairaAlarm.DueLead(tenBefore, leads);
            if (spawnHours.Contains(hour))
            {
                Assert.Equal(10, due);
            }
            else
            {
                Assert.Null(due);
            }
        }
    }

    /// <summary>23:50 → 익일 00:00. 격자를 시(hour) 나눗셈으로 재면 자정에서 끊기기 쉬운 자리다
    /// (1440 % 240 == 0 이라 실제로는 끊기지 않는다는 것을 못박는다).</summary>
    [Fact]
    public void The_midnight_slot_rolls_over_from_the_previous_day()
    {
        var leads = new[] { 10, 5, 1 };
        Assert.Equal(10, KairaAlarm.DueLead(KstMs(23, 50, day: 1), leads));
        Assert.Equal(1, KairaAlarm.DueLead(KstMs(23, 59, day: 1), leads));

        long spawn = KairaAlarm.NextSpawnMs(KstMs(23, 50, day: 1));
        Assert.Equal(new DateTimeOffset(2026, 9, 2, 0, 0, 0, Kst).ToUnixTimeMilliseconds(), spawn);
    }

    /// <summary>격자는 머신 시간대가 아니라 서버(KST)에 걸려 있다. 로컬 시로 재면 UTC+8 사용자는 여섯
    /// 슬롯이 전부 한 시간 어긋난다 — timeBasis 결정 전체를 지키는 그물이다.</summary>
    [Fact]
    public void The_grid_is_anchored_to_kst_not_to_the_machine_timezone()
    {
        var leads = new[] { 10 };

        // 같은 순간을 UTC+8 벽시계로 쓰면 19:50 이 아니라 18:50 이다. 그래도 KST 19:50 이므로 떠야 한다.
        long sameInstantFromPlus8 =
            new DateTimeOffset(2026, 9, 2, 18, 50, 0, TimeSpan.FromHours(8)).ToUnixTimeMilliseconds();
        Assert.Equal(KstMs(19, 50), sameInstantFromPlus8);
        Assert.Equal(10, KairaAlarm.DueLead(sameInstantFromPlus8, leads));

        // 반대로 UTC+8 사용자의 로컬 19:50 은 KST 20:50 이라 슬롯이 아니다.
        long localEveningInPlus8 =
            new DateTimeOffset(2026, 9, 2, 19, 50, 0, TimeSpan.FromHours(8)).ToUnixTimeMilliseconds();
        Assert.Null(KairaAlarm.DueLead(localEveningInPlus8, leads));
    }

    /// <summary>하루치를 분 단위로 훑어 두 스케줄이 실제로 갈렸음을 수치로 고정한다. 슈고는 매시 정각
    /// 그대로(24슬롯 × 3리드 = 72회), 카이라는 4시간 격자(6슬롯 × 3리드 = 18회).</summary>
    [Fact]
    public void A_full_day_gives_kaira_eighteen_cues_and_shugo_seventy_two()
    {
        var leads = new[] { 10, 5, 1 };
        int kaira = 0, shugo = 0;

        for (int minute = 0; minute < 24 * 60; minute++)
        {
            long ms = KstMs(0, 0) + (minute * 60_000L);
            if (KairaAlarm.DueLead(ms, leads) is not null)
            {
                kaira++;
            }

            // 슈고는 사용자 벽시계 기준이므로 같은 분을 KST 벽시계로 그대로 넘긴다.
            DateTime wall = new DateTimeOffset(2026, 9, 2, 0, 0, 0, Kst).AddMinutes(minute).DateTime;
            if (ShugoAlarm.DueLead(wall, leads) is not null)
            {
                shugo++;
            }
        }

        Assert.Equal(18, kaira);
        Assert.Equal(72, shugo);
    }

    /// <summary>토스트가 찍는 "· HH:mm" 의 근거. DueLead 와 같은 격자를 공유해야 한다.</summary>
    [Fact]
    public void The_next_spawn_is_the_slot_the_lead_is_counting_down_to()
    {
        long spawn20 = new DateTimeOffset(2026, 9, 2, 20, 0, 0, Kst).ToUnixTimeMilliseconds();
        Assert.Equal(spawn20, KairaAlarm.NextSpawnMs(KstMs(19, 50)));
        Assert.Equal(spawn20, KairaAlarm.NextSpawnMs(KstMs(19, 59)));
        Assert.Equal(spawn20, KairaAlarm.NextSpawnMs(KstMs(16, 1)));   // 16시 슬롯 직후 → 다음은 20시
        Assert.Equal(spawn20, KairaAlarm.NextSpawnMs(KstMs(20, 0)));   // 정각 자신
    }

    [Fact]
    public void Weekly_schedule_returns_the_next_matching_day_and_time()
    {
        // 2026-07-27 is a Monday → the next 수·토 22:35 is Wednesday the 29th. This is the value the real
        // 2026-07-27 어비스 capture carried for the 수·토 group, so the schedule reproduces the wire.
        long monday = new DateTimeOffset(2026, 7, 27, 9, 0, 0, TimeSpan.FromHours(9)).ToUnixTimeMilliseconds();
        Assert.True(FieldBossFixedSchedule.TryNextSpawn(2600521, monday, out long next));
        Assert.Equal(new DateTimeOffset(2026, 7, 29, 22, 35, 0, TimeSpan.FromHours(9)).ToUnixTimeMilliseconds(), next);

        // …and the 금·일 group's next spawn from that same Monday was Friday the 31st.
        Assert.True(FieldBossFixedSchedule.TryNextSpawn(2600520, monday, out long friday));
        Assert.Equal(new DateTimeOffset(2026, 7, 31, 22, 5, 0, TimeSpan.FromHours(9)).ToUnixTimeMilliseconds(), friday);

        // Same day but past the time → rolls to the pair's other day.
        long wedLate = new DateTimeOffset(2026, 7, 29, 23, 0, 0, TimeSpan.FromHours(9)).ToUnixTimeMilliseconds();
        Assert.True(FieldBossFixedSchedule.TryNextSpawn(2600521, wedLate, out long after));
        Assert.Equal(new DateTimeOffset(2026, 8, 1, 22, 35, 0, TimeSpan.FromHours(9)).ToUnixTimeMilliseconds(), after);
    }
}

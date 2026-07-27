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
        Assert.True(FieldBossFixedSchedule.HasFixedSchedule(2600089));   // 감시자 카이라 — 매시 정각
        Assert.False(FieldBossFixedSchedule.HasFixedSchedule(2406034));  // 모르헤임은 일반 리스폰 타이머
        // 카이라만 서버가 시각을 0으로 보내 우리 추정에 의존한다 — 표시로 드러난다.
        Assert.Equal("매시 정각(추정)", FieldBossFixedSchedule.Describe(2600089));
        Assert.Equal("금·일 22:05", FieldBossFixedSchedule.Describe(2600520));   // 실캡처: 금 22:05
        Assert.Equal("수·토 22:35", FieldBossFixedSchedule.Describe(2600156));   // 실캡처: 수 22:35
        Assert.Null(FieldBossFixedSchedule.Describe(2406034));
    }

    [Fact]
    public void Hourly_schedule_returns_the_next_hour_boundary()
    {
        // 2026-07-27 13:37:10 KST
        long from = new DateTimeOffset(2026, 7, 27, 13, 37, 10, TimeSpan.FromHours(9)).ToUnixTimeMilliseconds();
        Assert.True(FieldBossFixedSchedule.TryNextSpawn(2600089, from, out long next));
        Assert.Equal(new DateTimeOffset(2026, 7, 27, 14, 0, 0, TimeSpan.FromHours(9)).ToUnixTimeMilliseconds(), next);
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

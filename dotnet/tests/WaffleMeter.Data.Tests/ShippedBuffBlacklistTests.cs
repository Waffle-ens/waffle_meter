using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>
/// 출하 <c>buff_blacklist.json</c>의 두 목록.
/// <para><c>"blacklist"</c>는 어디서나 지운다 — 본인 오버레이·음성뿐 아니라 상세 가동률(보스 탭 포함), DPS 그래프
/// 레인, 업로드 BossDebuffs 까지. 그래서 보스 쪽에서도 의미 없는 적 대상 표식·CC 만 넣는다.</para>
/// <para><c>"overlayOnly"</c>는 본인 오버레이·음성·셀프 스테이징에서만 뺀다. 징벌·타박상·지연 피해처럼 보스에게
/// 의미 있는 디버프가 본인 버프 base 로 접혀 '버프 온'으로 오점등되는 것만 막고, 보스 통계는 그대로 둔다.</para>
/// </summary>
public sealed class ShippedBuffBlacklistTests
{
    private const int Me = 7;
    private const int Boss = 500;

    // 2026-11-11 패치 분석에서 넣은 A군(보스 쪽 의미 없음).
    private static readonly int[] AddedBlacklist =
    [
        111900501, 122200001,
        190700001, 190700002, 190700003, 190700004, 190800002, 190900301, 190900302, 190900311, 192700201,
    ];

    // B군(보스 디버프로 의미 있음) — 오버레이에서만 뺀다.
    private static readonly int[] OverlayOnly =
    [
        111900171, 113900001, 113900201, 113900501,
        120900201, 120900202, 120900203, 120900204, 120900211, 120900212, 120900213, 120900214,
        120900221, 120900222, 120900223, 120900224, 120900231, 120900232, 120900233, 120900234,
        163000008,
        190700201, 190700202,
    ];

    // 같은 base 로 접히는 정당한 본인 버프 — 어느 목록에도 들어가면 안 된다.
    private static readonly int[] OwnBuffs =
    [
        190900001, // 투기
        120900001, // 집행자
        113900081, 113900481, // 격노
        111900471, // 도약 찍기(본인)
        111000071, // 광분
    ];

    private static string BlacklistPath()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "Assets", "json", "buff_blacklist.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("buff_blacklist.json not found above " + AppContext.BaseDirectory);
    }

    private static DataManager Shipped()
    {
        string path = BlacklistPath();
        var dm = new DataManager();
        dm.LoadBuffs(ReferenceJson.LoadBuffs(Path.Combine(Path.GetDirectoryName(path)!, "buff.json")));
        dm.LoadBuffBlacklist(ReferenceJson.LoadBuffBlacklist(path));
        dm.LoadBuffOverlayExclude(ReferenceJson.LoadBuffOverlayExclude(path));
        return dm;
    }

    private static DataManager ShippedWithSelf()
    {
        DataManager dm = Shipped();
        dm.SaveNickname(Me, "본인", isExecutor: true, server: 3, jobByte: 0);
        return dm;
    }

    [Fact]
    public void The_shipped_lists_hold_the_patch_entries()
    {
        string path = BlacklistPath();
        HashSet<int> blacklist = ReferenceJson.LoadBuffBlacklist(path).ToHashSet();
        HashSet<int> overlayOnly = ReferenceJson.LoadBuffOverlayExclude(path).ToHashSet();

        Assert.Subset(blacklist, AddedBlacklist.ToHashSet());
        Assert.Equal(OverlayOnly.OrderBy(c => c), overlayOnly.OrderBy(c => c));
    }

    [Fact]
    public void The_two_lists_do_not_overlap()
    {
        string path = BlacklistPath();
        HashSet<int> blacklist = ReferenceJson.LoadBuffBlacklist(path).ToHashSet();
        List<int> overlayOnly = ReferenceJson.LoadBuffOverlayExclude(path);

        Assert.DoesNotContain(overlayOnly, c => blacklist.Contains(c));
        Assert.Equal(overlayOnly.Count, overlayOnly.Distinct().Count());
    }

    [Fact]
    public void No_own_buff_is_on_either_list()
    {
        string path = BlacklistPath();
        HashSet<int> all = ReferenceJson.LoadBuffBlacklist(path)
            .Concat(ReferenceJson.LoadBuffOverlayExclude(path))
            .ToHashSet();

        Assert.DoesNotContain(OwnBuffs, c => all.Contains(c));
    }

    [Fact]
    public void A_pull_debuff_landing_on_self_does_not_light_up_투기()
    {
        long t = 1_000_000;
        DataManager dm = ShippedWithSelf();

        dm.SaveUseBuff(Me, 190900311, t, t + 100, 100, actorId: Me);
        Assert.Empty(dm.ActiveOwnerBuffs(t));

        dm.SaveUseBuff(Me, 190900001, t, t + 10_000, 10_000, actorId: Me);
        OwnerBuffView b = Assert.Single(dm.ActiveOwnerBuffs(t));
        Assert.Equal(19090000, b.Code);
    }

    [Theory]
    [InlineData(120900201, 12090000)] // 징벌 → 집행자 slot
    [InlineData(113900001, 11390000)] // 타박상 → 격노 폭발 slot
    [InlineData(163000008, 16300000)] // 지연 피해 → 원소 융합 slot
    public void An_overlay_only_debuff_stays_off_the_self_overlay(int code, int baseCode)
    {
        long t = 1_000_000;
        DataManager dm = ShippedWithSelf();

        dm.SaveUseBuff(Me, code, t, t + 3_000, 3_000, actorId: Me);

        Assert.DoesNotContain(dm.ActiveOwnerBuffs(t), b => b.Code == baseCode);
        Assert.True(dm.IsBuffOverlayExcluded(code));
        Assert.False(dm.IsBuffBlacklisted(code));
    }

    [Fact]
    public void An_overlay_only_debuff_keeps_its_boss_uptime_row()
    {
        DataManager dm = ShippedWithSelf();

        dm.SaveUseBuff(Boss, 120900201, 0, 500, 500, actorId: Me);

        OperatingData row = Assert.Single(new DpsCalculator(dm).GetBuffOperatingRate(Boss, 0, 1000));
        Assert.Equal("징벌", row.Name);
        Assert.Equal(50.0, row.OperatingRate, 3);
    }

    [Fact]
    public void A_blacklisted_mark_is_gone_from_the_boss_uptime_too()
    {
        // 대조군: "blacklist"는 보스 탭에서도 지운다 — 그래서 의미 없는 표식만 넣는다.
        DataManager dm = ShippedWithSelf();

        dm.SaveUseBuff(Boss, 190700001, 0, 500, 500, actorId: Me);

        Assert.Empty(new DpsCalculator(dm).GetBuffOperatingRate(Boss, 0, 1000));
    }

    [Fact]
    public void An_overlay_only_debuff_staged_before_recognition_is_not_replayed()
    {
        long t = 1_000_000;
        DataManager dm = Shipped();
        dm.Clock = () => t;

        // executor 미확정(0)에 도착 → 스테이징 경로. 본인 버프 하나는 대조군으로 같이 둔다.
        dm.SaveUseBuff(Me, 120900201, t, t + 3_000, 3_000, actorId: Me);
        dm.SaveUseBuff(Me, 190900001, t, t + 10_000, 10_000, actorId: Me);
        Assert.Empty(dm.ActiveOwnerBuffs(t));

        dm.SaveNickname(Me, "본인", isExecutor: true, server: 3, jobByte: 0);

        OwnerBuffView b = Assert.Single(dm.ActiveOwnerBuffs(t));
        Assert.Equal(19090000, b.Code);
    }

    [Fact]
    public void A_file_without_the_new_key_reads_as_an_empty_overlay_list()
    {
        string path = Path.Combine(Path.GetTempPath(), $"bl-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{ \"blacklist\": [111100002] }");
            Assert.Empty(ReferenceJson.LoadBuffOverlayExclude(path));
            Assert.Equal(new[] { 111100002 }, ReferenceJson.LoadBuffBlacklist(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void The_blacklist_reader_ignores_the_overlay_key()
    {
        string path = Path.Combine(Path.GetTempPath(), $"bl-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{ \"blacklist\": [111100002], \"overlayOnly\": [120900201] }");
            Assert.Equal(new[] { 111100002 }, ReferenceJson.LoadBuffBlacklist(path));
            Assert.Equal(new[] { 120900201 }, ReferenceJson.LoadBuffOverlayExclude(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}

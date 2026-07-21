using WaffleMeter.App.Core;
using WaffleMeter.Data;
using WaffleMeter.Services;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

/// <summary>
/// "전투 중 간헐적으로 한 명씩 안 보인다"에 대한 세 갈래 보강.
/// <para>P1 — 로스터 복구가 1:1일 때만 동작해서, 실측 지배 사례인 "동시에 2~3명 무명"에서는 아무도 복구되지
/// 않았다. 직업이 양쪽에서 유일하게 대응되면 이름을 붙인다(모호하면 붙이지 않는다).</para>
/// <para>P2 — 표시 상한이 정원과 같으면 여유가 0이라 낯선 사람 한 줄에 파티원이 잘린다. 확인된 파티원과
/// 본인은 상한에서 제외한다.</para>
/// <para>P3 — 재사용된 uid가 이전 점유자 닉네임을 들고 있으면 파티원의 딜이 낯선 이름으로 표시되고 정작
/// 그 파티원은 사라진다. 확정 로스터에 없는 이름은 STALE로 보고 복구 후보에 넣는다.</para>
/// </summary>
public sealed class MissingRowRecoveryTests
{
    private static User Member(int uid, string nick, JobClass? job = null) =>
        new(uid, nick, 2003) { Job = job };

    private static DpsReport Report(params (int Uid, string? Nick, JobClass? Job, double Amount)[] rows)
    {
        var report = new DpsReport();
        foreach ((int uid, string? nick, JobClass? job, double amount) in rows)
        {
            report.Contributors.Add(new User(uid, nick, nick == null ? -1 : 2003) { Job = job });
            report.Information[uid] = new DpsInformation(amount, amount, amount, amount);
        }

        return report;
    }

    private static IReadOnlyList<OverlayRowBuilder.Row> Build(
        DpsReport report, IReadOnlyList<User>? party, int topN = 10) =>
        OverlayRowBuilder.Build(
            report, roster: [], liveSelfId: 0, useTotalDamage: true, showPreCombatRoster: false,
            out _, topN: topN, authoritativeParty: party);

    private static string?[] Names(IReadOnlyList<OverlayRowBuilder.Row> rows) =>
        rows.Select(r => r.User?.Nickname).ToArray();

    // ---- P1: 동시에 여러 명이 무명일 때 직업으로 복구 ----

    [Fact]
    public void Two_bare_dealers_are_named_when_their_jobs_match_the_roster_uniquely()
    {
        // 종전에는 1:1이 아니라는 이유로 둘 다 숨겨졌다.
        DpsReport report = Report(
            (1, "아군", JobClass.CLERIC, 1000),
            (2, null, JobClass.GLADIATOR, 900),
            (3, null, JobClass.RANGER, 800));
        var party = new List<User>
        {
            Member(1, "아군", JobClass.CLERIC),
            Member(20, "검성친구", JobClass.GLADIATOR),
            Member(30, "궁성친구", JobClass.RANGER),
        };

        Assert.Equal(new[] { "아군", "검성친구", "궁성친구" }, Names(Build(report, party)));
    }

    [Fact]
    public void An_ambiguous_job_is_left_alone_rather_than_mislabelled()
    {
        // 같은 직업 무명이 둘이면 누가 누군지 알 수 없다 — 잘못된 이름을 붙이느니 그대로 둔다.
        DpsReport report = Report(
            (1, "아군", JobClass.CLERIC, 1000),
            (2, null, JobClass.GLADIATOR, 900),
            (3, null, JobClass.GLADIATOR, 800));
        var party = new List<User>
        {
            Member(1, "아군", JobClass.CLERIC),
            Member(20, "검성A", JobClass.GLADIATOR),
            Member(30, "검성B", JobClass.GLADIATOR),
        };

        Assert.Equal(new[] { "아군" }, Names(Build(report, party))); // 무명 둘은 종전대로 숨김
    }

    [Fact]
    public void The_one_to_one_case_still_works()
    {
        DpsReport report = Report((1, "아군", JobClass.CLERIC, 1000), (2, null, null, 900));
        var party = new List<User> { Member(1, "아군", JobClass.CLERIC), Member(20, "혼자남은친구") };

        Assert.Equal(new[] { "아군", "혼자남은친구" }, Names(Build(report, party)));
    }

    [Fact]
    public void Without_a_confirmed_roster_nothing_is_recovered()
    {
        DpsReport report = Report((1, "아군", JobClass.CLERIC, 1000), (2, null, JobClass.RANGER, 900));

        Assert.Equal(new[] { "아군" }, Names(Build(report, party: null)));
    }

    // ---- P3: 재사용된 uid의 옛 이름(로스터에 없는 이름) ----

    [Fact]
    public void A_stale_name_from_a_reused_uid_is_reclaimed_for_the_real_party_member()
    {
        // uid 2는 로스터가 "궁성친구"로 지목한 멤버인데, 저장된 이름은 그 uid를 쓰던 이전 점유자의 것이다.
        // 로스터가 uid↔이름의 권위 있는 출처이므로 로스터 이름으로 교정한다.
        DpsReport report = Report(
            (1, "아군", JobClass.CLERIC, 1000),
            (2, "지나가던사람", JobClass.RANGER, 900));
        var party = new List<User> { Member(1, "아군", JobClass.CLERIC), Member(2, "궁성친구", JobClass.RANGER) };

        Assert.Equal(new[] { "아군", "궁성친구" }, Names(Build(report, party)));
    }

    [Fact]
    public void A_named_outsider_blocks_every_recovery()
    {
        // 낯선 사람이 이름을 달고 딜하고 있으면 공개 씬(필드보스)이다 — 아무것도 재라벨하지 않는다.
        DpsReport report = Report(
            (1, "아군", JobClass.CLERIC, 1000),
            (2, null, JobClass.RANGER, 900),
            (3, "생판남", JobClass.SORCERER, 850));
        var party = new List<User> { Member(1, "아군", JobClass.CLERIC), Member(30, "궁성친구", JobClass.RANGER) };

        Assert.Equal(new[] { "아군", "생판남" }, Names(Build(report, party))); // 무명은 종전대로 숨김
    }

    // ---- P2: 상한 여유 + 파티원 면제 ----

    [Fact]
    public void A_confirmed_party_member_is_never_cut_by_the_display_cap()
    {
        // 상한 2인데 파티원이 3명 — 낯선 사람 한 줄에 파티원이 밀려나면 안 된다.
        DpsReport report = Report(
            (1, "아군1", null, 1000), (2, "아군2", null, 900), (3, "아군3", null, 800));
        var party = new List<User> { Member(1, "아군1"), Member(2, "아군2"), Member(3, "아군3") };

        Assert.Equal(new[] { "아군1", "아군2", "아군3" }, Names(Build(report, party, topN: 2)));
    }

    [Fact]
    public void Non_party_rows_are_still_capped_and_order_is_preserved()
    {
        DpsReport report = Report(
            (9, "남1", null, 1000), (1, "아군", null, 900), (8, "남2", null, 800), (7, "남3", null, 700));
        var party = new List<User> { Member(1, "아군") };

        // 상한 2 → 상위 2행(남1, 아군)만, 그리고 파티원은 어차피 면제. 남2·남3은 잘린다. 순서는 유지.
        Assert.Equal(new[] { "남1", "아군" }, Names(Build(report, party, topN: 2)));
    }

    [Fact]
    public void The_display_cap_gives_one_and_a_half_times_the_chosen_row_count()
    {
        string temp = Path.Combine(Path.GetTempPath(), "wm_cap_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var s = new MeterSettings(new PropertyHandler(temp));
            s.MaxVisibleRows = 10;
            Assert.Equal(15, s.DisplayRowCap);
            s.MaxVisibleRows = 8;
            Assert.Equal(12, s.DisplayRowCap);
            s.MaxVisibleRows = 5;
            Assert.Equal(8, s.DisplayRowCap); // 7.5 → 올림
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch { }
        }
    }
}

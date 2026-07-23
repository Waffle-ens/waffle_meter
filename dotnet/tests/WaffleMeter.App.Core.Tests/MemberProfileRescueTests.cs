using WaffleMeter.App.Core;
using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

/// <summary>
/// 0x9200 멤버 프로필(uid↔이름을 한 레코드에 실음)로 무명 파티원 전투행을 직접 명명한다. 파티원의 타인 닉
/// 스냅샷(0x3645)이 유실되면 그 전투행은 무명이라 blank-row 필터가 통째로 숨긴다(딜은 누적 → 보이는 비중 합이
/// 100%가 안 됨) — "전투 중 한 명씩 사라진다"의 잔여 갈래다. 0x9702 로스터와 달리 0x9200은 엔티티 uid를
/// 직접 실어 오므로 직업 추측·1:1 모호성 없이 uid로 곧장 이름을 붙일 수 있고, 0x9702가 통째로 유실된 입장에서도
/// 동작한다. 0x9200은 파티/공대 스코프라 여기 uid가 있으면 곧 파티원 — 필드보스의 낯선 zerg는 포함되지 않는다.
/// </summary>
public sealed class MemberProfileRescueTests
{
    private const int Srv = 2003;

    private static DpsReport Report(params (int Uid, string? Nick, double Amount)[] rows)
    {
        var report = new DpsReport();
        foreach ((int uid, string? nick, double amount) in rows)
        {
            report.Contributors.Add(new User(uid, nick, nick == null ? -1 : Srv));
            report.Information[uid] = new DpsInformation(amount, amount, amount, amount);
        }

        return report;
    }

    private static IReadOnlyList<OverlayRowBuilder.Row> Build(
        DpsReport report,
        IReadOnlyList<(int Uid, string Nickname, int Server)>? memberProfiles,
        IReadOnlyList<User>? party = null) =>
        OverlayRowBuilder.Build(
            report, roster: [], liveSelfId: 100, useTotalDamage: true, showPreCombatRoster: false,
            out _, selfNickname: "본인", selfServer: Srv, selfJob: JobClass.CLERIC,
            authoritativeParty: party, memberProfiles: memberProfiles);

    private static string?[] Names(IReadOnlyList<OverlayRowBuilder.Row> rows) =>
        rows.Select(r => r.User?.Nickname).OrderBy(n => n).ToArray();

    [Fact]
    public void A_bare_party_member_is_dropped_without_a_member_profile()
    {
        // 회귀 베이스라인: 본인(100) + 무명 파티원(200, 0x3645 유실). 로스터/프로필 없음 → 무명 행은 숨는다.
        DpsReport report = Report((100, "본인", 1000), (200, null, 900));
        Assert.Equal(new[] { "본인" }, Names(Build(report, memberProfiles: null)));
    }

    [Fact]
    public void A_bare_party_member_is_named_by_its_member_profile_uid()
    {
        DpsReport report = Report((100, "본인", 1000), (200, null, 900));
        var profiles = new List<(int, string, int)> { (200, "엽록소", 1014) };
        Assert.Equal(new[] { "본인", "엽록소" }, Names(Build(report, profiles)));
    }

    [Fact]
    public void A_bare_non_member_stays_hidden()
    {
        // 무명 행 uid 999가 멤버 프로필에 없다(파티 밖) → 이름 안 붙고 그대로 숨는다.
        DpsReport report = Report((100, "본인", 1000), (999, null, 900));
        var profiles = new List<(int, string, int)> { (200, "엽록소", 1014) };
        Assert.Equal(new[] { "본인" }, Names(Build(report, profiles)));
    }

    [Fact]
    public void A_named_row_is_not_overwritten_by_a_member_profile()
    {
        // uid 200이 이미 '달잔'으로 명명돼 있으면 프로필의 다른 이름으로 덮지 않는다(무명 행만 대상).
        DpsReport report = Report((100, "본인", 1000), (200, "달잔", 900));
        var profiles = new List<(int, string, int)> { (200, "엽록소", 1014) };
        Assert.Equal(new[] { "달잔", "본인" }, Names(Build(report, profiles)));
    }

    [Fact]
    public void Named_members_survive_the_display_cap_even_when_not_in_the_0x9702_party()
    {
        // 0x9702가 통째로 유실돼 authoritativeParty가 비어 있어도, member-profile로 명명한 파티원이 표시 상한에
        // 잘려 도로 사라지면 안 된다(상한 면제). topN=1로 강제해도 명명된 파티원은 남는다.
        DpsReport report = Report((100, "본인", 1000), (200, null, 900), (300, null, 800));
        var profiles = new List<(int, string, int)> { (200, "엽록소", 1014), (300, "달잔", 1006) };
        IReadOnlyList<OverlayRowBuilder.Row> rows = OverlayRowBuilder.Build(
            report, roster: [], liveSelfId: 100, useTotalDamage: true, showPreCombatRoster: false,
            out _, topN: 1, selfNickname: "본인", selfServer: Srv, selfJob: JobClass.CLERIC,
            memberProfiles: profiles);
        Assert.Equal(new[] { "달잔", "본인", "엽록소" }, Names(rows));
    }
}

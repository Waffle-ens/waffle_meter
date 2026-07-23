using WaffleMeter.App.Core;
using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

/// <summary>
/// Feature 1 — 파티/공대 구성이 (닉네임+서버 기준) 바뀌면 직전(프리즌) 전투 대신 로스터 프리뷰를 다시 띄운다.
/// 같은 파티 리풀은 직전 전투를 유지하고, 기록 재생 경로(allowRosterResurface:false)는 라이브 로스터와 달라도
/// 절대 비우지 않으며, 라이브 로스터가 비면(파티 나감/TTL) 마지막 전투를 유지한다. 판정은 uid 불가지 —
/// (닉,서버) 신원집합만 본다.
/// </summary>
public sealed class RosterResurfaceTests
{
    // 저장/기록(프리즌) 리포트: Information + 얼린 ExecutorId + 그 전투의 0x9702 스냅샷(신원집합).
    private static DpsReport FrozenBattle((int Uid, string Nick, double Amount)[] rows, params (string Nick, int Server)[] snapshot)
    {
        var report = new DpsReport { ExecutorId = rows[0].Uid };
        foreach ((int uid, string nick, double amount) in rows)
        {
            report.Contributors.Add(new User(uid, nick, 2003));
            report.Information[uid] = new DpsInformation(amount, amount, amount, amount);
        }

        report.PartyIdentitiesSnapshot = snapshot
            .Select(m => new RosterMember { Nickname = m.Nick, Server = m.Server, Slot = 1 }).ToList();
        return report;
    }

    private static string?[] Names(IReadOnlyList<OverlayRowBuilder.Row> rows) =>
        rows.Select(r => r.User?.Nickname).ToArray();

    private static IReadOnlyList<OverlayRowBuilder.Row> Build(
        DpsReport report, IReadOnlyList<User> roster, IReadOnlyList<(string, int, int)> live, bool allow, out bool hasCombat) =>
        OverlayRowBuilder.Build(
            report, roster, liveSelfId: 0, useTotalDamage: true, showPreCombatRoster: true, out hasCombat,
            rosterIdentities: live, allowRosterResurface: allow);

    [Fact]
    public void Party_change_resurfaces_the_roster_preview_over_the_frozen_battle()
    {
        // 직전 전투 = 아군A/아군B. 지금 파티는 아군C/아군D로 재편(닉/서버 다름).
        DpsReport report = FrozenBattle([(1, "아군A", 1000), (2, "아군B", 900)], ("아군A", 2003), ("아군B", 2003));
        var roster = new List<User> { new(10, "아군C", 2003), new(11, "아군D", 2003) };
        var live = new List<(string, int, int)> { ("아군C", 2003, 1), ("아군D", 2003, 2) };

        IReadOnlyList<OverlayRowBuilder.Row> rows = Build(report, roster, live, allow: true, out bool hasCombat);

        Assert.False(hasCombat);                              // 전투 아님 = 로스터 프리뷰로 갈아끼움
        Assert.Equal(new[] { "아군C", "아군D" }, Names(rows));  // 직전 전투(아군A/B) 아님
    }

    [Fact]
    public void Same_party_repull_keeps_the_frozen_battle()
    {
        DpsReport report = FrozenBattle([(1, "아군A", 1000), (2, "아군B", 900)], ("아군A", 2003), ("아군B", 2003));
        var roster = new List<User> { new(1, "아군A", 2003), new(2, "아군B", 2003) };
        var live = new List<(string, int, int)> { ("아군A", 2003, 1), ("아군B", 2003, 2) }; // 동일 파티

        IReadOnlyList<OverlayRowBuilder.Row> rows = Build(report, roster, live, allow: true, out bool hasCombat);

        Assert.True(hasCombat);                               // 직전 전투 유지
        Assert.Equal(new[] { "아군A", "아군B" }, Names(rows));
    }

    [Fact]
    public void History_replay_never_resurfaces_even_when_the_live_party_differs()
    {
        DpsReport report = FrozenBattle([(1, "아군A", 1000), (2, "아군B", 900)], ("아군A", 2003), ("아군B", 2003));
        var roster = new List<User> { new(10, "아군C", 2003) };
        var live = new List<(string, int, int)> { ("아군C", 2003, 1) }; // 다른 파티지만 기록 재생 경로

        IReadOnlyList<OverlayRowBuilder.Row> rows = Build(report, roster, live, allow: false, out bool hasCombat);

        Assert.True(hasCombat);                               // 기록 재생은 절대 안 비움
        Assert.Equal(new[] { "아군A", "아군B" }, Names(rows));
    }

    [Fact]
    public void An_empty_live_roster_does_not_blank_the_last_battle()
    {
        // 파티 나감/TTL로 라이브 로스터가 비면(0x9702 무발화) currentSet 빈 값 가드 → 직전 전투 유지(town fail-safe).
        DpsReport report = FrozenBattle([(1, "아군A", 1000), (2, "아군B", 900)], ("아군A", 2003), ("아군B", 2003));
        var roster = new List<User> { new(1, "아군A", 2003) };
        var live = new List<(string, int, int)>();

        IReadOnlyList<OverlayRowBuilder.Row> rows = Build(report, roster, live, allow: true, out bool hasCombat);

        Assert.True(hasCombat);
        Assert.Equal(new[] { "아군A", "아군B" }, Names(rows));
    }
}

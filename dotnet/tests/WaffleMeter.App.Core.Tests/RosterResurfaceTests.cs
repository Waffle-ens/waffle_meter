using WaffleMeter.App.Core;
using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

/// <summary>
/// Feature 1 — 파티/공대 구성이 (닉네임+서버 기준) 바뀌면 직전(끝난) 전투 대신 로스터 프리뷰를 다시 띄운다.
/// 같은 파티 리풀은 직전 전투를 유지하고, 기록 재생 경로(allowRosterResurface:false)는 라이브 로스터와 달라도
/// 절대 비우지 않으며, 라이브 로스터가 비면(파티 나감/TTL) 마지막 전투를 유지한다. 판정은 uid 불가지 —
/// (닉,서버) 신원집합만 본다.
/// <para>게이트의 "끝난 전투" 조건은 <see cref="DpsReport.BattleFinished"/>다. 파티 스냅샷 유무로 대신 판정하면
/// 혼자 잡은 전투(스냅샷이 정당하게 빔)에서 프리뷰가 영영 안 뜬다 — 아래 solo 테스트가 그걸 고정한다.</para>
/// </summary>
public sealed class RosterResurfaceTests
{
    // 저장/기록(끝난) 리포트: Information + 얼린 ExecutorId + 그 전투의 0x9702 스냅샷(신원집합).
    private static DpsReport FrozenBattle((int Uid, string Nick, double Amount)[] rows, params (string Nick, int Server)[] snapshot)
    {
        DpsReport report = FinishedBattle(rows);
        report.PartyIdentitiesSnapshot = snapshot
            .Select(m => new RosterMember { Nickname = m.Nick, Server = m.Server, Slot = 1 }).ToList();
        return report;
    }

    // 파티 스냅샷이 없는 끝난 전투 — 혼자 잡은 보스가 정확히 이 모양이다(파티가 없으니 0x9702 스냅샷도 없음).
    private static DpsReport FinishedBattle((int Uid, string Nick, double Amount)[] rows)
    {
        var report = new DpsReport { ExecutorId = rows[0].Uid, BattleFinished = true };
        foreach ((int uid, string nick, double amount) in rows)
        {
            report.Contributors.Add(new User(uid, nick, 2003));
            report.Information[uid] = new DpsInformation(amount, amount, amount, amount);
        }

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
    public void A_solo_battle_resurfaces_the_preview_when_a_party_forms_afterwards()
    {
        // 혼자 잡은 보스(악몽 등) → 파티 가입. 파티가 없었으니 스냅샷은 비어 있고, 그래도 프리뷰가 떠야 한다.
        // 종전에는 "스냅샷이 비었다 = 끝난 전투가 아니다"로 읽혀 게이트가 열리지 않았고, 실제 전투가 시작될
        // 때까지 직전 솔로 전투가 화면에 남았다.
        DpsReport report = FinishedBattle([(1, "본인", 1000)]);
        var roster = new List<User> { new(1, "본인", 2003), new(10, "파티원A", 2003), new(11, "파티원B", 2003) };
        var live = new List<(string, int, int)> { ("본인", 2003, 1), ("파티원A", 2003, 2), ("파티원B", 2003, 3) };

        IReadOnlyList<OverlayRowBuilder.Row> rows = Build(report, roster, live, allow: true, out bool hasCombat);

        Assert.False(hasCombat);                                          // 전투 아님 = 프리뷰로 갈아끼움
        Assert.Equal(new[] { "본인", "파티원A", "파티원B" }, Names(rows)); // 전투력 조회용 idle 로스터
    }

    [Fact]
    public void A_live_in_progress_battle_is_never_blanked_by_the_live_roster()
    {
        // 회귀 방지: 진행 중 전투(BattleFinished=false)는 파티 스냅샷도 없어 battleSet이 공집합이다. "끝난 전투"
        // 조건을 빼면 매 틱 rosterChanged=true가 되어 전투 행이 지워진다 — 전투 중 미터가 비는 최악의 회귀.
        DpsReport report = FinishedBattle([(1, "본인", 1000), (2, "파티원A", 900)]);
        report.BattleFinished = false;
        report.ExecutorId = 0; // 라이브 리포트
        var roster = new List<User> { new(1, "본인", 2003), new(10, "파티원B", 2003) };
        var live = new List<(string, int, int)> { ("본인", 2003, 1), ("파티원B", 2003, 2) }; // 로스터가 달라도

        IReadOnlyList<OverlayRowBuilder.Row> rows = Build(report, roster, live, allow: true, out bool hasCombat);

        Assert.True(hasCombat);
        Assert.Equal(new[] { "본인", "파티원A" }, Names(rows));
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

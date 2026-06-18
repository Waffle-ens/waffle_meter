using WaffleMeter.App.Core;
using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

/// <summary>
/// Covers OverlayRowBuilder — the pure row-selection extracted from OverlayViewModel.Update. Pins three
/// behaviors: (#3) a no-nickname COMBAT row (a bare mid-join provisional actor) is hidden but its DPS is
/// retained and pops in once identity arrives; (#1) the recognized self appears in the pre-combat roster and
/// self-colors; (#2) the roster is suppressed once a real combat report is on screen.
/// </summary>
public sealed class OverlayRowBuilderTests
{
    private static DpsReport CombatReport(params (int Uid, string? Nick, double Amount)[] rows)
    {
        var report = new DpsReport();
        foreach ((int uid, string? nick, double amount) in rows)
        {
            report.Contributors.Add(new User(uid, nick, nick == null ? -1 : 2003));
            report.Information[uid] = new DpsInformation(amount, amount, amount, amount);
        }

        return report;
    }

    [Fact]
    public void Blank_nickname_combat_row_is_filtered_and_does_not_count_as_combat()
    {
        DpsReport report = CombatReport((13601, null, 1000)); // a bare 난입 provisional actor

        IReadOnlyList<OverlayRowBuilder.Row> rows = OverlayRowBuilder.Build(
            report, roster: [], liveSelfId: 0, useTotalDamage: true, showPreCombatRoster: true, out bool hasCombatRows);

        Assert.Empty(rows);              // no blank "broken" row
        Assert.False(hasCombatRows);     // CRITICAL: regate — no boss bar / ticking timer over the placeholder
    }

    [Fact]
    public void Only_named_combat_rows_show_even_if_the_bare_actor_deals_more()
    {
        DpsReport report = CombatReport((1, "플러시", 500), (13601, null, 1000));

        IReadOnlyList<OverlayRowBuilder.Row> rows = OverlayRowBuilder.Build(
            report, [], 0, true, false, out bool hasCombatRows);

        Assert.Single(rows);
        Assert.Equal(1, rows[0].Uid);
        Assert.True(hasCombatRows);
    }

    [Fact]
    public void A_bare_actor_that_gains_a_nickname_appears_with_its_accumulated_dps()
    {
        var report = new DpsReport();
        var user = new User(13601); // bare provisional (Nickname null)
        report.Contributors.Add(user);
        report.Information[13601] = new DpsInformation(1000, 100, 50, 10);

        Assert.Empty(OverlayRowBuilder.Build(report, [], 0, true, false, out _)); // hidden while bare

        user.Nickname = "플러시"; // 0x3633/0x3645 enriches the SAME object in place
        user.Server = 2003;

        IReadOnlyList<OverlayRowBuilder.Row> rows = OverlayRowBuilder.Build(report, [], 0, true, false, out bool hasCombatRows);
        Assert.Single(rows);
        Assert.Equal(1000.0, rows[0].Info.Amount); // full accumulated DPS retained
        Assert.True(hasCombatRows);
    }

    [Fact]
    public void Recognized_self_appears_first_in_the_pre_combat_roster_and_self_colors()
    {
        var exec = new User(1, "플러시", 2003) { IsExecutor = true, Power = 3000 };
        var mate = new User(2, "Wildz", 1014) { Power = 5000 };
        var report = new DpsReport(); // pre-combat: empty Information, ExecutorId 0

        IReadOnlyList<OverlayRowBuilder.Row> rows = OverlayRowBuilder.Build(
            report, roster: new[] { exec, mate }, liveSelfId: 1, useTotalDamage: true, showPreCombatRoster: true, out bool hasCombatRows);

        Assert.Equal(2, rows.Count);
        Assert.Equal(1, rows[0].Uid);    // executor first (roster order preserved)
        Assert.True(rows[0].IsSelf);     // self-colored via User.IsExecutor
        Assert.False(rows[1].IsSelf);
        Assert.False(hasCombatRows);     // idle preview is not "in combat"
    }

    [Fact]
    public void Pre_combat_roster_is_suppressed_once_a_real_combat_report_is_shown()
    {
        DpsReport report = CombatReport((1, "플러시", 1000));
        report.ExecutorId = 1; // a saved/post-combat report freezes the executor uid

        IReadOnlyList<OverlayRowBuilder.Row> rows = OverlayRowBuilder.Build(
            report, roster: new[] { new User(2, "Wildz", 1014) }, liveSelfId: 1, useTotalDamage: true, showPreCombatRoster: true, out bool hasCombatRows);

        Assert.Single(rows);          // only the combat row; the roster is NOT merged in
        Assert.Equal(1, rows[0].Uid);
        Assert.True(hasCombatRows);
    }

    [Fact]
    public void All_bare_combat_with_a_known_roster_shows_neither_a_blank_row_nor_the_preview()
    {
        // Mid-join: the only damager is bare (no nickname) and the executor isn't recognized yet (ExecutorId 0),
        // but a 0x9702 party roster is known. The idle preview must NOT resurface OVER the live fight — the
        // gate keys on report.Information (real combat present), not the post-filter row count.
        DpsReport report = CombatReport((13601, null, 1000));
        var roster = new[] { new User(2, "Wildz", 1014), new User(3, "Mate", 1014) };

        IReadOnlyList<OverlayRowBuilder.Row> rows = OverlayRowBuilder.Build(
            report, roster, liveSelfId: 0, useTotalDamage: true, showPreCombatRoster: true, out bool hasCombatRows);

        Assert.Empty(rows);          // no blank combat row AND no idle preview injected over live combat
        Assert.False(hasCombatRows);
    }

    [Fact]
    public void Self_outside_the_top_eight_is_still_appended()
    {
        var rows = new (int, string?, double)[]
        {
            (10, "A", 100), (11, "B", 90), (12, "C", 80), (13, "D", 70),
            (14, "E", 60), (15, "F", 50), (16, "G", 40), (17, "H", 30),
            (1, "Me", 5),
        };
        DpsReport report = CombatReport(rows);
        report.Contributors[8].IsExecutor = true; // uid 1 = self, lowest damage

        IReadOnlyList<OverlayRowBuilder.Row> display = OverlayRowBuilder.Build(report, [], 1, true, false, out _);

        Assert.Equal(9, display.Count);                       // top 8 + self appended
        Assert.Contains(display, r => r.Uid == 1 && r.IsSelf);
    }
}

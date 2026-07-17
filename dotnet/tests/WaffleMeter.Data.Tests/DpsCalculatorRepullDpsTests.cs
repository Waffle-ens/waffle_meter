using WaffleMeter.Capture;
using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>
/// Covers the "전투 중엔 DPS가 낮게 표시되다가 전투가 끝나면 정상으로 나온다" bug after a 전멸(wipe) re-pull.
/// The live (in-progress) report and the final (ended/saved) report MUST use the same battle-duration basis:
/// the first-damage timestamp. The bug was that the live report took Math.Min(CurrentBattleStart, firstDamage)
/// (DpsCalculator.GetDps), so a re-pull's start toggle — fired at re-aggro/run-back, before damage resumes —
/// inflated the live duration and deflated the displayed DPS; the ended report (RefreshRecentReportFromCache)
/// used firstDamage only, so it was correct, which is exactly the asymmetry the user saw.
/// </summary>
public sealed class DpsCalculatorRepullDpsTests
{
    private const int Instance = 100;
    private const int BossCode = 2301008;
    private const int Dealer = 5001;

    private static (DataManager Dm, DpsCalculator Calc, long[] Clock) Boss()
    {
        long[] now = { 1_000_000 };
        var dm = new DataManager { Clock = () => now[0] };
        dm.LoadMobs(new Dictionary<int, Mob> { [BossCode] = new Mob(BossCode, "보스", Boss: true) });
        dm.SaveMobId(Instance, BossCode);
        dm.SaveNickname(Dealer, "딜러", isExecutor: true, server: 2003, jobByte: 32);
        return (dm, new DpsCalculator(dm), now);
    }

    private static void Hit(DataManager dm, long timestamp, int damage) =>
        dm.SaveDamage(
            new ParsedDamagePacket { ActorId = Dealer, TargetId = Instance, Damage = damage, Timestamp = timestamp },
            dm.CurrentEpoch());

    [Fact]
    public void Live_dps_after_wipe_repull_matches_the_ended_report()
    {
        (DataManager dm, DpsCalculator calc, long[] clock) = Boss();

        // ---- first attempt, then 전멸 (party wipes; the boss leaves combat) ----
        dm.MobHp(Instance, 5000);
        dm.StartBattle(Instance);
        Hit(dm, clock[0] + 1_000, 3000);
        Hit(dm, clock[0] + 2_000, 3000);
        calc.GetDps();                        // a live tick during attempt 1
        clock[0] += 3_000;
        dm.EndBattle(Instance);               // combat-state toggle off on the wipe
        calc.GetDps();                        // end transition: attempt 1 saved + packets flushed

        // ---- re-pull the SAME boss. The start toggle fires at re-aggro, but the party runs back / re-buffs
        //      for ~20s before the first hit lands. ----
        clock[0] = 1_100_000;                 // re-pull, well after the wipe
        dm.MobHp(Instance, 5000);             // boss reset to full -> clears recently-ended, no corpse-guard
        dm.StartBattle(Instance);             // CurrentBattleStart (toggle wall-clock) = 1_100_000
        long firstHit = clock[0] + 20_000;    // first damage 20s after the toggle (the run-back gap)
        Hit(dm, firstHit + 0, 2000);
        Hit(dm, firstHit + 1_000, 2000);
        Hit(dm, firstHit + 2_000, 2000);
        Hit(dm, firstHit + 3_000, 2000);
        Hit(dm, firstHit + 4_000, 2000);      // 5 hits over a 4s span, total 10_000

        // The live (in-progress) report — what the overlay renders mid-fight.
        DpsReport live = calc.GetDps();
        double liveDps = live.Information[Dealer].Dps;

        // The re-pull cache holds only the re-pull's damage (attempt-1 damage was flushed on the wipe).
        Assert.Equal(10_000.0, live.Information[Dealer].Amount, 3);

        // ---- the battle ends -> the "정상" number the user trusts ----
        clock[0] = firstHit + 5_000;
        dm.EndBattle(Instance);
        DpsReport ended = calc.GetDps();
        double endedDps = ended.Information[Dealer].Dps;

        // Both are computed over the 4s damage span (10_000 / 4_000ms * 1000 = 2500), not toggle->now (24s).
        // Pre-fix the live value was ~6x lower (10_000 / 24_000 * 1000 = 416.7) while the ended value was 2500.
        Assert.Equal(2500.0, endedDps, 3);
        Assert.Equal(endedDps, liveDps, 3);   // live == final once damage exists (the regression guard)
    }

    [Fact]
    public void An_early_opener_is_counted_but_the_start_is_capped_near_the_toggle()
    {
        // An opener can land before the combat-enter toggle fires. The ~1s admit window still COUNTS it, but
        // the reported start must not be dragged a full second back (that was the combat-time gap vs the
        // in-game meter) — it is capped to 250ms before the toggle.
        (DataManager dm, DpsCalculator calc, long[] clock) = Boss();
        dm.MobHp(Instance, 5000);
        long toggle = clock[0];
        dm.StartBattle(Instance);            // CurrentBattleStart (combat-enter toggle) = toggle

        Hit(dm, toggle - 800, 2000);         // opener: 800ms BEFORE the toggle (within the ±1s admit window)
        Hit(dm, toggle + 200, 2000);
        Hit(dm, toggle + 1_200, 2000);
        calc.GetDps();                       // a live tick — accumulates the three hits

        clock[0] = toggle + 2_000;
        dm.EndBattle(Instance);
        DpsReport ended = calc.GetDps();

        Assert.Equal(6000.0, ended.Information[Dealer].Amount, 3);   // opener NOT dropped — all 3 counted
        Assert.Equal(toggle - 250, ended.BattleStart);              // capped to 250ms, not the opener's 800ms
    }

    [Fact]
    public void A_spurious_late_toggle_does_not_amputate_the_battle_window()
    {
        // Reproduces the 191M-DPS contamination (2026-07-17): a corpse/phase re-toggle re-stamps
        // CurrentBattleStart LATE relative to the accumulated damage. The end-of-battle full-ring rebuild
        // admits that early damage (before=0L, no cutoff), so the toggle-derived floor sits far
        // (> PreemptivePacketWindowMs) after the first hit. Letting that floor bind collapsed the window to
        // [floor, lastHit] while Amount kept the full fight -> dps exploded. StartAnchor must fall back to the
        // first-damage timestamp. Here the toggle is 2s AFTER the first counted hit (same structure as the bug).
        (DataManager dm, DpsCalculator calc, long[] clock) = Boss();
        dm.MobHp(Instance, 5000);
        long toggle = clock[0];
        dm.StartBattle(Instance);            // CurrentBattleStart (the late/spurious toggle) = toggle

        long firstHit = toggle - 2_000;      // first counted hit is 2s BEFORE the toggle (> the 1s admit window)
        Hit(dm, firstHit + 0, 2000);
        Hit(dm, firstHit + 1_000, 2000);
        Hit(dm, firstHit + 2_000, 2000);     // == toggle
        Hit(dm, firstHit + 3_000, 2000);
        calc.GetDps();                       // a live tick so the end path has a previousTarget to rebuild

        clock[0] = toggle + 2_000;
        dm.EndBattle(Instance);
        DpsReport ended = calc.GetDps();     // end path re-accumulates the WHOLE ring (before=0L, no cutoff)

        Assert.Equal(8000.0, ended.Information[Dealer].Amount, 3);   // full fight retained
        Assert.Equal(firstHit, ended.BattleStart);                  // pre-fix this was toggle-250 (window amputated)
        // window = firstHit..lastHit = 3000ms; pre-fix it collapsed to (lastHit - (toggle-250)) = 1250ms
        Assert.True(ended.BattleEnd - ended.BattleStart >= 2_500);
    }

    [Fact]
    public void A_corpse_toggle_after_the_kill_does_not_spawn_a_second_collapsed_battle()
    {
        // Integration reproduction of the split. One kill must produce ONE saved battle. Pre-fix, when the residual
        // corpse toggle arrived BETWEEN the end toggle and the next GetDps tick (so -1 never propagated and the ring
        // was never flushed), its boss HP was UNTRACKED (null, not 0), it leaked past the corpse-guard, re-stamped
        // CurrentBattleStart at the kill, and the meter emitted the real fight PLUS a second, window-collapsed
        // battle from the full-ring rebuild — the split + 191M-DPS upload. HP is left untracked to hit the null path.
        (DataManager dm, DpsCalculator calc, long[] clock) = Boss();
        var saved = new List<DpsLog>();
        calc.OnBattleLogged = saved.Add;

        long toggle = clock[0];
        dm.StartBattle(Instance);
        Hit(dm, toggle + 1_000, 3000);
        Hit(dm, toggle + 2_000, 3000);
        calc.GetDps();                       // live tick: target=Instance, ring holds the fight

        // Kill toggle and the residual corpse toggle both land before the next GetDps (the real timing window).
        clock[0] = toggle + 3_000;
        dm.EndBattle(Instance);              // kill (HP stays untracked/null) — NO GetDps yet
        clock[0] = toggle + 3_100;
        dm.StartBattle(Instance);            // corpse toggle: pre-fix this leaks and re-stamps the start LATE
        calc.GetDps();                       // pre-fix: "restart" tick saves the real fight, ring NOT flushed

        clock[0] = toggle + 3_200;
        dm.EndBattle(Instance);              // the ghost battle ends
        calc.GetDps();                       // pre-fix: full-ring rebuild w/ late toggle -> 2nd collapsed save

        OverlayAssertSingleNormalWindow(saved);
    }

    private static void OverlayAssertSingleNormalWindow(List<DpsLog> saved)
    {
        Assert.Single(saved);                                             // exactly one battle (no split)
        Assert.True(saved[0].Report.BattleEnd - saved[0].Report.BattleStart >= 1_000); // real window, not ~250ms
    }
}

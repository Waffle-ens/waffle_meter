using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>
/// Covers the corpse-guard in DataManager.StartBattle (the "전투 재시작 시 미터 초기화 안 됨" fix). Battle start/end
/// is a single toggle packet; boss HP is a separate packet. On a re-pull the start-toggle can beat the fresh
/// HP packet, so the guard (which swallows the post-death residual toggle) must NOT freeze a genuine re-pull:
/// it is bounded to a short death-rattle window AND a swallowed start is replayed by the first HP&gt;0 packet.
/// </summary>
public sealed class DataManagerRepullTests
{
    private const int Instance = 100;
    private const int BossCode = 2301008;

    private static DataManager FreshBoss(out long[] clock)
    {
        long[] now = { 1_000_000 };
        clock = now;
        var dm = new DataManager { Clock = () => now[0] };
        dm.SaveMobId(Instance, BossCode);
        dm.MobHp(Instance, 5000); // boss alive
        return dm;
    }

    private static void KillBoss(DataManager dm)
    {
        dm.StartBattle(Instance);
        Assert.Equal(Instance, dm.CurrentTarget());
        dm.MobHp(Instance, 0); // boss dies
        dm.EndBattle(Instance);
        Assert.Equal(-1, dm.CurrentTarget());
    }

    [Fact]
    public void Repull_within_window_is_suppressed_then_replayed_by_first_positive_hp()
    {
        DataManager dm = FreshBoss(out long[] clock);
        KillBoss(dm);

        clock[0] += 1_000; // re-pull 1s after the kill (inside the 3s death-rattle window), HP still 0
        dm.StartBattle(Instance);
        Assert.Equal(-1, dm.CurrentTarget()); // swallowed as a possible residual toggle...

        dm.MobHp(Instance, 4000); // ...but the boss takes its first hit -> the swallowed start is replayed
        Assert.Equal(Instance, dm.CurrentTarget());
    }

    [Fact]
    public void Repull_after_window_starts_immediately()
    {
        DataManager dm = FreshBoss(out long[] clock);
        KillBoss(dm);

        clock[0] += 3_001; // past the death-rattle window
        dm.StartBattle(Instance);
        Assert.Equal(Instance, dm.CurrentTarget()); // genuine re-pull is never blocked
    }

    [Fact]
    public void Repull_within_window_stays_suppressed_until_hp_or_window()
    {
        DataManager dm = FreshBoss(out long[] clock);
        KillBoss(dm);

        clock[0] += 2_999; // still inside the window, no fresh HP yet
        dm.StartBattle(Instance);
        Assert.Equal(-1, dm.CurrentTarget()); // pins the 3000ms threshold
    }

    [Fact]
    public void Repull_with_positive_hp_starts_even_inside_window()
    {
        DataManager dm = FreshBoss(out long[] clock);
        dm.StartBattle(Instance);
        dm.EndBattle(Instance); // ended with HP still > 0 (e.g. boss leashed), not a 0-HP corpse

        clock[0] += 500; // inside the window, but MobHp != 0 so the guard does not apply
        dm.StartBattle(Instance);
        Assert.Equal(Instance, dm.CurrentTarget());
    }

    [Fact]
    public void A_pending_start_does_not_replay_after_its_ttl_expires()
    {
        DataManager dm = FreshBoss(out long[] clock);
        KillBoss(dm);

        clock[0] += 1_000;
        dm.StartBattle(Instance);  // suppressed within the death-rattle window -> pending recorded
        Assert.Equal(-1, dm.CurrentTarget());

        clock[0] += 61_000;        // long past the pending-start TTL — this is no longer "this re-pull"
        dm.MobHp(Instance, 4000);  // a late HP>0 must NOT spawn a stale battle
        Assert.Equal(-1, dm.CurrentTarget());
    }

    [Fact]
    public void Recycling_the_instance_id_clears_a_pending_start()
    {
        DataManager dm = FreshBoss(out long[] clock);
        KillBoss(dm);

        clock[0] += 1_000;
        dm.StartBattle(Instance);            // suppressed -> pending recorded
        Assert.Equal(-1, dm.CurrentTarget());

        dm.SaveMobId(Instance, 9_999_999);   // instance id recycled to a different mob -> drop the stale retry
        dm.MobHp(Instance, 4000);            // HP>0, but the pending start was invalidated
        Assert.Equal(-1, dm.CurrentTarget());
    }
}

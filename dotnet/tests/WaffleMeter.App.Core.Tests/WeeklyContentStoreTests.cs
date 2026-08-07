using WaffleMeter.App.Core;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

/// <summary>
/// Spec for the per-character weekly 성역 clear store and the reset boundary it is read against.
/// </summary>
public sealed class WeeklyContentStoreTests
{
    private static long Kst(int year, int month, int day, int hour, int minute = 0) =>
        new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.FromHours(9)).ToUnixTimeMilliseconds();

    // 2026-08-05 is a Wednesday.
    private static readonly long WedBeforeReset = Kst(2026, 8, 5, 4, 30);
    private static readonly long WedAtReset = Kst(2026, 8, 5, 5, 0);
    private static readonly long WedAfterReset = Kst(2026, 8, 5, 6, 0);
    private static readonly long Friday = Kst(2026, 8, 7, 12, 0);

    [Fact]
    public void Remembers_a_counter_for_the_rest_of_the_week()
    {
        var store = WeeklyContentStore.Parse(null);
        Assert.True(store.Upsert("h1", "rud", 0, WedAfterReset));

        Assert.Equal(0, store.Remaining("h1", "rud", Friday));
    }

    /// <summary>The point of storing a timestamp. The server recharges the counter at the weekly reset, so a
    /// value recorded before it is not "0 left" — it is no longer known, and the panel must show the full grant
    /// rather than keep claiming the raid is done.</summary>
    [Fact]
    public void Forgets_a_counter_recorded_before_the_last_reset()
    {
        var store = WeeklyContentStore.Parse(null);
        store.Upsert("h1", "rud", 0, WedBeforeReset);

        Assert.Null(store.Remaining("h1", "rud", WedAfterReset));
    }

    /// <summary>A record stamped exactly at the reset instant belongs to the new week, not the old one.</summary>
    [Fact]
    public void Keeps_a_counter_recorded_exactly_at_the_reset()
    {
        var store = WeeklyContentStore.Parse(null);
        store.Upsert("h1", "rud", 0, WedAtReset);

        Assert.Equal(0, store.Remaining("h1", "rud", Friday));
    }

    [Fact]
    public void Reports_unknown_for_a_character_or_dungeon_it_has_never_seen()
    {
        var store = WeeklyContentStore.Parse(null);
        store.Upsert("h1", "rud", 0, WedAfterReset);

        Assert.Null(store.Remaining("h2", "rud", Friday));
        Assert.Null(store.Remaining("h1", "ero", Friday));
        Assert.Null(store.Remaining(null, "rud", Friday));
    }

    [Fact]
    public void Round_trips_through_the_settings_blob()
    {
        var store = WeeklyContentStore.Parse(null);
        store.Upsert("h1", "rud", 0, WedAfterReset);
        store.Upsert("h1", "mus", 1, WedAfterReset);
        store.Upsert("h2", "ero", 0, WedAfterReset);

        var reloaded = WeeklyContentStore.Parse(store.Serialize());

        Assert.Equal(0, reloaded.Remaining("h1", "rud", Friday));
        Assert.Equal(1, reloaded.Remaining("h1", "mus", Friday));
        Assert.Equal(0, reloaded.Remaining("h2", "ero", Friday));
    }

    /// <summary>A slug this build doesn't know must survive a load/save cycle. Otherwise a user who ships a
    /// newer meter, records a clear for a dungeon added later, then rolls back once, loses it permanently —
    /// this blob is rewritten on every broadcast.</summary>
    [Fact]
    public void Preserves_a_slug_it_does_not_know()
    {
        var store = WeeklyContentStore.Parse($"h1,newdungeon,0,{WedAfterReset}");

        Assert.Contains("newdungeon", store.Serialize(), StringComparison.Ordinal);
        Assert.Equal(0, store.Remaining("h1", "newdungeon", Friday));
    }

    [Fact]
    public void Skips_malformed_records_without_throwing()
    {
        var store = WeeklyContentStore.Parse($"garbage;h1,rud,notanumber,1;;h2,ero,0,{WedAfterReset};h3,,0,1");

        Assert.Null(store.Remaining("h1", "rud", Friday));
        Assert.Equal(0, store.Remaining("h2", "ero", Friday));
    }

    [Fact]
    public void Skips_a_rewrite_when_nothing_changed()
    {
        var store = WeeklyContentStore.Parse(null);
        Assert.True(store.Upsert("h1", "rud", 1, WedAfterReset));
        Assert.False(store.Upsert("h1", "rud", 1, WedAfterReset + 1000));
        Assert.True(store.Upsert("h1", "rud", 0, WedAfterReset + 2000));
    }

    /// <summary>Same value but a new week still has to be written — the timestamp is what keeps it from
    /// expiring at the next reset.</summary>
    [Fact]
    public void Rewrites_the_same_value_in_a_new_week()
    {
        var store = WeeklyContentStore.Parse(null);
        store.Upsert("h1", "rud", 1, WedBeforeReset);

        Assert.True(store.Upsert("h1", "rud", 1, WedAfterReset));
    }

    [Fact]
    public void Forgetting_a_character_drops_all_of_its_dungeons()
    {
        var store = WeeklyContentStore.Parse(null);
        store.Upsert("h1", "rud", 0, WedAfterReset);
        store.Upsert("h1", "ero", 0, WedAfterReset);
        store.Upsert("h2", "rud", 0, WedAfterReset);

        Assert.True(store.RemoveAll(["h1"]));

        Assert.Null(store.Remaining("h1", "rud", Friday));
        Assert.Null(store.Remaining("h1", "ero", Friday));
        Assert.Equal(0, store.Remaining("h2", "rud", Friday));
        Assert.False(store.RemoveAll(["h1"]));
    }

    [Fact]
    public void Evicts_the_oldest_character_past_the_cap()
    {
        var store = WeeklyContentStore.Parse(null);
        for (int i = 0; i <= WeeklyContentStore.MaxCharacters; i++)
        {
            store.Upsert($"h{i}", "rud", 0, WedAfterReset + i);
        }

        Assert.Null(store.Remaining("h0", "rud", Friday));
        Assert.Equal(0, store.Remaining($"h{WeeklyContentStore.MaxCharacters}", "rud", Friday));
    }

    [Fact]
    public void Rejects_unusable_arguments()
    {
        var store = WeeklyContentStore.Parse(null);

        Assert.False(store.Upsert(null, "rud", 0, WedAfterReset));
        Assert.False(store.Upsert("h1", " ", 0, WedAfterReset));
        Assert.False(store.Upsert("h1", "rud", 0, 0));
    }
}

/// <summary>Spec for the Wednesday 05:00 KST weekly reset boundary.</summary>
public sealed class WeeklyContentResetTests
{
    private static long Kst(int year, int month, int day, int hour, int minute = 0) =>
        new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.FromHours(9)).ToUnixTimeMilliseconds();

    /// <summary>Wednesday before 05:00 still belongs to the PREVIOUS week — the loop has to walk a full 7 days
    /// back to find it, which is the case an "is it Wednesday?" check gets wrong.</summary>
    [Fact]
    public void Wednesday_before_five_belongs_to_the_previous_week() =>
        Assert.Equal(Kst(2026, 7, 29, 5), WeeklyContentReset.LastResetAtOrBefore(Kst(2026, 8, 5, 4, 59)));

    [Fact]
    public void Wednesday_at_five_starts_the_new_week() =>
        Assert.Equal(Kst(2026, 8, 5, 5), WeeklyContentReset.LastResetAtOrBefore(Kst(2026, 8, 5, 5)));

    [Fact]
    public void Later_in_the_week_still_points_at_that_wednesday() =>
        Assert.Equal(Kst(2026, 8, 5, 5), WeeklyContentReset.LastResetAtOrBefore(Kst(2026, 8, 11, 23, 59)));

    /// <summary>The boundary is KST, not the machine's timezone: a player abroad gets the game's reset, not
    /// their PC's idea of Wednesday.</summary>
    [Fact]
    public void Boundary_is_kst_regardless_of_machine_time()
    {
        // 2026-08-04 20:30 UTC == 2026-08-05 05:30 KST — past the reset even though it is still Tuesday in UTC.
        long utcTuesdayEvening = new DateTimeOffset(2026, 8, 4, 20, 30, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

        Assert.Equal(Kst(2026, 8, 5, 5), WeeklyContentReset.LastResetAtOrBefore(utcTuesdayEvening));
    }

    [Fact]
    public void A_nonsense_timestamp_does_not_throw()
    {
        Assert.Equal(0, WeeklyContentReset.LastResetAtOrBefore(long.MinValue));
        Assert.Equal(0, WeeklyContentReset.LastResetAtOrBefore(long.MaxValue));
        Assert.False(WeeklyContentReset.IsCurrentWeek(0, Kst(2026, 8, 7, 12)));
    }
}

/// <summary>
/// Spec for who a 0x610B login dump's counters belong to. The dump arrives ~4 s BEFORE the packet that names
/// the character, so on a character switch "file it under whoever is current" writes the incoming character's
/// counters onto the outgoing character's week.
/// </summary>
public sealed class WeeklyContentOwnershipTests
{
    private const long Dump = 1_000_000;

    /// <summary>The switch. Character A was named at T-5s; B's dump lands now; B's own naming packet has not
    /// arrived. Filing here would overwrite A's real weekly record with B's numbers.</summary>
    [Fact]
    public void Holds_a_dump_while_the_previous_characters_identity_is_still_current() =>
        Assert.False(WeeklyContentOwnership.CanFile(Dump, identityAtMs: Dump - 5_000, nowMs: Dump + 100));

    /// <summary>…and files it once the identity that FOLLOWED the dump is established. That identity is, by
    /// construction, the character the dump described.</summary>
    [Fact]
    public void Files_a_dump_once_the_following_identity_lands() =>
        Assert.True(WeeklyContentOwnership.CanFile(Dump, identityAtMs: Dump + 4_000, nowMs: Dump + 4_100));

    /// <summary>An identity established at the same instant counts as following it.</summary>
    [Fact]
    public void Treats_a_simultaneous_identity_as_following() =>
        Assert.True(WeeklyContentOwnership.CanFile(Dump, identityAtMs: Dump, nowMs: Dump));

    /// <summary>First launch: no identity has ever been established, so 0 must not read as "before the dump"
    /// and let it through immediately.</summary>
    [Fact]
    public void Holds_a_dump_when_no_identity_has_ever_been_established() =>
        Assert.False(WeeklyContentOwnership.CanFile(Dump, identityAtMs: 0, nowMs: Dump + 100));

    /// <summary>A dump that is never followed by a naming packet (a re-send while already in the zone) must
    /// not be held forever — the panel would freeze on a stale reading.</summary>
    [Fact]
    public void Files_a_dump_after_the_settle_window_regardless() =>
        Assert.True(WeeklyContentOwnership.CanFile(
            Dump, identityAtMs: Dump - 5_000, nowMs: Dump + WeeklyContentOwnership.SettleMs));

    [Fact]
    public void Settle_window_is_well_clear_of_the_observed_naming_delay() =>
        Assert.True(WeeklyContentOwnership.SettleMs >= 10_000);
}

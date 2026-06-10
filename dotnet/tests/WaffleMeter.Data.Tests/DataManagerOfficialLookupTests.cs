using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>
/// Covers the official-lookup wiring added to DataManager. The key parity guarantee: with no lookup
/// injected (replay / headless), RequestOfficialCharacterLookup is inert, so the DPS golden is
/// unaffected. The enrichment + throttle behaviour mirrors Kotlin DataManager.
/// </summary>
public sealed class DataManagerOfficialLookupTests
{
    private static readonly IReadOnlyDictionary<int, int> NoSkills = new Dictionary<int, int>();

    private sealed class FakeLookup : IOfficialCharacterLookup
    {
        public int Calls;
        public OfficialCharacterInfo? Result;

        public void LookupAsync(string? nickname, int server, JobClass? fallbackJob, Action<OfficialCharacterInfo> callback)
        {
            Calls++;
            if (Result != null)
            {
                callback(Result); // synchronous for deterministic tests
            }
        }

        public OfficialCharacterInfo? LookupBlocking(string? nickname, int server, JobClass? fallbackJob)
        {
            Calls++;
            return Result;
        }
    }

    [Fact]
    public void Lookup_is_inert_when_no_lookup_injected()
    {
        var dm = new DataManager();
        dm.SaveNickname(1, "Hero", isExecutor: false, server: 3, jobByte: 0);

        dm.RequestOfficialCharacterLookup(1);

        User? user = dm.User(1);
        Assert.NotNull(user);
        Assert.Equal(0, user!.Power);
        Assert.Null(user.Job);
    }

    [Fact]
    public void Enriches_existing_user_with_missing_fields()
    {
        var dm = new DataManager
        {
            OfficialLookup = new FakeLookup { Result = new OfficialCharacterInfo("Hero", 3, JobClass.SORCERER, 9999, NoSkills) },
        };
        dm.SaveNickname(1, "Hero", isExecutor: false, server: 3, jobByte: 0); // job null, power 0

        dm.RequestOfficialCharacterLookup(1);

        User? user = dm.User(1);
        Assert.Equal(9999, user!.Power);
        Assert.Equal(JobClass.SORCERER, user.Job);
    }

    [Fact]
    public void Does_not_overwrite_known_power_or_job()
    {
        var dm = new DataManager
        {
            OfficialLookup = new FakeLookup { Result = new OfficialCharacterInfo("Hero", 3, JobClass.SORCERER, 9999, NoSkills) },
        };
        dm.SaveNickname(1, "Hero", isExecutor: false, server: 3, jobByte: 5); // jobByte 5 -> GLADIATOR
        dm.SaveUserPower(1, 100);

        dm.RequestOfficialCharacterLookup(1);

        User? user = dm.User(1);
        Assert.Equal(100, user!.Power);            // kept
        Assert.Equal(JobClass.GLADIATOR, user.Job); // kept
    }

    [Fact]
    public void Throttles_repeat_lookups_within_ten_minutes()
    {
        long now = 1_000_000;
        var fake = new FakeLookup { Result = new OfficialCharacterInfo("Hero", 3, JobClass.SORCERER, 9999, NoSkills) };
        var dm = new DataManager { OfficialLookup = fake, Clock = () => now };

        dm.RequestOfficialCharacterLookup(1, "Hero", 3, null);
        dm.RequestOfficialCharacterLookup(1, "Hero", 3, null);
        Assert.Equal(1, fake.Calls); // second within 10 min is throttled

        now += (10 * 60 * 1000L) + 1;
        dm.RequestOfficialCharacterLookup(1, "Hero", 3, null);
        Assert.Equal(2, fake.Calls); // throttle window elapsed
    }

    [Theory]
    [InlineData(null, 3)]
    [InlineData("", 3)]
    [InlineData("Hero", 0)]
    [InlineData("Hero", -1)]
    public void Guards_blank_nickname_and_nonpositive_server(string? nickname, int server)
    {
        var fake = new FakeLookup { Result = new OfficialCharacterInfo("Hero", 3, JobClass.SORCERER, 9999, NoSkills) };
        var dm = new DataManager { OfficialLookup = fake };

        dm.RequestOfficialCharacterLookup(1, nickname, server, null);

        Assert.Equal(0, fake.Calls);
    }

    [Fact]
    public void ResolveBlocking_applies_and_returns_info()
    {
        var fake = new FakeLookup { Result = new OfficialCharacterInfo("Hero", 3, JobClass.CHANTER, 4242, NoSkills) };
        var dm = new DataManager { OfficialLookup = fake };
        dm.SaveNickname(1, "Hero", isExecutor: false, server: 3, jobByte: 0);

        OfficialCharacterInfo? info = dm.ResolveOfficialCharacterInfo(1, "Hero", 3, null);

        Assert.NotNull(info);
        Assert.Equal(4242, info!.Power);
        Assert.Equal(4242, dm.User(1)!.Power);
        Assert.Equal(JobClass.CHANTER, dm.User(1)!.Job);
    }

    [Fact]
    public void ResolveBlocking_returns_null_without_a_lookup()
    {
        var dm = new DataManager();
        Assert.Null(dm.ResolveOfficialCharacterInfo(1, "Hero", 3, null));
    }
}

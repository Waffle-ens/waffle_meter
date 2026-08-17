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

    /// <summary>같은 공식 조회 값인데 <b>경로에 따라 규칙이 달랐다</b>. uid가 이미 등록돼 있으면
    /// <c>ApplyOfficialCharacterInfo</c>가 일부러 <c>existing.Power &lt;= 0</c>일 때만 채우는데
    /// (<see cref="Does_not_overwrite_known_power_or_job"/>), uid가 아직 없으면 pending으로 파킹됐다가
    /// <c>UserRepository.Save → MergeInto</c>에서 <b>무조건</b> 덮어썼다 — 저장소 전체에서 유일한
    /// 무조건 덮어쓰기였다. 공식 값은 최대 6시간 캐시된 스냅샷이고 패킷 값은 실시간이라, 이 비대칭은
    /// 맞던 전투력을 낡은 값으로 되돌린다. 두 분기의 규칙을 같게 고정한다.</summary>
    [Fact]
    public void Pending_official_power_does_not_overwrite_a_known_power()
    {
        var dm = new DataManager
        {
            // uid 999는 이 세션에 등록된 적이 없는 엔티티라 결과가 (닉네임, 서버) pending으로 파킹된다.
            OfficialLookup = new FakeLookup { Result = new OfficialCharacterInfo("하아앙", 2003, JobClass.GLADIATOR, 411_232, NoSkills) },
        };

        dm.EnsureUser(339);
        dm.SaveUserPower(339, 356_559);                                  // 0x3656이 앉힌 실시간 진값
        dm.RequestOfficialCharacterLookup(999, "하아앙", 2003, null);     // pending 파킹
        dm.SaveNickname(339, "하아앙", isExecutor: true, server: 2003, jobByte: 8); // 여기서 pending이 병합된다

        Assert.Equal(356_559, dm.User(339)!.Power);
    }

    /// <summary>과잉 차단 방지: 빈 칸이면 pending 전투력은 그대로 채워야 한다(신원보다 전투력이 먼저
    /// 도착하는 경우가 이 pending 맵의 존재 이유다).</summary>
    [Fact]
    public void Pending_official_power_still_fills_an_empty_one()
    {
        var dm = new DataManager
        {
            OfficialLookup = new FakeLookup { Result = new OfficialCharacterInfo("하아앙", 2003, JobClass.GLADIATOR, 411_232, NoSkills) },
        };

        dm.RequestOfficialCharacterLookup(999, "하아앙", 2003, null);
        dm.SaveNickname(339, "하아앙", isExecutor: true, server: 2003, jobByte: 8);

        Assert.Equal(411_232, dm.User(339)!.Power);
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

    [Fact]
    public void Callback_path_is_not_throttled()
    {
        // The party-join panel injects skill/stigma badges via the callback; it must fire on EVERY request
        // (a re-application within 10 min is common in a busy recruit). The 10-min throttle applies only to the
        // fire-and-forget power-enrichment path — throttling the callback path left the join card with no badges.
        long now = 1_000_000;
        var fake = new FakeLookup { Result = new OfficialCharacterInfo("Hero", 3, JobClass.SORCERER, 9999, NoSkills) };
        var dm = new DataManager { OfficialLookup = fake, Clock = () => now };

        int callbacks = 0;
        dm.RequestOfficialCharacterLookup(1, "Hero", 3, null, _ => callbacks++);
        dm.RequestOfficialCharacterLookup(1, "Hero", 3, null, _ => callbacks++); // same uid, well within 10 min

        Assert.Equal(2, callbacks); // both fired — no throttle on the callback path
        Assert.Equal(2, fake.Calls);
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

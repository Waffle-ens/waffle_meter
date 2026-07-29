using WaffleMeter.Data;
using WaffleMeter.Services;
using WaffleMeter.Stats;
using Xunit;

namespace WaffleMeter.Stats.Tests;

/// <summary>
/// 기동 시 1회 위생 정리(<see cref="StatsConsentManager.PurgeImpossibleCharacters"/>).
/// <para>2026-07-30: 0x3633 파서가 아웃바운드 난수 프레임을 본인으로 오인해 <c>nickname="I" / server=47200</c>을
/// 심었고, 그 신원으로 동의 이벤트가 올라갔다가 서버에 거절당하면서(HTTP 400 invalid_schema) 로컬에는
/// <c>accepted / uploadEnabled=false</c>로 굳었다. 파서 게이트는 새 오염만 막으므로 이미 저장된 레코드는
/// 여기서만 사라진다.</para>
/// <para>판정을 서버 번호로만 하는 게 이 스위트의 요점이다 — 닉네임 길이/문자로 지우면 실존 캐릭터를 날릴 수
/// 있고, <c>Server == 0</c>인 이름 없는 구 레코드는 <b>정상</b>이라 반드시 보존해야 한다.</para>
/// </summary>
public sealed class ConsentImpossibleCharacterPurgeTests : IDisposable
{
    private const string GarbageHash = "d044f8703709a3fb402e82271849f2998d2fd5ef9c01c450c088dfae5f446b70";
    private const string RealHash = "0291206ee0f826ce22f3340aebcb21702c31cba93df21876011ded2de2734e23";
    private const string LegacyHash = "9c85509fa6ba7750eb5e713a38653331b75630c0552f523fe4e6cd74c0951848";

    private readonly string _tempAppData;
    private readonly PropertyHandler _props;

    public ConsentImpossibleCharacterPurgeTests()
    {
        _tempAppData = Path.Combine(Path.GetTempPath(), "wm_purge_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempAppData);
        _props = new PropertyHandler(_tempAppData);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempAppData, recursive: true);
        }
        catch
        {
            // best effort
        }
    }

    private StatsConsentManager Manager() => new(
        _props,
        new DataManager(),
        new StatsApiClient(() => "install-1", (_, _, _, _) => new StatsHttpResponse(200, "{}")),
        ownCharacter: () => new StatsOwnCharacter(false, 0, null, 0, null, 0),
        clock: () => 1_700_000_000_000);

    private static string Character(string hash, string? nickname, int server, long updatedAt = 1) =>
        $"\"{hash}\":{{\"state\":\"accepted\",\"uploadEnabled\":true,\"publicCharacter\":true," +
        $"\"consentVersion\":\"2026-06-04\",\"updatedAt\":{updatedAt}," +
        $"\"nickname\":{(nickname is null ? "null" : $"\"{nickname}\"")},\"server\":{server}," +
        $"\"job\":null,\"grant\":false,\"pendingPublic\":false}}";

    private void Seed(params string[] characters) =>
        _props.SetProperty("statsConsentCharacters", "{" + string.Join(",", characters) + "}");

    /// <summary>사고 당시 settings.properties에 실제로 남아 있던 구성: 정상 캐릭터 + 이름 없는 구 레코드 +
    /// 쓰레기 신원.</summary>
    private void SeedContaminatedStore() => Seed(
        Character(RealHash, "콘팡", 2003, 1784123836722),
        Character(LegacyHash, null, 0, 1781447498519),
        Character(GarbageHash, "I", 47200, 1785342115688));

    [Fact]
    public void Purges_the_impossible_character_and_keeps_the_real_one()
    {
        SeedContaminatedStore();
        StatsConsentManager sut = Manager();

        IReadOnlyList<string> purged = sut.PurgeImpossibleCharacters();

        Assert.Equal([GarbageHash], purged);
        IReadOnlyList<string> remaining = sut.ConsentedCharacterHashes();
        Assert.Contains(RealHash, remaining);
        Assert.DoesNotContain(GarbageHash, remaining);
    }

    /// <summary>이름 없는 구 레코드(<c>server: 0</c>)는 정상이다 — 이름/서버가 저장되기 전 세션에서 동의한
    /// 캐릭터이고, 업로드는 그 결정을 존중해야 한다. 여기서 지우면 사용자가 이미 내린 동의가 사라진다.</summary>
    [Fact]
    public void Keeps_the_nameless_legacy_record()
    {
        SeedContaminatedStore();
        StatsConsentManager sut = Manager();

        sut.PurgeImpossibleCharacters();

        Assert.Contains(LegacyHash, sut.ConsentedCharacterHashes());
    }

    /// <summary>전역 키가 지워진 신원을 가리키면 함께 비운다 — 안 그러면 마이그레이션 폴백이 방금 지운 신원의
    /// 상태를 되살린다. 그 자리에 남은 sync 상태/오류도 그 신원의 것이다.</summary>
    [Fact]
    public void Clears_the_global_keys_when_they_point_at_a_purged_identity()
    {
        SeedContaminatedStore();
        _props.SetProperty("statsConsentIdentityHash", GarbageHash);
        _props.SetProperty("statsConsentState", "accepted");
        _props.SetProperty("statsConsentSyncStatus", "sync_failed");
        _props.SetProperty("statsConsentSyncError", "HTTP 400: invalid_schema");

        Manager().PurgeImpossibleCharacters();

        Assert.Equal(string.Empty, _props.GetProperty("statsConsentIdentityHash"));
        Assert.Equal("unknown", _props.GetProperty("statsConsentState"));
        Assert.Equal("local", _props.GetProperty("statsConsentSyncStatus"));
        Assert.Equal(string.Empty, _props.GetProperty("statsConsentSyncError"));
    }

    /// <summary>전역 키가 <b>정상</b> 캐릭터를 가리키면 건드리지 않는다.</summary>
    [Fact]
    public void Leaves_the_global_keys_alone_when_they_point_at_a_surviving_identity()
    {
        SeedContaminatedStore();
        _props.SetProperty("statsConsentIdentityHash", RealHash);
        _props.SetProperty("statsConsentState", "accepted");

        Manager().PurgeImpossibleCharacters();

        Assert.Equal(RealHash, _props.GetProperty("statsConsentIdentityHash"));
        Assert.Equal("accepted", _props.GetProperty("statsConsentState"));
    }

    [Fact]
    public void Is_a_no_op_and_writes_nothing_when_every_record_is_sane()
    {
        Seed(Character(RealHash, "콘팡", 2003));
        string before = _props.GetProperty("statsConsentCharacters")!;

        Assert.Empty(Manager().PurgeImpossibleCharacters());
        Assert.Equal(before, _props.GetProperty("statsConsentCharacters"));
    }

    [Fact]
    public void Is_idempotent()
    {
        SeedContaminatedStore();
        StatsConsentManager sut = Manager();

        Assert.Single(sut.PurgeImpossibleCharacters());
        Assert.Empty(sut.PurgeImpossibleCharacters());
    }

    /// <summary>느슨한 상·하한이라 서버가 증설돼도 정상 사용자를 막지 않는다(실제 범위는 1001-1021 /
    /// 2001-2021이지만, 그걸 하드 화이트리스트로 쓰면 신규 서버 사용자의 동의가 통째로 지워진다).</summary>
    [Theory]
    [InlineData(1001, false)]
    [InlineData(2003, false)]
    [InlineData(2021, false)]
    [InlineData(2050, false)] // 아직 없는 번호지만 같은 체계 — 지우지 않는다
    [InlineData(47200, true)] // 2026-07-30 사고의 실측값
    [InlineData(65535, true)]
    [InlineData(12, true)]
    public void Purge_decision_follows_the_loose_server_bounds(int server, bool shouldPurge)
    {
        Seed(Character(GarbageHash, "X", server));

        Assert.Equal(shouldPurge, Manager().PurgeImpossibleCharacters().Count == 1);
    }
}

using WaffleMeter.Capture;
using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>
/// <see cref="DataManager.SaveUserPower"/>는 패킷에서 온 전투력이 <c>User.Power</c>에 앉는 <b>유일한</b>
/// 관문이다(파서 3경로 — 0x3656 본인 · 0x3645 스냅샷 마커 스캔 · carry-forward — 가 전부 여기로 들어온다).
/// 셋 다 휴리스틱이라 오프셋을 잘못 잡으면 "그 자리에 있던 u32"를 전투력으로 삼는데, 한 번 앉은 값은
/// 되돌릴 경로가 없다: 공식 조회 보정은 <c>Power &lt;= 0</c>일 때만 채우고, 파서의 carry-forward는 그
/// 값을 재입장마다 새 uid로 다시 찍고, 저장 전투에는 그대로 얼어붙는다.
/// <para>실측 사고(2026-08-17): 본인 전투력이 356,559 대신 2,285,1xx로 표시됐고, 400k 미만이라 원래
/// 뜨면 안 되는 티어 배지까지 함께 떴다(<c>TierLadder.MinPower</c>).</para>
/// </summary>
public sealed class DataManagerCombatPowerGateTests
{
    private static DataManager WithUser(int uid)
    {
        var dm = new DataManager();
        dm.EnsureUser(uid);
        return dm;
    }

    [Fact]
    public void PlausiblePower_IsStored()
    {
        DataManager dm = WithUser(339);

        dm.SaveUserPower(339, 356_559);

        Assert.Equal(356_559, dm.User(339)!.Power);
    }

    /// <summary>사고 값. 종전 파서 상한(1000만) 아래라 그대로 통과했다.</summary>
    [Fact]
    public void IncidentValue_IsRejected()
    {
        DataManager dm = WithUser(339);

        dm.SaveUserPower(339, 2_285_100);

        Assert.Equal(0, dm.User(339)!.Power);
    }

    /// <summary>이미 정상값이 앉아 있으면 말이 안 되는 값이 그걸 덮지 못한다 — 사고의 핵심은 "틀린 값이
    /// 들어온 것"이 아니라 "들어온 뒤 되돌릴 수 없었던 것"이다.</summary>
    [Fact]
    public void ImplausibleValue_DoesNotOverwriteAGoodOne()
    {
        DataManager dm = WithUser(339);
        dm.SaveUserPower(339, 356_559);

        dm.SaveUserPower(339, 9_999_999);

        Assert.Equal(356_559, dm.User(339)!.Power);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(1, true)]
    [InlineData(979_329, true)]              // 2026-08-17 코퍼스 실측 최댓값
    [InlineData(CombatPower.Max, true)]
    [InlineData(CombatPower.Max + 1, false)]
    public void Boundaries(int power, bool stored)
    {
        DataManager dm = WithUser(339);

        dm.SaveUserPower(339, power);

        Assert.Equal(stored ? power : 0, dm.User(339)!.Power);
    }
}

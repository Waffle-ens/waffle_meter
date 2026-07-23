using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>
/// 0x9200 멤버 프로필의 표시-계층 보조 로스터(<see cref="DataManager.SaveMemberProfile"/> /
/// <see cref="DataManager.MemberProfileRoster"/>). 신원 저장소는 건드리지 않고, uid 재사용으로 낡은 매핑이 오래
/// 남지 않도록 TTL·상한으로 관리한다.
/// </summary>
public sealed class DataManagerMemberProfileTests
{
    [Fact]
    public void A_saved_profile_is_returned_within_the_window()
    {
        long now = 1_000_000;
        var dm = new DataManager { Clock = () => now };
        dm.SaveMemberProfile(8611, "엽록소", 1014);

        Assert.Contains((8611, "엽록소", 1014), dm.MemberProfileRoster(60_000));
    }

    [Fact]
    public void A_profile_older_than_the_window_is_excluded()
    {
        long clock = 1_000_000;
        var dm = new DataManager { Clock = () => clock };
        dm.SaveMemberProfile(8611, "엽록소", 1014);
        clock += 120_000;

        Assert.Empty(dm.MemberProfileRoster(60_000));
    }

    [Fact]
    public void A_reused_uid_takes_the_latest_name()
    {
        long clock = 1_000_000;
        var dm = new DataManager { Clock = () => clock };
        dm.SaveMemberProfile(8611, "엽록소", 1014);
        clock += 1_000;
        dm.SaveMemberProfile(8611, "다른사람", 1014); // 같은 uid 재사용

        Assert.Equal([(8611, "다른사람", 1014)], dm.MemberProfileRoster(60_000));
    }

    [Fact]
    public void Blank_or_invalid_records_are_ignored()
    {
        var dm = new DataManager { Clock = () => 1_000_000 };
        dm.SaveMemberProfile(0, "엽록소", 1014);   // uid 없음
        dm.SaveMemberProfile(8611, "  ", 1014);    // 빈 이름
        dm.SaveMemberProfile(8611, "엽록소", 0);   // 서버 없음

        Assert.Empty(dm.MemberProfileRoster(60_000));
    }
}

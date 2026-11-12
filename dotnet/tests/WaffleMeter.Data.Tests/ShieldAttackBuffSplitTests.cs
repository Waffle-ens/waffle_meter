using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>
/// 수호성 '보호의 방패'(2026-11-11 클래스 패치)의 공격력 증가 효과는 같은 base 12110000의 짧은 막기 상태와 함께
/// 온다. base 하나로 접으면 뒤에 온 막기 상태가 공격력 버프의 슬롯을 덮어쓴다. 공격력 효과 코드만 표시 코드
/// 12110037로 뗀다.
/// </summary>
public sealed class ShieldAttackBuffSplitTests
{
    private const int Me = 800;
    private const int VisibleRuntime = 121100371; // '보호의 방패' — 오버레이에 보이는 버프
    private const int DisplayCode = 12110037;
    private const int BaseCode = 12110000;
    private const int HiddenStateA = 121100301;   // 같은 base의 숨김 상태(막기 상태)
    private const int HiddenStateB = 121100403;

    [Fact]
    public void The_visible_buff_gets_its_own_display_code_and_the_hidden_states_keep_the_base()
    {
        Assert.Equal(DisplayCode, DataManager.BuffDisplayBase(VisibleRuntime));
        // 집계/통계가 쓰는 BuffBaseCode는 종전 그대로여야 한다(웹 페이로드 불변).
        Assert.Equal(BaseCode, DataManager.BuffBaseCode(VisibleRuntime));

        Assert.Equal(BaseCode, DataManager.BuffDisplayBase(HiddenStateA));
        Assert.Equal(BaseCode, DataManager.BuffDisplayBase(HiddenStateB));
    }

    [Theory]
    [InlineData(HiddenStateA)]
    [InlineData(HiddenStateB)]
    public void A_later_short_hidden_state_does_not_take_over_the_slot(int hiddenState)
    {
        long t0 = 1_000_000;
        var dm = new DataManager { Clock = () => t0 };
        dm.LoadBuffCatalog(new[] { (DisplayCode, "보호의 방패", "수호성") }, Array.Empty<int>());
        dm.SaveNickname(Me, "수호성", isExecutor: true, server: 3, jobByte: 0);

        dm.SaveUseBuff(Me, VisibleRuntime, t0, t0 + 10_000, 10_000, actorId: Me, level: 0, slot: 21);
        dm.SaveUseBuff(Me, hiddenState, t0 + 200, t0 + 1_200, 1_000, actorId: Me, level: 0, slot: 22);

        OwnerBuffView slot = Assert.Single(dm.ActiveOwnerBuffs(t0 + 500));
        Assert.Equal(DisplayCode, slot.Code);      // 이름·아이콘·picker 행의 키
        Assert.Equal("보호의 방패", slot.Name);       // 오버레이 라벨 + TTS 문구
        Assert.Equal(t0 + 10_000, slot.EndMs);     // 남은 시간이 숨김 상태의 1초로 바뀌지 않는다

        // 숨김 상태가 끝나도(그리고 서버가 그 인스턴스를 지워도) 버프는 남는다.
        dm.RemoveBuffSlots(Me, new[] { 22 }, t0 + 1_200);
        Assert.Equal(DisplayCode, Assert.Single(dm.ActiveOwnerBuffs(t0 + 5_000)).Code);
        Assert.Empty(dm.ActiveOwnerBuffs(t0 + 10_001));
    }

    [Fact]
    public void The_display_code_falls_back_to_the_base_icon()
    {
        // 표시 코드는 빈 번호라 전용 PNG가 없다 — 오버레이(8자리)와 상세(9자리) 모두 base 아이콘으로 떨어져야 한다.
        Func<int, bool> shipped = c => c == BaseCode;
        Assert.Equal(BaseCode, IconKey.Skill(DisplayCode, shipped));
        Assert.Equal(BaseCode, IconKey.Skill(VisibleRuntime, shipped));
    }
}

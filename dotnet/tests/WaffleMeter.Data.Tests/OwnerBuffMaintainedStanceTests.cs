using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>
/// The combat-assist overlay's handling of 폭주 (권성): a maintained stance broadcast with no expiry
/// (duration 0xFFFFFFFF). The parser stamps a short synthetic duration; the live overlay must NOT
/// false-expire the slot on an ordinary held re-broadcast gap, and must flag it Indefinite so the UI
/// draws no countdown and the voice alert never pre-warns its (guessed) end.
/// </summary>
public sealed class OwnerBuffMaintainedStanceTests
{
    private const int PokjuRuntimeCode = 191300401; // 폭주 variant; base 19130000
    private const int NormalBuffCode = 118000071;   // an ordinary job buff (real duration)

    private static DataManager WithExecutor(int uid)
    {
        var dm = new DataManager();
        dm.SaveNickname(uid, "권성", isExecutor: true, server: 3, jobByte: 0);
        return dm;
    }

    [Fact]
    public void Maintained_stance_survives_past_its_synthetic_duration()
    {
        long start = 1_000_000;
        DataManager dm = WithExecutor(7);
        // Parser stamps a 6s synthetic duration for the 0xFFFFFFFF stance.
        dm.SaveUseBuff(7, PokjuRuntimeCode, start, start + 6000, 6000, actorId: 7);

        // 12s later — well past the 6s synthetic duration — the stance is still shown (keep-alive), whereas
        // a real 6s buff would already be gone. This is the "폭주가 유지되는데 꺼졌다고 뜬다" fix.
        OwnerBuffView b = Assert.Single(dm.ActiveOwnerBuffs(start + 12_000));
        Assert.True(b.Indefinite);
    }

    [Fact]
    public void A_normal_buff_is_not_indefinite_and_expires_on_time()
    {
        long start = 1_000_000;
        DataManager dm = WithExecutor(7);
        dm.SaveUseBuff(7, NormalBuffCode, start, start + 6000, 6000, actorId: 7);

        OwnerBuffView active = Assert.Single(dm.ActiveOwnerBuffs(start + 3_000));
        Assert.False(active.Indefinite);
        Assert.False(active.Toggle);

        Assert.Empty(dm.ActiveOwnerBuffs(start + 6_001)); // gone right after its real duration
    }

    [Theory]
    // 실측 와이어 모양 그대로: 코드는 세션에서 본 런타임 코드, 지속시간은 펄스마다 실리는 값.
    [InlineData(174100511, 17410000, 1250)] // 치유성 보호의 빛
    [InlineData(181900511, 18190000, 2400)] // 호법성 불패의 진언
    public void A_toggle_aura_is_flagged_and_outlives_a_late_pulse_by_the_grace(int runtimeCode, int baseCode, long pulseMs)
    {
        // 켜 두는 동안 짧은 지속시간을 1초마다 다시 보내는 오라 — 오버레이가 1~0초 톱니를 그리지 않도록
        // Toggle 로 표시하고, 펄스가 선언 만료를 넘겨 늦게 와도 아이콘이 꺼지지 않게 3초 유예를 붙인다.
        long start = 1_000_000;
        DataManager dm = WithExecutor(7);
        dm.SaveUseBuff(7, runtimeCode, start, start + pulseMs, pulseMs, actorId: 3);

        OwnerBuffView b = Assert.Single(dm.ActiveOwnerBuffs(start + 500));
        Assert.Equal(baseCode, b.Code);
        Assert.True(b.Toggle);
        Assert.False(b.Indefinite);          // 폭주의 20초 keep-alive 가 아니라 펄스 + 3초
        Assert.Equal(start + pulseMs + 3_000, b.EndMs);

        Assert.Single(dm.ActiveOwnerBuffs(start + pulseMs + 2_999)); // 늦은 펄스 구간 — 아직 떠 있다
        Assert.Empty(dm.ActiveOwnerBuffs(start + pulseMs + 3_000));  // 유예가 끝나면 사라진다
    }

    [Fact]
    public void Storm_spell_is_an_ordinary_timed_buff_not_a_toggle()
    {
        // 2026-11-11 클래스 패치: 호법성 '질주의 진언'(1초마다 2.4초를 다시 보내는 on/off 오라)이 남은 시간이 있는
        // 자기 버프 '폭풍의 주문'(18160000, 10초)이 됐다. 토글로 남겨 두면 링·타이머를 안 그리고, 만료 뒤 3초를 더
        // 떠 있으며, 서버가 인스턴스를 지워도 버티고, 종료 3초 전 알림도 못 받는다.
        long start = 1_000_000;
        DataManager dm = WithExecutor(7);
        dm.SaveUseBuff(7, 181600001, start, start + 10_000, 10_000, actorId: 7, level: 0, slot: 12);

        OwnerBuffView b = Assert.Single(dm.ActiveOwnerBuffs(start + 500));
        Assert.Equal(18160000, b.Code);
        Assert.False(b.Toggle);
        Assert.False(b.Indefinite);
        Assert.Equal(start + 10_000, b.EndMs);
        Assert.Empty(dm.ActiveOwnerBuffs(start + 10_001)); // 유예 없이 제때 사라진다

        dm.SaveUseBuff(7, 181600001, start + 20_000, start + 30_000, 10_000, actorId: 7, level: 0, slot: 12);
        dm.RemoveBuffSlots(7, new[] { 12 }, start + 21_000);
        Assert.Empty(dm.ActiveOwnerBuffs(start + 21_100)); // 서버가 지우면 바로 지운다
    }

    [Fact]
    public void A_toggle_keeps_its_hold_start_while_pulses_arrive_within_the_grace()
    {
        // 적용 순서 정렬의 키다. 펄스마다 "방금 걸렸다"가 되면 아이콘이 매번 자리를 옮긴다.
        long t = 1_000_000;
        DataManager dm = WithExecutor(7);
        dm.SaveUseBuff(7, 181900511, t, t + 2_400, 2_400, actorId: 3);
        dm.SaveUseBuff(7, 181900511, t + 1_000, t + 3_400, 2_400, actorId: 3);
        dm.SaveUseBuff(7, 181900511, t + 6_000, t + 8_400, 2_400, actorId: 3); // 2.6초 늦음 — 유예 안

        Assert.Equal(t, Assert.Single(dm.ActiveOwnerBuffs(t + 6_500)).HeldSinceMs);

        // 유예까지 넘긴 뒤 다시 켜지면 새로 걸린 것이다.
        long again = t + 8_400 + 3_000;
        dm.SaveUseBuff(7, 181900511, again, again + 2_400, 2_400, actorId: 3);
        Assert.Equal(again, Assert.Single(dm.ActiveOwnerBuffs(again + 100)).HeldSinceMs);
    }

    [Fact]
    public void A_slot_remove_does_not_blink_a_toggle_off()
    {
        // 서버가 오라 인스턴스를 지웠다가 1초 안에 새 펄스를 보내는 일이 흔하다 — 제거로 바로 지우면 그 사이가
        // 깜빡임이다. on/off 오라는 유예가 끝낼 때까지 둔다.
        long t = 1_000_000;
        DataManager dm = WithExecutor(7);
        dm.SaveUseBuff(7, 174100511, t, t + 1_250, 1_250, actorId: 3, level: 0, slot: 41);

        dm.RemoveBuffSlots(7, new[] { 41 }, t + 1_300);

        Assert.Single(dm.ActiveOwnerBuffs(t + 1_400));
        Assert.Empty(dm.ActiveOwnerBuffs(t + 1_250 + 3_000));
    }

    [Fact]
    public void Death_wipes_a_toggle_with_everything_else_without_waiting_for_the_grace()
    {
        // 사망하면 인게임에서 버프가 전부 날아간다. 유예(펄스 + 3초)는 늦은 펄스를 잇는 용도지 사망을 넘기는
        // 용도가 아니다. 부활 뒤 다시 온 펄스는 새로 켠 것으로 잡혀야 자리도 새로 받는다.
        long t = 1_000_000;
        DataManager dm = WithExecutor(7);
        dm.SaveUseBuff(7, 181900511, t, t + 2_400, 2_400, actorId: 3);           // 불패의 진언
        dm.SaveUseBuff(7, NormalBuffCode, t, t + 30_000, 30_000, actorId: 7);

        dm.SaveEntityDeath(7, t + 500);
        Assert.Empty(dm.ActiveOwnerBuffs(t + 600));

        long revived = t + 3_500; // 실측: 사망 뒤 첫 펄스는 빨라야 +3.0초
        dm.SaveUseBuff(7, 181900511, revived, revived + 2_400, 2_400, actorId: 3);
        Assert.Equal(revived, Assert.Single(dm.ActiveOwnerBuffs(revived + 100)).HeldSinceMs);
    }

    [Fact]
    public void A_normal_buff_has_no_hold_start()
    {
        long start = 1_000_000;
        DataManager dm = WithExecutor(7);
        dm.SaveUseBuff(7, NormalBuffCode, start, start + 6000, 6000, actorId: 7);

        Assert.Equal(0, Assert.Single(dm.ActiveOwnerBuffs(start + 1_000)).HeldSinceMs);
    }

    [Fact]
    public void The_maintained_stance_is_not_a_toggle()
    {
        long start = 1_000_000;
        DataManager dm = WithExecutor(7);
        dm.SaveUseBuff(7, PokjuRuntimeCode, start, start + 6000, 6000, actorId: 7);

        OwnerBuffView b = Assert.Single(dm.ActiveOwnerBuffs(start + 1_000));
        Assert.True(b.Indefinite);
        Assert.False(b.Toggle);
    }
}

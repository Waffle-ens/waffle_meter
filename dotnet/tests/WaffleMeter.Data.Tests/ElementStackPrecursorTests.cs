using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>
/// 정령성 4원소 칸(16300000)의 '누적 단계'. 원소(163000003)는 정령 스킬마다 한 겹씩 쌓여 4겹에서 4원소(163000002)로
/// 바뀐다. 오너 결정(2026-11-21)으로 원소 중첩을 4원소 칸에 그리되, 원소가 칸의 대표일 때는 음성을 내지 않는다
/// (<see cref="OwnerBuffView.Silent"/>) — 스택이 쌓이는 것으로는 알림을 울리지 않는다.
/// </summary>
public sealed class ElementStackPrecursorTests
{
    private const int Me = 500;
    private const int Element = 163000003;     // 원소 (클라 중첩 4)
    private const int FourElements = 163000002; // 4원소 (클라 중첩 1)
    private const int Base = 16300000;
    private const int Frenzy = 111000071;      // 검성 광분 — 누적 단계가 아닌 일반 중첩 버프
    private const int FrenzyBase = 11100000;

    private static DataManager Self(long t0)
    {
        var dm = new DataManager { Clock = () => t0 };
        dm.SaveNickname(Me, "본인", isExecutor: true, server: 3, jobByte: 0);
        dm.LoadBuffCatalog(
            new (int, string, string)[] { (Base, "4원소", "정령성"), (FrenzyBase, "광분", "검성") },
            Array.Empty<int>(),
            new Dictionary<int, int> { [Base] = 4, [FrenzyBase] = 2 });
        return dm;
    }

    private static OwnerBuffView Row(DataManager dm, int baseCode, long at) =>
        Assert.Single(dm.ActiveOwnerBuffs(at), b => b.Code == baseCode);

    [Fact]
    public void Element_stacks_show_on_the_four_elements_row_without_voice()
    {
        long t0 = 1_000_000;
        DataManager dm = Self(t0);
        dm.SaveUseBuff(Me, Element, t0, t0 + 10_000, 10_000, Me, level: 1, slot: 11);
        dm.SaveUseBuff(Me, Element, t0 + 1_000, t0 + 11_000, 10_000, Me, level: 1, slot: 12);
        dm.SaveUseBuff(Me, Element, t0 + 2_000, t0 + 12_000, 10_000, Me, level: 1, slot: 13);

        OwnerBuffView row = Row(dm, Base, t0 + 2_500);
        Assert.Equal(3, row.Stacks);
        Assert.True(row.ShowsStackBadge);
        Assert.True(row.Silent);
    }

    [Fact]
    public void Turning_into_four_elements_makes_the_row_voiced_with_no_badge()
    {
        long t0 = 1_000_000;
        DataManager dm = Self(t0);
        for (int i = 0; i < 4; i++)
        {
            dm.SaveUseBuff(Me, Element, t0 + i * 1_000, t0 + 10_000 + i * 1_000, 10_000, Me, level: 1, slot: 11 + i);
        }

        // 4겹에서 4원소가 걸리고 원소 slot 들이 풀린다(어느 순서로 와도 같아야 한다).
        dm.SaveUseBuff(Me, FourElements, t0 + 4_000, t0 + 19_000, 15_000, Me, level: 1, slot: 20);
        dm.RemoveBuffSlots(Me, new[] { 11, 12, 13, 14 }, t0 + 4_000);

        OwnerBuffView row = Row(dm, Base, t0 + 4_500);
        Assert.False(row.Silent);
        Assert.Equal(1, row.Stacks);
        Assert.False(row.ShowsStackBadge);
        Assert.Equal(t0 + 19_000, row.EndMs);
    }

    [Fact]
    public void An_ordinary_stacking_buff_stays_voiced()
    {
        long t0 = 1_000_000;
        DataManager dm = Self(t0);
        dm.SaveUseBuff(Me, Frenzy, t0, t0 + 10_000, 10_000, Me, level: 1, slot: 31);
        dm.SaveUseBuff(Me, Frenzy, t0 + 1_000, t0 + 11_000, 10_000, Me, level: 1, slot: 32);

        OwnerBuffView row = Row(dm, FrenzyBase, t0 + 1_500);
        Assert.Equal(2, row.Stacks);
        Assert.False(row.Silent);
    }
}

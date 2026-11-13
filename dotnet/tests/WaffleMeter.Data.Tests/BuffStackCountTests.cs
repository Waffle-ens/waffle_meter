using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>
/// 본인 버프의 중첩 수(<see cref="OwnerBuffView.Stacks"/>). 서버는 중첩 하나를 버프 인스턴스(slot) 하나로 보내므로,
/// 같은 raw 코드로 살아 있는 slot 의 수가 곧 중첩 수다(0727 코퍼스: 원소의 흐름 slot 235·247·287·311·341 이 차례로
/// 쌓이고 0x382C 한 배치로 함께 풀렸다). 카탈로그에 최대 중첩 수(<c>"s"</c>)가 적힌 base 만 센다.
/// </summary>
public sealed class BuffStackCountTests
{
    private const int Me = 500;
    private const int Flow = 160300401;     // 정령성 원소의 흐름
    private const int FlowBase = 16030000;
    private const int FlowOther = 160300402; // 같은 base 로 접히는 다른 효과(가상)
    private const int Frenzy = 111000071;   // 검성 광분 (OverlapTimeType=Longer)
    private const int FrenzyBase = 11100000;
    private const int Volley = 143300271;   // 궁성 난사, 최대 10
    private const int VolleyBase = 14330000;
    private const int Plain = 117800071;    // 검성 노련한 반격 — "s" 없음
    private const int PlainBase = 11780000;

    private static DataManager Self(long t0)
    {
        var dm = new DataManager { Clock = () => t0 };
        dm.SaveNickname(Me, "본인", isExecutor: true, server: 3, jobByte: 0);
        dm.LoadBuffCatalog(
            new (int, string, string)[]
            {
                (FlowBase, "원소의 흐름", "정령성"),
                (FrenzyBase, "광분", "검성"),
                (VolleyBase, "난사", "궁성"),
                (PlainBase, "노련한 반격", "검성"),
            },
            Array.Empty<int>(),
            new Dictionary<int, int> { [FlowBase] = 5, [FrenzyBase] = 2, [VolleyBase] = 10 });
        return dm;
    }

    private static OwnerBuffView? Row(DataManager dm, int baseCode, long at) =>
        dm.ActiveOwnerBuffs(at).Where(b => b.Code == baseCode).Cast<OwnerBuffView?>().FirstOrDefault();

    [Fact]
    public void Three_slots_of_the_same_code_are_three_stacks_on_one_icon()
    {
        long t0 = 1_000_000;
        DataManager dm = Self(t0);
        dm.SaveUseBuff(Me, Flow, t0, t0 + 10_000, 10_000, Me, level: 1, slot: 235);
        dm.SaveUseBuff(Me, Flow, t0 + 1_000, t0 + 11_000, 10_000, Me, level: 1, slot: 247);
        dm.SaveUseBuff(Me, Flow, t0 + 2_000, t0 + 12_000, 10_000, Me, level: 1, slot: 287);

        OwnerBuffView[] rows = dm.ActiveOwnerBuffs(t0 + 2_500).Where(b => b.Code == FlowBase).ToArray();
        Assert.Single(rows);
        Assert.Equal(3, rows[0].Stacks);
        Assert.True(rows[0].ShowsStackBadge);
    }

    [Fact]
    public void Refreshing_an_existing_slot_does_not_add_a_stack()
    {
        // 최대 중첩에서 다시 걸리면 0x382B(flag=2)가 가장 오래된 slot 을 덮어쓴다 — 수는 그대로다.
        long t0 = 1_000_000;
        DataManager dm = Self(t0);
        dm.SaveUseBuff(Me, Flow, t0, t0 + 10_000, 10_000, Me, level: 1, slot: 235);
        dm.SaveUseBuff(Me, Flow, t0 + 1_000, t0 + 11_000, 10_000, Me, level: 1, slot: 247);
        dm.SaveUseBuff(Me, Flow, t0 + 2_000, t0 + 12_000, 10_000, Me, level: 1, slot: 235);
        dm.SaveUseBuff(Me, Flow, t0 + 2_000, t0 + 12_000, 10_000, Me, level: 1, slot: 247);

        Assert.Equal(2, Row(dm, FlowBase, t0 + 2_500)!.Value.Stacks);
    }

    [Fact]
    public void Removing_one_of_three_slots_keeps_the_icon_with_two_stacks()
    {
        // 회귀 고정: 종전에는 대표 slot(마지막에 기록된 slot) 이 지목되면 아이콘이 통째로 사라졌다.
        long t0 = 1_000_000;
        DataManager dm = Self(t0);
        dm.SaveUseBuff(Me, Flow, t0, t0 + 10_000, 10_000, Me, level: 1, slot: 235);
        dm.SaveUseBuff(Me, Flow, t0 + 1_000, t0 + 11_000, 10_000, Me, level: 1, slot: 247);
        dm.SaveUseBuff(Me, Flow, t0 + 2_000, t0 + 12_000, 10_000, Me, level: 1, slot: 287);

        dm.RemoveBuffSlots(Me, new[] { 287 }, t0 + 3_000);

        OwnerBuffView? row = Row(dm, FlowBase, t0 + 3_000);
        Assert.NotNull(row);
        Assert.Equal(2, row!.Value.Stacks);
        Assert.True(row.Value.ShowsStackBadge);
        Assert.Equal(t0 + 11_000, row.Value.EndMs); // 남은 slot 중 가장 늦은 만료(247)
    }

    [Fact]
    public void Removing_every_slot_drops_the_icon()
    {
        long t0 = 1_000_000;
        DataManager dm = Self(t0);
        dm.SaveUseBuff(Me, Flow, t0, t0 + 10_000, 10_000, Me, level: 1, slot: 235);
        dm.SaveUseBuff(Me, Flow, t0 + 1_000, t0 + 11_000, 10_000, Me, level: 1, slot: 247);
        dm.SaveUseBuff(Me, Flow, t0 + 2_000, t0 + 12_000, 10_000, Me, level: 1, slot: 287);

        dm.RemoveBuffSlots(Me, new[] { 235, 247, 287 }, t0 + 3_000);

        Assert.Null(Row(dm, FlowBase, t0 + 3_000));
    }

    [Fact]
    public void A_slot_that_expires_first_only_lowers_the_count()
    {
        // 광분(Longer): slot 마다 만료가 다르다. 두 번째 slot 의 적용 뒤에 첫 slot 의 0x382B 가 뒤따라도 대표 만료는
        // 늦은 쪽을 따른다 — 이른 slot 이 지면 수만 줄고 아이콘·종료 시각은 그대로다.
        long t0 = 1_000_000;
        DataManager dm = Self(t0);
        dm.SaveUseBuff(Me, Frenzy, t0, t0 + 8_000, 8_000, Me, level: 1, slot: 10);
        dm.SaveUseBuff(Me, Frenzy, t0 + 5_000, t0 + 13_000, 8_000, Me, level: 1, slot: 11);
        dm.SaveUseBuff(Me, Frenzy, t0 + 5_000, t0 + 8_000, 8_000, Me, level: 1, slot: 10);

        OwnerBuffView both = Row(dm, FrenzyBase, t0 + 6_000)!.Value;
        Assert.Equal(2, both.Stacks);
        Assert.Equal(t0 + 13_000, both.EndMs);

        OwnerBuffView one = Row(dm, FrenzyBase, t0 + 9_000)!.Value;
        Assert.Equal(1, one.Stacks);
        Assert.Equal(t0 + 13_000, one.EndMs);
        Assert.False(one.ShowsStackBadge);
    }

    [Fact]
    public void A_different_raw_code_folding_into_the_same_base_is_not_counted()
    {
        long t0 = 1_000_000;
        DataManager dm = Self(t0);
        dm.SaveUseBuff(Me, FlowOther, t0, t0 + 10_000, 10_000, Me, level: 1, slot: 40);
        dm.SaveUseBuff(Me, Flow, t0 + 500, t0 + 10_500, 10_000, Me, level: 1, slot: 41);

        Assert.Equal(1, Row(dm, FlowBase, t0 + 1_000)!.Value.Stacks);

        dm.SaveUseBuff(Me, Flow, t0 + 600, t0 + 10_600, 10_000, Me, level: 1, slot: 42);
        Assert.Equal(2, Row(dm, FlowBase, t0 + 1_000)!.Value.Stacks);
    }

    [Fact]
    public void A_buff_without_a_stack_cap_reports_zero_and_keeps_the_old_removal_rule()
    {
        long t0 = 1_000_000;
        DataManager dm = Self(t0);
        dm.SaveUseBuff(Me, Plain, t0, t0 + 60_000, 60_000, Me, level: 10, slot: 65);
        dm.SaveUseBuff(Me, Plain, t0 + 100, t0 + 60_100, 60_000, Me, level: 10, slot: 66);

        OwnerBuffView row = Row(dm, PlainBase, t0 + 200)!.Value;
        Assert.Equal(0, row.Stacks);
        Assert.False(row.ShowsStackBadge);

        // 종전 규칙 그대로: 대표 slot(마지막 기록 66) 이 지목되면 엔트리째 지운다.
        dm.RemoveBuffSlots(Me, new[] { 66 }, t0 + 300);
        Assert.Null(Row(dm, PlainBase, t0 + 300));
    }

    [Fact]
    public void Own_death_resets_the_count_to_one_on_the_next_apply()
    {
        long t0 = 1_000_000;
        DataManager dm = Self(t0);
        dm.SaveUseBuff(Me, Flow, t0, t0 + 10_000, 10_000, Me, level: 1, slot: 235);
        dm.SaveUseBuff(Me, Flow, t0 + 1_000, t0 + 11_000, 10_000, Me, level: 1, slot: 247);

        dm.SaveEntityDeath(Me, t0 + 2_000);
        Assert.Null(Row(dm, FlowBase, t0 + 2_000));

        dm.SaveUseBuff(Me, Flow, t0 + 3_000, t0 + 13_000, 10_000, Me, level: 1, slot: 300);
        Assert.Equal(1, Row(dm, FlowBase, t0 + 3_000)!.Value.Stacks);
    }

    [Fact]
    public void A_new_own_uid_after_a_zone_load_starts_counting_afresh()
    {
        // 존을 넘으면 본인이 새 uid 로 재등록된다. 옛 uid 의 slot 을 지목하는 0x382C 는 executor 게이트에 막히므로,
        // 옛 slot 을 세면 만료까지 수가 부풀어 있다.
        long t0 = 1_000_000;
        const int MeAfterLoad = 777;
        DataManager dm = Self(t0);
        dm.SaveUseBuff(Me, Flow, t0, t0 + 10_000, 10_000, Me, level: 1, slot: 235);
        dm.SaveUseBuff(Me, Flow, t0 + 1_000, t0 + 11_000, 10_000, Me, level: 1, slot: 247);

        dm.SaveNickname(MeAfterLoad, "본인", isExecutor: true, server: 3, jobByte: 0);
        dm.SaveUseBuff(MeAfterLoad, Flow, t0 + 2_000, t0 + 12_000, 10_000, MeAfterLoad, level: 1, slot: 5);

        Assert.Equal(1, Row(dm, FlowBase, t0 + 2_000)!.Value.Stacks);
    }

    [Fact]
    public void The_count_is_clipped_at_the_catalog_cap()
    {
        long t0 = 1_000_000;
        DataManager dm = Self(t0);
        for (int i = 0; i < 4; i++)
        {
            dm.SaveUseBuff(Me, Frenzy, t0 + i * 100, t0 + 8_000 + i * 100, 8_000, Me, level: 1, slot: 20 + i);
        }

        for (int i = 0; i < 12; i++)
        {
            dm.SaveUseBuff(Me, Volley, t0 + i * 100, t0 + 10_000, 10_000, Me, level: 1, slot: 100 + i);
        }

        Assert.Equal(2, Row(dm, FrenzyBase, t0 + 2_000)!.Value.Stacks);
        Assert.Equal(10, Row(dm, VolleyBase, t0 + 2_000)!.Value.Stacks);
    }

    [Fact]
    public void Slot_zero_is_never_counted()
    {
        long t0 = 1_000_000;
        DataManager dm = Self(t0);
        dm.SaveUseBuff(Me, Flow, t0, t0 + 10_000, 10_000, Me, level: 1, slot: 0);
        dm.SaveUseBuff(Me, Flow, t0 + 100, t0 + 10_100, 10_000, Me, level: 1, slot: 0);

        Assert.Equal(1, Row(dm, FlowBase, t0 + 200)!.Value.Stacks);
    }
}

using WaffleMeter.Capture;
using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>
/// 0x9702 로스터 항목을 uid로 푸는 규칙(<c>ResolveRosterMemberUid</c>).
/// <para>배경(2026-08-09 운영 DB): 2.9.3 공대 미신뢰 24건이 전부 "정확히 1명만 슬롯 없음"이었고 그중
/// 19건(79%)이 <b>업로더 본인</b>이었다. 웹은 참가자(평균 6.9)가 로스터(10)보다 적은 기믹 분할에서 빈자리가
/// 여럿이라 소거법으로 메울 수 없다 — 여기서 본인 uid를 제대로 돌려주는 것이 유일한 해법이다.</para>
/// <para>서버 값은 미상일 수 있다(잘린 0x3633은 -1, 스냅샷 전 기여자는 0). 미상을 불일치로 읽으면 이름이
/// 맞는데도 매칭이 통째로 실패한다. 단 <b>정확 일치가 항상 이겨야</b> 느슨함이 오답을 만들지 않는다.</para>
/// </summary>
public sealed class RosterSlotServerLenienceTests
{
    private static DpsReport ReportOf(DataManager dm, params int[] uids)
    {
        var report = new DpsReport { Contributors = new List<User>(), Information = new Dictionary<int, DpsInformation>() };
        foreach (int uid in uids)
        {
            report.Contributors.Add(dm.User(uid)!);
            report.Information[uid] = new DpsInformation(1, 1, 1, 1);
        }

        return report;
    }

    private static DpsLog Save(DataManager dm, DpsReport report) => dm.SaveBattleLog(
        report,
        new Dictionary<int, Dictionary<string, AnalyzedSkill>>(),
        new Dictionary<int, List<OperatingData>>(),
        new List<OperatingData>());

    [Fact]
    public void Uploader_keeps_its_slot_when_the_contributor_server_is_not_known_yet()
    {
        // 본인이 딜은 했는데 서버가 아직 0인 상태(스냅샷 전에 기여자로 잡힘). 종전에는 서버 동등을 요구해
        // 이 항목이 통째로 실패했고, 업로더만 슬롯을 잃었다.
        var dm = new DataManager { Clock = () => 1_000_000 };
        dm.SaveNickname(7, "Me", isExecutor: true, server: 0, jobByte: 0);
        dm.SavePartyRoster(new List<(string, int, int)> { ("Me", 2003, 8), ("Ally", 2003, 1) });
        dm.SaveNickname(9, "Ally", isExecutor: false, server: 2003, jobByte: 0);

        DpsLog log = Save(dm, ReportOf(dm, 7, 9));

        Assert.Equal(8, log.Report.PartySlots[7]);   // 업로더가 슬롯을 지켰다
        Assert.Equal(1, log.Report.PartySlots[9]);
    }

    [Fact]
    public void A_truncated_self_load_leaving_server_minus_one_still_resolves()
    {
        // 잘린 0x3633은 Server=-1을 남긴다 — identityChanged 판정이 이미 같은 이유로 완화돼 있는 값이다.
        var dm = new DataManager { Clock = () => 1_000_000 };
        dm.SaveNickname(3, "Me", isExecutor: true, server: -1, jobByte: 0);
        dm.SavePartyRoster(new List<(string, int, int)> { ("Me", 2011, 4) });

        DpsLog log = Save(dm, ReportOf(dm, 3));

        Assert.Equal(4, log.Report.PartySlots[3]);
    }

    [Fact]
    public void An_exact_server_match_always_wins_over_a_lenient_one()
    {
        // 🔑 느슨함이 오답을 만들지 않는다는 보장. 같은 이름이 서버만 다르게 둘 있을 때, 로스터가 가리키는
        // 서버와 정확히 맞는 쪽이 슬롯을 가져가야 한다 — 서버 미상인 동명이인이 가로채면 안 된다.
        var dm = new DataManager { Clock = () => 1_000_000 };
        dm.SaveNickname(1, "동명", isExecutor: false, server: 0, jobByte: 0);     // 서버 미상
        dm.SaveNickname(2, "동명", isExecutor: false, server: 2003, jobByte: 0);  // 정확히 일치
        dm.SavePartyRoster(new List<(string, int, int)> { ("동명", 2003, 5) });

        DpsLog log = Save(dm, ReportOf(dm, 1, 2));

        Assert.Equal(5, log.Report.PartySlots[2]);
        Assert.False(log.Report.PartySlots.ContainsKey(1));
    }

    [Fact]
    public void A_real_server_conflict_is_still_rejected()
    {
        // 양쪽 다 서버를 알고 서로 다르면 그건 진짜 불일치다 — 완화는 '미상'에만 적용된다.
        var dm = new DataManager { Clock = () => 1_000_000 };
        dm.SaveNickname(1, "동명", isExecutor: false, server: 1019, jobByte: 0);
        dm.SavePartyRoster(new List<(string, int, int)> { ("동명", 2003, 5) });

        DpsLog log = Save(dm, ReportOf(dm, 1));

        Assert.False(log.Report.PartySlots.ContainsKey(1));
    }

    [Fact]
    public void A_contributor_still_beats_the_executor_pointer()
    {
        // 기여자는 페이로드가 실제로 태그하는 uid다. executor는 존 로드마다 새 uid로 재등록되므로 이 전투에
        // 없는 uid일 수 있고, 그러면 슬롯이 아무에게도 안 붙는다. 순서가 뒤집히면 이 테스트가 잡는다.
        var dm = new DataManager { Clock = () => 1_000_000 };
        dm.SaveNickname(11, "Me", isExecutor: false, server: 2003, jobByte: 0); // 이 전투에서 딜한 uid
        dm.SaveNickname(22, "Me", isExecutor: true, server: 2003, jobByte: 0);  // 재등록된 최신 executor
        dm.SavePartyRoster(new List<(string, int, int)> { ("Me", 2003, 3) });

        DpsLog log = Save(dm, ReportOf(dm, 11));

        Assert.Equal(3, log.Report.PartySlots[11]);
        Assert.False(log.Report.PartySlots.ContainsKey(22));
    }

    [Fact]
    public void Slot_zero_is_still_dropped()
    {
        // 헤더 미매칭(slot 0)은 자리를 모르는 것이므로 완화 대상이 아니다.
        var dm = new DataManager { Clock = () => 1_000_000 };
        dm.SaveNickname(1, "Me", isExecutor: true, server: 0, jobByte: 0);
        dm.SavePartyRoster(new List<(string, int, int)> { ("Me", 2003, 0) });

        DpsLog log = Save(dm, ReportOf(dm, 1));

        Assert.Empty(log.Report.PartySlots);
    }
}

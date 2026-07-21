using WaffleMeter.App.Core;
using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

/// <summary>
/// "이번 세션에 한 번도 못 본 파티원" 구제 (tier 2). <c>DataManager.PartyRoster()</c>는 (닉네임, 서버)로 uid를
/// 찾지 못한 멤버를 조용히 버리므로, 종전의 로스터 복구는 원리적으로 그 멤버를 다룰 수 없었다 — 그런데 무명 행의
/// 주인이 대개 바로 그 사람이다. RAW 0x9702 스냅샷(<c>PartyRosterIdentities</c>)을 표시 계층에 그대로 노출해
/// 채운다. raw에는 uid·직업·전투력이 없으므로 <b>완전한 1:1일 때만</b> 이름을 붙이고, 조금이라도 모호하면
/// 붙이지 않는다(잘못된 이름 &gt; 숨은 행).
/// <para>⚠️ 핵심 안전선: 외부인 가드(<c>HasNamedOutsider</c>)는 <b>이름이 있는</b> 낯선 사람만 본다. 그래서
/// tier 2는 "여기가 정말 파티 씬"이라는 양성 증거(본인 외 파티원이 이름을 달고 이 전투에 있음, 또는 분류된
/// 인스턴스 보스전)를 따로 요구한다 — 그게 없으면 필드보스의 <b>무명</b> 낯선 사람이 파티원 이름을 뒤집어쓴다.</para>
/// </summary>
public sealed class RawRosterRescueTests
{
    private const int Srv = 2003;
    private const int SelfUid = 1;
    private const string SelfName = "나";
    private const int AllyUid = 2;
    private const string AllyName = "아군";

    private static User Member(int uid, string nick, JobClass? job = null) => new(uid, nick, Srv) { Job = job };

    private static (string Nickname, int Server, int Slot) Raw(string nick, int slot, int server = Srv) =>
        (nick, server, slot);

    /// <summary>rows: (uid, nickname or null for a bare row, job, metric).</summary>
    private static DpsReport Report(params (int Uid, string? Nick, JobClass? Job, double Amount)[] rows)
    {
        var report = new DpsReport();
        foreach ((int uid, string? nick, JobClass? job, double amount) in rows)
        {
            report.Contributors.Add(new User(uid, nick, nick == null ? -1 : Srv) { Job = job });
            report.Information[uid] = new DpsInformation(amount, amount, amount, amount);
        }

        return report;
    }

    /// <summary>본인과 파티원 하나가 이름을 달고 싸우는 평범한 파티 씬 + 무명 행 하나.</summary>
    private static DpsReport PartyScene(JobClass? bareJob = null, double bareAmount = 800) =>
        Report(
            (SelfUid, SelfName, JobClass.CLERIC, 1000),
            (AllyUid, AllyName, JobClass.RANGER, 900),
            (3, null, bareJob, bareAmount));

    private static IReadOnlyList<OverlayRowBuilder.Row> Build(
        DpsReport report,
        IReadOnlyList<User>? party,
        IReadOnlyList<(string Nickname, int Server, int Slot)>? raw,
        int selfId = SelfUid,
        string? selfName = SelfName,
        int topN = 10) =>
        OverlayRowBuilder.Build(
            report, roster: [], liveSelfId: selfId, useTotalDamage: true, showPreCombatRoster: false,
            out _, topN: topN, selfNickname: selfName, selfServer: selfName is null ? 0 : Srv,
            selfJob: JobClass.CLERIC, authoritativeParty: party, rosterIdentities: raw);

    private static string?[] Names(IReadOnlyList<OverlayRowBuilder.Row> rows) =>
        rows.Select(r => r.User?.Nickname).ToArray();

    private static List<User> PartyOfTwo() => [Member(SelfUid, SelfName, JobClass.CLERIC), Member(AllyUid, AllyName, JobClass.RANGER)];

    private static List<(string, int, int)> RawOfTwoPlus(params string[] extra)
    {
        var raw = new List<(string, int, int)> { Raw(SelfName, 1), Raw(AllyName, 2) };
        for (int i = 0; i < extra.Length; i++)
        {
            raw.Add(Raw(extra[i], 3 + i));
        }

        return raw;
    }

    // ---- 구제가 동작해야 하는 경우 ----

    [Fact]
    public void A_never_seen_party_member_names_the_single_bare_major()
    {
        // 로스터는 셋을 말하는데 uid가 해석된 건 둘뿐 — "처음 보는 파티원"이 딱 하나, 무명 행도 딱 하나.
        Assert.Equal(
            new[] { SelfName, AllyName, "처음보는친구" },
            Names(Build(PartyScene(), PartyOfTwo(), RawOfTwoPlus("처음보는친구"))));
    }

    [Fact]
    public void A_rescued_row_is_exempt_from_the_display_cap()
    {
        // 구제해 놓고 상한에서 잘리면 도로 사라진다 — raw 로스터도 면제 근거로 인정해야 한다.
        Assert.Equal(
            new[] { SelfName, AllyName, "처음보는친구" },
            Names(Build(PartyScene(), PartyOfTwo(), RawOfTwoPlus("처음보는친구"), topN: 2)));
    }

    [Fact]
    public void An_instanced_boss_is_party_scene_evidence_on_its_own()
    {
        // 분류된 인스턴스(원정/초월/성역)에는 외부인이 없다 — 다른 파티원이 아직 안 보여도 구제해도 된다.
        DpsReport report = Report((SelfUid, SelfName, JobClass.CLERIC, 1000), (3, null, null, 800));
        report.TargetInstanced = true;
        var party = new List<User> { Member(SelfUid, SelfName, JobClass.CLERIC) };

        Assert.Equal(
            new[] { SelfName, "처음보는친구" },
            Names(Build(report, party, new List<(string, int, int)> { Raw(SelfName, 1), Raw("처음보는친구", 2) })));
    }

    [Fact]
    public void A_resolved_unaccounted_member_is_preferred_over_a_raw_only_one()
    {
        // uid까지 확인된 멤버가 미청구로 남아 있으면 그쪽이 먼저다(tier 1). tier 2는 그 뒤의 잔여만 본다.
        DpsReport report = PartyScene();
        List<User> party = [.. PartyOfTwo(), Member(9, "확인된친구", JobClass.SORCERER)];

        Assert.Equal(
            new[] { SelfName, AllyName, "확인된친구" },
            Names(Build(report, party, RawOfTwoPlus("확인된친구", "처음보는친구"))));
    }

    // ---- 붙이면 안 되는 경우 (전부 "이름이 붙지 않는다"를 단언) ----

    [Fact]
    public void A_field_boss_stranger_is_never_named_even_with_a_raw_roster()
    {
        // ★ 이름을 달고 딜하는 낯선 사람이 있으면 공개 씬이므로 raw 로스터가 아무리 풍부해도 재라벨하지 않는다.
        DpsReport report = Report(
            (SelfUid, SelfName, JobClass.CLERIC, 1000),
            (AllyUid, AllyName, JobClass.RANGER, 950),
            (7, "생판남", JobClass.SORCERER, 900),
            (3, null, null, 800));

        Assert.Equal(
            new[] { SelfName, AllyName, "생판남" },
            Names(Build(report, PartyOfTwo(), RawOfTwoPlus("처음보는친구"))));
    }

    [Fact]
    public void A_bare_stranger_is_never_named_when_nothing_proves_this_is_a_party_scene()
    {
        // ★★ 이게 tier 2 고유의 사각지대다. 외부인 가드는 '이름 있는' 낯선 사람만 본다 — 0x3645를 놓쳐
        // 무명인 낯선 사람은 가드에 안 걸린다. 본인 말고 아무 파티원도 이 전투에 안 보이면(필드보스에서
        // 흔한 상황) 그 무명 행이 파티원이라는 근거가 하나도 없으므로 이름을 붙이지 않는다.
        DpsReport report = Report((SelfUid, SelfName, JobClass.CLERIC, 1000), (9001, null, null, 900));
        var party = new List<User> { Member(SelfUid, SelfName, JobClass.CLERIC) };

        Assert.Equal(
            new[] { SelfName },
            Names(Build(report, party, new List<(string, int, int)> { Raw(SelfName, 1), Raw("처음보는친구", 2) })));
    }

    [Fact]
    public void Two_unaccounted_raw_members_suppress_the_rescue()
    {
        Assert.Equal(
            new[] { SelfName, AllyName },
            Names(Build(PartyScene(), PartyOfTwo(), RawOfTwoPlus("친구A", "친구B"))));
    }

    [Fact]
    public void Two_bare_majors_suppress_the_rescue()
    {
        DpsReport report = Report(
            (SelfUid, SelfName, JobClass.CLERIC, 1000),
            (AllyUid, AllyName, JobClass.RANGER, 950),
            (3, null, null, 900),
            (4, null, null, 800));

        Assert.Equal(
            new[] { SelfName, AllyName },
            Names(Build(report, PartyOfTwo(), RawOfTwoPlus("처음보는친구"))));
    }

    [Fact]
    public void A_resolved_member_left_unaccounted_suppresses_the_rescue()
    {
        // tier 1이 직업 근거로 한 명을 소비하고도 '해석된 미청구 멤버'가 남아 있으면, 나머지 무명 행이 그
        // 사람일 수도 있다 — raw 후보로 단정하면 안 된다. (leftResolved 검사를 지우면 이 테스트가 죽는다.)
        DpsReport report = Report(
            (SelfUid, SelfName, JobClass.CLERIC, 1000),
            (3, null, JobClass.RANGER, 900),   // 직업이 유일하게 대응 -> tier 1이 '검증친구A'로 소비
            (4, null, null, 800));             // 직업 정보 없음 -> tier 1이 손대지 못한다
        List<User> party =
        [
            Member(SelfUid, SelfName, JobClass.CLERIC),
            Member(8, "검증친구A", JobClass.RANGER),
            Member(9, "검증친구B", JobClass.SORCERER),
        ];
        var raw = new List<(string, int, int)>
        {
            Raw(SelfName, 1), Raw("검증친구A", 2), Raw("검증친구B", 3), Raw("처음보는친구", 4),
        };

        Assert.Equal(new[] { SelfName, "검증친구A" }, Names(Build(report, party, raw)));
    }

    [Fact]
    public void A_slotless_raw_member_is_never_painted()
    {
        // slot 0은 파싱 유령일 수 있다(실측 코퍼스에 존재). 구조 검증을 통과한 레코드만 실제로 칠한다.
        var raw = new List<(string, int, int)> { Raw(SelfName, 1), Raw(AllyName, 2), Raw("r", 0, 1009) };

        Assert.Equal(new[] { SelfName, AllyName }, Names(Build(PartyScene(), PartyOfTwo(), raw)));
    }

    [Fact]
    public void A_slotless_ghost_that_inflates_the_count_fails_closed()
    {
        // 유령이 후보 수를 부풀리면 1:1이 깨져 억제된다 — slot 필터를 '카운트 전'에 걸면 여기서 오명명이 난다.
        var raw = new List<(string, int, int)>
        {
            Raw(SelfName, 1), Raw(AllyName, 2), Raw("처음보는친구", 3), Raw("r", 0, 1009),
        };

        Assert.Equal(new[] { SelfName, AllyName }, Names(Build(PartyScene(), PartyOfTwo(), raw)));
    }

    [Fact]
    public void The_self_entry_in_the_raw_roster_is_never_a_candidate()
    {
        // 본인이 이 전투에 등록 uid로 안 잡힌 상태(=본인도 실종)에서, raw 로스터의 본인 항목이 무명 행에
        // 칠해지면 안 된다. raw에는 uid가 없으므로 이름(+서버)으로 배제하는 가드가 유일한 방어선이다.
        DpsReport report = Report((AllyUid, AllyName, JobClass.RANGER, 1000), (3, null, null, 900));
        var party = new List<User> { Member(AllyUid, AllyName, JobClass.RANGER) };

        Assert.Equal(
            new[] { AllyName },
            Names(Build(report, party, new List<(string, int, int)> { Raw(SelfName, 1), Raw(AllyName, 2) })));
    }

    [Fact]
    public void The_rescue_is_skipped_without_a_recognized_self()
    {
        // 본인 앵커가 없으면 데이터 계층의 본인 복구(TryRecoverExecutorFromRoster)가 같은 1:1 형태를 소비한다 —
        // 표시 계층이 같은 후보를 '파티원'으로 먼저 칠하면 둘이 충돌한다.
        DpsReport report = Report((AllyUid, AllyName, JobClass.RANGER, 1000), (3, null, null, 800));
        var party = new List<User> { Member(AllyUid, AllyName, JobClass.RANGER) };

        Assert.Equal(
            new[] { AllyName },
            Names(Build(report, party, RawOfTwoPlus("처음보는친구"), selfId: 0, selfName: null)));
    }

    [Fact]
    public void A_trace_bare_row_is_never_named()
    {
        // 상위 대비 15% 미만은 소환수/NPC 누수라 복구 후보가 아니다.
        Assert.Equal(
            new[] { SelfName, AllyName },
            Names(Build(PartyScene(bareAmount: 50), PartyOfTwo(), RawOfTwoPlus("처음보는친구"))));
    }

    [Fact]
    public void A_raw_only_roster_leaves_the_uid_keyed_repair_inert_and_does_not_throw()
    {
        // 해석된 로스터가 비어 있어도(=uid를 하나도 못 찾음) P3(uid 키)는 잠자코 있어야 하고, 어떤 경로도
        // null 컬렉션을 건드리면 안 된다.
        DpsReport report = Report(
            (SelfUid, SelfName, JobClass.CLERIC, 1000), (AllyUid, AllyName, JobClass.RANGER, 900));

        Assert.Equal(
            new[] { SelfName, AllyName },
            Names(Build(report, party: [], RawOfTwoPlus())));
    }

    [Fact]
    public void No_raw_roster_means_the_previous_behavior_is_unchanged()
    {
        Assert.Equal(new[] { SelfName, AllyName }, Names(Build(PartyScene(), PartyOfTwo(), raw: null)));
    }
}

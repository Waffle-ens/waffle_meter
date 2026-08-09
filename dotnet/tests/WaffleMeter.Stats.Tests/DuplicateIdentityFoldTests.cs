using WaffleMeter.Capture;
using WaffleMeter.Data;
using WaffleMeter.Stats;
using Xunit;

namespace WaffleMeter.Stats.Tests;

/// <summary>
/// 같은 캐릭터가 여러 uid로 참가자에 들어온 것을 페이로드에서 하나로 접는다.
/// <para>배경(2026-08-09 운영 DB): executor는 존/인스턴스 로드마다 새 uid로 재등록되는데, 그 경계에서 한 전투의
/// 데미지가 두 uid에 걸치면 같은 사람이 참가자에 두 번 올라간다. 실측된 쌍은 한쪽이 실제 딜(1.1억/1,302타),
/// 다른 쪽이 <c>딜 1 / 1타</c>였고 <b>로스터 슬롯이 유령 쪽에 붙어</b> 실제 딜러가 슬롯을 잃었다. 웹은 참가자
/// 전원이 슬롯을 가져야 서브파티를 신뢰하므로 그 리포트가 통째로 '구분 이전 지표'가 된다.</para>
/// </summary>
public sealed class DuplicateIdentityFoldTests
{
    /// <summary>uid 1·3이 같은 캐릭터("Me"@서버 3), uid 2는 다른 사람.</summary>
    private static DataManager PartyWithRezonedSelf()
    {
        var dm = new DataManager();
        dm.SaveNickname(1, "Me", isExecutor: true, server: 3, jobByte: 5);
        dm.SaveUserPower(1, 5000);
        dm.SaveNickname(2, "Ally", isExecutor: false, server: 3, jobByte: 25);
        dm.SaveUserPower(2, 3000);
        dm.SaveNickname(3, "Me", isExecutor: false, server: 3, jobByte: 5); // 재등록된 같은 캐릭터
        dm.SaveUserPower(3, 5000);
        return dm;
    }

    private static DpsLog LogWithDuplicate(DataManager dm, int ghostSlot, int realSlot)
    {
        var report = new DpsReport
        {
            Contributors = new List<User> { dm.User(1)!, dm.User(2)!, dm.User(3)! },
            BattleStart = 1_000_000,
            BattleEnd = 1_030_000,
            Target = new MobInfo(100, new Mob(12345, "센터보스", true), remainHp: 0, maxHp: 1_000_000),
            Information = new Dictionary<int, DpsInformation>
            {
                [1] = new DpsInformation(1_000_000, 50_000, 60.0, 40.0), // 실제 딜
                [2] = new DpsInformation(600_000, 30_000, 40.0, 24.0),
                [3] = new DpsInformation(1, 1, 0.0, 0.0),                // 유령 (딜 1)
            },
            PartyRosterSize = 10,
            PartySlots = new Dictionary<int, int> { [3] = ghostSlot, [2] = realSlot },
        };

        return new DpsLog
        {
            Report = report,
            SkillDetails = new Dictionary<int, Dictionary<string, AnalyzedSkill>>
            {
                [1] = new() { ["11020001"] = new AnalyzedSkill { SkillCode = 11020001, Name = "강타", DamageAmount = 1_000_000, Times = 100, CritTimes = 20 } },
                [2] = new() { ["15210001"] = new AnalyzedSkill { SkillCode = 15210001, Name = "파이어", DamageAmount = 600_000, Times = 50, CritTimes = 10 } },
                [3] = new() { ["11020001"] = new AnalyzedSkill { SkillCode = 11020001, Name = "강타", DamageAmount = 1, Times = 1, CritTimes = 1 } },
            },
            BuffRates = new Dictionary<int, List<OperatingData>>
            {
                [1] = new() { new OperatingData(110200050, "글래디버프", null, null, 95.5, 1) },
                [3] = new() { new OperatingData(110200051, "재등록버프", null, null, 10.0, 3) },
            },
            BossBuffRates = new List<OperatingData>(),
        };
    }

    private static StatsUploadPayload Build(DataManager dm, DpsLog log) =>
        Assert.IsType<BuildResult.Payload>(
            new StatsPayloadBuilder(dm, publicCharacterProvider: () => false, clock: () => 1_700_000_000_000)
                .Build(log, "1.7.9", killConfirmed: true)).Value;

    [Fact]
    public void The_same_character_appears_once_and_keeps_every_hit()
    {
        DataManager dm = PartyWithRezonedSelf();
        StatsUploadPayload payload = Build(dm, LogWithDuplicate(dm, ghostSlot: 8, realSlot: 2));

        Assert.Equal(2, payload.Participants.Count); // 3명이 아니라 2명
        StatsParticipantPayload me = payload.Participants.Single(p => p.IsUploader);
        Assert.Equal(1_000_001, me.Result.TotalDamage); // 유령의 1딜도 버리지 않는다
        Assert.Equal(101, me.Skills.Single().HitCount); // 100 + 1
    }

    [Fact]
    public void The_slot_survives_even_when_it_landed_on_the_ghost_uid()
    {
        // 🔑 이게 배지의 직접 원인이었다. 슬롯이 유령 uid에 붙어도 접힌 참가자가 물려받아야 한다.
        DataManager dm = PartyWithRezonedSelf();
        StatsUploadPayload payload = Build(dm, LogWithDuplicate(dm, ghostSlot: 8, realSlot: 2));

        Assert.Equal(8, payload.Participants.Single(p => p.IsUploader).PartySlot);
        Assert.All(payload.Participants, p => Assert.NotNull(p.PartySlot)); // 전원 슬롯 = 웹이 신뢰한다
    }

    [Fact]
    public void Folding_does_not_merge_two_different_characters()
    {
        DataManager dm = PartyWithRezonedSelf();
        StatsUploadPayload payload = Build(dm, LogWithDuplicate(dm, ghostSlot: 8, realSlot: 2));

        StatsParticipantPayload ally = payload.Participants.Single(p => !p.IsUploader);
        Assert.Equal(600_000, ally.Result.TotalDamage);
        Assert.Equal(2, ally.PartySlot);
    }

    [Fact]
    public void A_battle_without_duplicates_is_untouched()
    {
        var dm = new DataManager();
        dm.SaveNickname(1, "Me", isExecutor: true, server: 3, jobByte: 5);
        dm.SaveUserPower(1, 5000);
        dm.SaveNickname(2, "Ally", isExecutor: false, server: 3, jobByte: 25);
        dm.SaveUserPower(2, 3000);

        var report = new DpsReport
        {
            Contributors = new List<User> { dm.User(1)!, dm.User(2)! },
            BattleStart = 1_000_000,
            BattleEnd = 1_030_000,
            Target = new MobInfo(100, new Mob(12345, "센터보스", true), remainHp: 0, maxHp: 1_000_000),
            Information = new Dictionary<int, DpsInformation>
            {
                [1] = new DpsInformation(1_000_000, 50_000, 60.0, 40.0),
                [2] = new DpsInformation(600_000, 30_000, 40.0, 24.0),
            },
        };
        var log = new DpsLog
        {
            Report = report,
            SkillDetails = new Dictionary<int, Dictionary<string, AnalyzedSkill>>(),
            BuffRates = new Dictionary<int, List<OperatingData>>(),
            BossBuffRates = new List<OperatingData>(),
        };

        StatsUploadPayload payload = Build(dm, log);

        Assert.Equal(2, payload.Participants.Count);
        Assert.Equal(1_000_000, payload.Participants.Single(p => p.IsUploader).Result.TotalDamage);
        Assert.Equal(600_000, payload.Participants.Single(p => !p.IsUploader).Result.TotalDamage);
    }

    [Fact]
    public void Head_count_party_size_and_job_composition_all_use_the_folded_count()
    {
        // 🔑 웹의 중복 병합 그룹 키가 partySize + jobs 조합이다(battle-group.ts). 접기 전 값으로 보내면 같은
        // 전투를 올린 두 미터의 키가 갈려 하나로 안 묶인다. 정원과 직업 구성도 사람 수 기준이어야 맞다.
        DataManager dm = PartyWithRezonedSelf();
        StatsUploadPayload payload = Build(dm, LogWithDuplicate(dm, ghostSlot: 8, realSlot: 2));

        Assert.Equal(2, payload.Battle.PartySize);              // 기여자 uid 3개가 아니라 사람 2명
        Assert.Equal(payload.Participants.Count, payload.Battle.PartySize);
        Assert.Equal(1, payload.PartyComposition.Jobs["검성"]); // 재등록된 본인이 두 번 세어지지 않는다
        Assert.Equal(1, payload.PartyComposition.Jobs["마도성"]);
    }

    [Fact]
    public void The_top_level_own_result_matches_the_folded_participant_row()
    {
        // 참가자 행은 접힌 합인데 최상위 Result 만 한쪽 uid 몫이면 같은 전투에서 업로더 숫자가 두 군데서
        // 다르게 나간다. 웹은 둘 다 읽는다.
        DataManager dm = PartyWithRezonedSelf();
        StatsUploadPayload payload = Build(dm, LogWithDuplicate(dm, ghostSlot: 8, realSlot: 2));

        StatsParticipantPayload me = payload.Participants.Single(p => p.IsUploader);
        Assert.Equal(me.Result.TotalDamage, payload.Result.TotalDamage);
        Assert.Equal(1_000_001, payload.Result.TotalDamage);
        Assert.Equal(2, payload.Buffs.Count); // 최상위 버프도 두 uid 합
    }

    [Fact]
    public void Buff_caster_references_follow_the_fold()
    {
        // 유령 uid가 시전자로 적힌 버프도 접힌 참가자의 인덱스를 가리켜야 참조가 끊기지 않는다.
        DataManager dm = PartyWithRezonedSelf();
        StatsUploadPayload payload = Build(dm, LogWithDuplicate(dm, ghostSlot: 8, realSlot: 2));

        StatsParticipantPayload me = payload.Participants.Single(p => p.IsUploader);
        int meIndex = 0;
        for (int i = 0; i < payload.Participants.Count; i++)
        {
            if (payload.Participants[i].IsUploader) { meIndex = i; }
        }

        Assert.Equal(2, me.Buffs.Count); // 두 uid의 버프가 합쳐졌다
        Assert.All(me.Buffs, b => Assert.Equal(meIndex, b.OwnerParticipantIndex));
        Assert.Contains(me.Buffs, b => b.ActorParticipantIndex == meIndex); // uid 3 시전 → 대표 인덱스
    }
}

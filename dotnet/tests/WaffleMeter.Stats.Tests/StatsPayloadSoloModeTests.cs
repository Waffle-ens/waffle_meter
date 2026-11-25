using WaffleMeter.Capture;
using WaffleMeter.Data;
using WaffleMeter.Stats;
using Xunit;

namespace WaffleMeter.Stats.Tests;

/// <summary>
/// 1인 모드 플래그(<c>battle.soloMode</c>)의 와이어 계약. 1인 모드는 파티 판과 같은 dungeonId·mobCode라
/// 웹이 인카운터만으로는 못 가른다. 추가 키이고 SchemaVersion 은 그대로다 — 올리면 웹 zod 리터럴이 전투를
/// 400 으로 거절하고 미터는 재시도가 없어 그 전투는 영구 손실이다.
/// </summary>
public sealed class StatsPayloadSoloModeTests
{
    private static (DataManager Dm, DpsLog Log) Scene(bool soloMode)
    {
        var dm = new DataManager();
        dm.SaveNickname(1, "Me", isExecutor: true, server: 3, jobByte: 5);
        dm.SaveUserPower(1, 5000);

        var report = new DpsReport
        {
            Contributors = new List<User> { dm.User(1)! },
            BattleStart = 1_000_000,
            BattleEnd = 1_030_000,
            Target = new MobInfo(100, new Mob(12345, "보스", true), remainHp: 0, maxHp: 1_000_000),
            Information = new Dictionary<int, DpsInformation>
            {
                [1] = new DpsInformation(1_000_000, 50_000, 100.0, 100.0),
            },
            SoloMode = soloMode,
        };
        var log = new DpsLog
        {
            Report = report,
            SkillDetails = new Dictionary<int, Dictionary<string, AnalyzedSkill>>
            {
                [1] = new() { ["11020001"] = new AnalyzedSkill { SkillCode = 11020001, Name = "강타", DamageAmount = 1_000_000, Times = 100 } },
            },
        };
        return (dm, log);
    }

    private static StatsUploadPayload Build(bool soloMode)
    {
        (DataManager dm, DpsLog log) = Scene(soloMode);
        var builder = new StatsPayloadBuilder(dm, publicCharacterProvider: () => false, clock: () => 1_700_000_000_000);
        return Assert.IsType<BuildResult.Payload>(builder.Build(log, "2.0.0", killConfirmed: true)).Value;
    }

    [Fact]
    public void A_solo_mode_battle_is_uploaded_with_the_flag()
    {
        StatsUploadPayload payload = Build(soloMode: true);

        Assert.True(payload.Battle.SoloMode);
        Assert.Contains("\"soloMode\":true", StatsJson.Serialize(payload));
    }

    /// <summary>아닌 전투는 키를 아예 안 보낸다 — false 가 아니다. 미터가 말할 수 있는 건 "못 봤다"뿐이고,
    /// 키가 없는 것이 이 기능 전의 모든 빌드가 보내는 모양이다.</summary>
    [Fact]
    public void Any_other_battle_omits_the_key()
    {
        StatsUploadPayload payload = Build(soloMode: false);

        Assert.Null(payload.Battle.SoloMode);
        Assert.DoesNotContain("soloMode", StatsJson.Serialize(payload));
    }

    [Fact]
    public void The_flag_does_not_bump_the_schema_version() =>
        Assert.Equal(Build(soloMode: false).SchemaVersion, Build(soloMode: true).SchemaVersion);
}

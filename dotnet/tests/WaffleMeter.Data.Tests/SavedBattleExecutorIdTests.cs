using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>
/// Regression guard for the "내 캐릭터" color leaking back to the job color on a saved/ended battle (직업
/// 강조 mode). A saved report's per-row <see cref="User.IsExecutor"/> is frozen by <c>CopyUser</c> — usually
/// <c>false</c>, since a battle is often saved before the own character is recognized — so the overlay had
/// nothing in the report itself to mark the local player and depended on the transient LIVE recognition
/// state, which the history-replay path never refreshes. The saved snapshot now freezes the executor uid
/// (<see cref="DpsReport.ExecutorId"/>) so a replayed battle self-identifies its own player.
/// </summary>
public sealed class SavedBattleExecutorIdTests
{
    private static DpsReport LiveReport(int uid) => new()
    {
        // The own row carries IsExecutor=false on purpose: this mirrors the "saved before recognition"
        // shape that CopyUser freezes, and proves the snapshot does NOT rely on the row flag.
        Contributors = { new User(uid, "마이농", server: 1) },
        Information = { [uid] = new DpsInformation(1000, 0, 100, 50) },
    };

    [Fact]
    public void SaveBattleLog_freezes_the_recognized_executor_uid_into_the_snapshot()
    {
        var dm = new DataManager();
        dm.SaveNickname(15485, "마이농", isExecutor: true, server: 1, jobByte: 0);

        DpsLog log = dm.SaveBattleLog(
            LiveReport(15485),
            new Dictionary<int, Dictionary<string, AnalyzedSkill>>(),
            new Dictionary<int, List<OperatingData>>(),
            new List<OperatingData>());

        Assert.Equal(15485, log.Report.ExecutorId);
        // The frozen row flag stays false — the snapshot self-identifies via ExecutorId, not the row.
        Assert.DoesNotContain(log.Report.Contributors, u => u.IsExecutor);
    }

    [Fact]
    public void SaveBattleLog_leaves_executor_zero_when_the_own_character_is_unrecognized()
    {
        var dm = new DataManager();

        DpsLog log = dm.SaveBattleLog(
            LiveReport(15485),
            new Dictionary<int, Dictionary<string, AnalyzedSkill>>(),
            new Dictionary<int, List<OperatingData>>(),
            new List<OperatingData>());

        // No executor recognized -> 0; the overlay falls back to the live recognized uid for this report.
        Assert.Equal(0, log.Report.ExecutorId);
    }
}

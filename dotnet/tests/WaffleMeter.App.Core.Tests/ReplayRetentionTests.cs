using WaffleMeter.App.Core;
using Xunit;

namespace WaffleMeter.App.Core.Tests;

public sealed class ReplayRetentionTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "waffle-replay-retention-" + Guid.NewGuid().ToString("N"));

    public ReplayRetentionTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort cleanup */ }
    }

    private void WriteReplay(long startMs) =>
        File.WriteAllText(Path.Combine(_dir, $"replay-{startMs}.json"), "{}");

    private string[] SurvivingStartMs() =>
        Directory.GetFiles(_dir, "replay-*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Select(n => n!["replay-".Length..])
            .OrderBy(s => long.Parse(s))
            .ToArray();

    [Fact]
    public void Keeps_only_the_newest_n_and_deletes_the_oldest()
    {
        for (long start = 1000; start <= 1000 + 54; start++) // 55 recordings, ascending battle-start
        {
            WriteReplay(start);
        }

        int deleted = ReplayRetention.Prune(_dir, keep: 50);

        Assert.Equal(5, deleted);
        string[] left = SurvivingStartMs();
        Assert.Equal(50, left.Length);
        Assert.Equal("1005", left[0]);   // the 5 oldest (1000..1004) were pruned
        Assert.Equal("1054", left[^1]);  // the newest survives
    }

    [Fact]
    public void Recency_follows_the_battle_start_in_the_name_not_write_order()
    {
        // Write the newest-numbered file FIRST so file-creation order is the reverse of battle order —
        // proves pruning ranks by the encoded start-ms, not by when the file landed on disk.
        WriteReplay(9000);
        WriteReplay(1000);
        WriteReplay(3000);

        ReplayRetention.Prune(_dir, keep: 2);

        Assert.Equal(new[] { "3000", "9000" }, SurvivingStartMs());
    }

    [Fact]
    public void No_op_when_at_or_below_the_cap()
    {
        for (long start = 1; start <= 50; start++)
        {
            WriteReplay(start);
        }

        Assert.Equal(0, ReplayRetention.Prune(_dir, keep: 50));
        Assert.Equal(50, Directory.GetFiles(_dir, "replay-*.json").Length);
    }

    [Fact]
    public void Ignores_unrelated_files_and_prunes_only_replays()
    {
        WriteReplay(1);
        WriteReplay(2);
        WriteReplay(3);
        File.WriteAllText(Path.Combine(_dir, "replay-diag.log"), "diag"); // not a replay-*.json recording
        File.WriteAllText(Path.Combine(_dir, "notes.txt"), "keep me");

        ReplayRetention.Prune(_dir, keep: 1);

        Assert.Equal(new[] { "3" }, SurvivingStartMs());
        Assert.True(File.Exists(Path.Combine(_dir, "replay-diag.log")));
        Assert.True(File.Exists(Path.Combine(_dir, "notes.txt")));
    }

    [Fact]
    public void Missing_directory_is_a_safe_no_op()
    {
        string missing = Path.Combine(_dir, "does-not-exist");
        Assert.Equal(0, ReplayRetention.Prune(missing));
    }
}

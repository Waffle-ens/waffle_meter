using WaffleMeter.Data;
using Xunit;

namespace WaffleMeter.Data.Tests;

/// <summary>
/// Locks the job-mislabel fix: a player's OWN job-locked damage skills (OwnSkill provenance) correct a
/// wrong job set by the fragile snapshot jobByte (Heuristic) or a same-name official lookup (Official),
/// and then stay locked. Reproduces the reported 차예[시엘] 살성→궁성 case (jobByte 13 => RANGER, but the
/// player casts 13xxxxxx ASSASSIN skills).
/// </summary>
public sealed class UserJobProvenanceTests
{
    private static User NewUser() => new(42, "차예", server: 1001);

    [Fact]
    public void Own_skill_corrects_an_authoritative_job_then_locks()
    {
        User u = NewUser();

        Assert.True(u.TrySetJob(JobClass.RANGER, JobProvenance.Authoritative)); // mis-read jobByte / collision
        Assert.Equal(JobClass.RANGER, u.Job);

        Assert.True(u.TrySetJob(JobClass.ASSASSIN, JobProvenance.OwnSkill)); // own 13xxxxxx skill corrects it
        Assert.Equal(JobClass.ASSASSIN, u.Job);

        Assert.False(u.TrySetJob(JobClass.RANGER, JobProvenance.Authoritative)); // a later byte/lookup can't flip it back
        Assert.Equal(JobClass.ASSASSIN, u.Job);
    }

    [Fact]
    public void Authoritative_first_write_wins_and_null_is_ignored()
    {
        // The snapshot jobByte and the official lookup share the Authoritative tier; the live snapshot byte
        // set first is not clobbered by a later same-name official lookup (preserves prior behavior).
        User u = NewUser();
        Assert.True(u.TrySetJob(JobClass.GLADIATOR, JobProvenance.Authoritative)); // jobByte
        Assert.False(u.TrySetJob(JobClass.SORCERER, JobProvenance.Authoritative)); // official lookup -> ignored
        Assert.False(u.TrySetJob(null, JobProvenance.OwnSkill));                    // null never clears a job
        Assert.Equal(JobClass.GLADIATOR, u.Job);
    }

    [Fact]
    public void Own_skill_writes_are_idempotent()
    {
        User u = NewUser();
        Assert.True(u.TrySetJob(JobClass.ASSASSIN, JobProvenance.OwnSkill));
        Assert.False(u.TrySetJob(JobClass.ASSASSIN, JobProvenance.OwnSkill)); // same source+job -> no-op
        Assert.Equal(JobClass.ASSASSIN, u.Job);
        Assert.Equal(JobProvenance.OwnSkill, u.JobSource);
    }

    [Fact]
    public void SaveNickname_jobByte_is_heuristic_and_yields_to_own_skill()
    {
        var dm = new DataManager();
        dm.SaveNickname(42, "차예", isExecutor: false, server: 1001, jobByte: 13); // ConvertFromCode(13) => RANGER
        Assert.Equal(JobClass.RANGER, dm.User(42)!.Job);

        dm.User(42)!.TrySetJob(JobClass.ASSASSIN, JobProvenance.OwnSkill); // the player's own 13xxxxxx skill

        dm.SaveNickname(42, "차예", isExecutor: false, server: 1001, jobByte: 13); // a later snapshot byte
        Assert.Equal(JobClass.ASSASSIN, dm.User(42)!.Job); // stays corrected
        Assert.Equal(JobProvenance.OwnSkill, dm.User(42)!.JobSource);
    }
}

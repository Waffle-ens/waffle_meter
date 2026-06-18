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

    [Fact]
    public void SaveNickname_resets_locked_job_when_a_reused_uid_changes_identity()
    {
        // AION2 reuses entity ids across pulls. A reused id taken over by a DIFFERENT player must NOT keep
        // the prior player's OwnSkill-locked class icon (the reported sticky-mislabel). Reproduces a 정령성
        // entity id later occupied by a 치유성.
        var dm = new DataManager();
        dm.SaveNickname(7, "옛주인", isExecutor: false, server: 1001, jobByte: 21); // ELEMENTALIST
        dm.User(7)!.TrySetJob(JobClass.ELEMENTALIST, JobProvenance.OwnSkill);       // locked by own skill
        dm.SaveUserPower(7, 5000);
        Assert.Equal(JobClass.ELEMENTALIST, dm.User(7)!.Job);

        dm.SaveNickname(7, "새주인", isExecutor: false, server: 1001, jobByte: 29); // CLERIC, different player

        Assert.Equal("새주인", dm.User(7)!.Nickname);
        Assert.Equal(JobClass.CLERIC, dm.User(7)!.Job);            // not the prior ELEMENTALIST
        Assert.Equal(JobProvenance.Authoritative, dm.User(7)!.JobSource);
        Assert.Equal(0, dm.User(7)!.Power);                        // stale power cleared, re-resolves
    }

    [Fact]
    public void SaveNickname_same_name_does_not_reset_an_own_skill_job()
    {
        // Guard for the reset above: a repeated snapshot for the SAME player (same name) must keep the
        // own-skill correction — otherwise every re-probe would wipe the live ground truth.
        var dm = new DataManager();
        dm.SaveNickname(7, "차예", isExecutor: false, server: 1001, jobByte: 13); // RANGER
        dm.User(7)!.TrySetJob(JobClass.ASSASSIN, JobProvenance.OwnSkill);

        dm.SaveNickname(7, "차예", isExecutor: false, server: 1001, jobByte: 13); // same name re-probe

        Assert.Equal(JobClass.ASSASSIN, dm.User(7)!.Job);
        Assert.Equal(JobProvenance.OwnSkill, dm.User(7)!.JobSource);
    }
}

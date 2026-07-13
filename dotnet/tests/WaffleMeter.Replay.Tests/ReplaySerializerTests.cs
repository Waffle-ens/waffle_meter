using WaffleMeter.Replay;
using Xunit;

namespace WaffleMeter.Replay.Tests;

/// <summary>Round-trip spec for <see cref="ReplaySerializer"/> (the on-disk / web-upload format).</summary>
public class ReplaySerializerTests
{
    private static ReplayRecording Sample()
    {
        return new ReplayRecording
        {
            BattleEpoch = 5,
            StartMs = 1_000_000,
            EndMs = 1_200_000,
            BossDefeated = false, // a 직전 전투
            TargetCode = 2301059,
            TargetName = "무스펠의 성배",
            Tracks =
            [
                new ReplayTrack
                {
                    Uid = 12892,
                    Nickname = "몽몽",
                    Server = 2003,
                    Job = "치유성",
                    IsSelf = true,
                    PartySlot = 3,
                    SourceOpcode = 0x371C,
                    SourceOffset = 2,
                    Points = [new ReplayPoint(0, 100.5f, -200.25f, 35043f), new ReplayPoint(400, 110.5f, -210.25f, 35050f)],
                },
                new ReplayTrack
                {
                    Uid = 37808,
                    IsTarget = true,
                    SourceOpcode = 0x372F,
                    SourceOffset = 1,
                    Points = [new ReplayPoint(0, 0f, 0f, 0f)],
                },
                new ReplayTrack { Uid = 999, Nickname = "지각", Server = 2003, Points = [] }, // empty track preserved
            ],
            Casts =
            [
                // a self-centred mechanic (the boss is its own anchor)
                new ReplayCast
                {
                    TMs = 5_000,
                    SkillCode = 1806450,
                    FacingDeg = -117.5f,
                    HpFraction = 0.72f,
                    Targets = [new ReplayCastTarget(37808, 1000f, 2000f, 50f)],
                },
                // a spread: several players marked at once
                new ReplayCast
                {
                    TMs = 9_500,
                    SkillCode = 1608090,
                    FacingDeg = 30f,
                    HpFraction = 0.41f,
                    Targets =
                    [
                        new ReplayCastTarget(12892, 10f, 20f, 5f),
                        new ReplayCastTarget(999, 300f, -80f, 5f),
                    ],
                },
            ],
        };
    }

    [Fact]
    public void Round_trips_all_fields()
    {
        ReplayRecording original = Sample();
        ReplayRecording back = ReplaySerializer.Deserialize(ReplaySerializer.Serialize(original));

        Assert.Equal(original.Schema, back.Schema);
        Assert.Equal(original.BattleEpoch, back.BattleEpoch);
        Assert.Equal(original.StartMs, back.StartMs);
        Assert.Equal(original.EndMs, back.EndMs);
        Assert.False(back.BossDefeated);
        Assert.Equal(2301059, back.TargetCode);
        Assert.Equal("무스펠의 성배", back.TargetName);
        Assert.Equal(3, back.Tracks.Count);
        Assert.Equal(original.PointCount, back.PointCount);
    }

    [Fact]
    public void Round_trips_a_player_track_with_points_and_unicode()
    {
        ReplayRecording back = ReplaySerializer.Deserialize(ReplaySerializer.Serialize(Sample()));

        ReplayTrack mong = Assert.Single(back.Tracks, t => t.Uid == 12892);
        Assert.Equal("몽몽", mong.Nickname);
        Assert.Equal("치유성", mong.Job);
        Assert.True(mong.IsSelf);
        Assert.Equal(3, mong.PartySlot);
        Assert.Equal(0x371C, mong.SourceOpcode);
        Assert.Equal(2, mong.Points.Count);
        Assert.Equal(0, mong.Points[0].TMs);
        Assert.Equal(100.5f, mong.Points[0].X);
        Assert.Equal(-200.25f, mong.Points[0].Y);
        Assert.Equal(35043f, mong.Points[0].Z);
        Assert.Equal(400, mong.Points[1].TMs);
    }

    [Fact]
    public void Preserves_target_flag_and_empty_tracks()
    {
        ReplayRecording back = ReplaySerializer.Deserialize(ReplaySerializer.Serialize(Sample()));

        Assert.Single(back.Tracks, t => t.IsTarget && t.Uid == 37808);
        Assert.Single(back.Tracks, t => t.Uid == 999 && t.Points.Count == 0);
    }

    [Fact]
    public void Points_are_emitted_as_flat_arrays_for_compactness()
    {
        string json = ReplaySerializer.Serialize(Sample());
        Assert.Contains("\"pts\":[[0,", json); // [t,x,y,z] arrays, not objects
    }

    [Fact]
    public void Round_trips_boss_mechanic_casts()
    {
        ReplayRecording back = ReplaySerializer.Deserialize(ReplaySerializer.Serialize(Sample()));

        Assert.Equal(ReplayRecording.CurrentSchema, back.Schema);
        Assert.Equal(2, back.Casts.Count);

        ReplayCast line = back.Casts[0];
        Assert.Equal(5_000, line.TMs);
        Assert.Equal(1806450, line.SkillCode);
        Assert.Equal(-117.5f, line.FacingDeg);
        Assert.Equal(0.72f, line.HpFraction);
        ReplayCastTarget anchor = Assert.Single(line.Targets);
        Assert.Equal(37808, anchor.Uid);
        Assert.Equal(1000f, anchor.X);

        ReplayCast spread = back.Casts[1];
        Assert.Equal(2, spread.Targets.Count); // every marked player survives the round trip
        Assert.Equal(999, spread.Targets[1].Uid);
        Assert.Equal(-80f, spread.Targets[1].Y);
    }

    [Fact]
    public void A_recording_without_casts_omits_the_array_and_reads_back_empty()
    {
        var rec = new ReplayRecording { StartMs = 1, EndMs = 2 };

        string json = ReplaySerializer.Serialize(rec);

        Assert.DoesNotContain("casts", json); // old-shaped payloads stay byte-identical
        Assert.Empty(ReplaySerializer.Deserialize(json).Casts);
    }

    [Fact]
    public void A_schema_1_payload_still_deserializes()
    {
        // What a pre-mechanics meter wrote. It must keep opening (no casts, tracks intact).
        const string v1 = """
            {"schema":1,"epoch":0,"startMs":10,"endMs":20,"bossDefeated":true,"targetCode":2301059,
             "tracks":[{"uid":7,"nick":"옛것","srv":2003,"job":"","self":false,"target":false,"slot":0,
                        "op":"371C","off":2,"pts":[[0,1,2,3]]}]}
            """;

        ReplayRecording back = ReplaySerializer.Deserialize(v1);

        Assert.Equal(1, back.Schema);
        Assert.Empty(back.Casts);
        Assert.Equal("옛것", Assert.Single(back.Tracks).Nickname);
    }
}

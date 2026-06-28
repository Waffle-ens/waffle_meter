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
}

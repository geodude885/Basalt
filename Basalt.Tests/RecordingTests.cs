using System.Numerics;
using Basalt.DevTools.Testing;
using Basalt.Services;
using Basalt.Services.Charts;
using Basalt.Services.Clips;
using Xunit;

namespace Basalt.Tests;

public class ChartWritingTests
{
    private const string HandWritten = /*lang=json*/ """
        {
          // a comment
          "name": "Song", /* another */
          "description": "Don't panic - it's été",
          "lane": { "length": 12, "width": 3, },
          "actors": [ { "id": "boss" } ],
        }
        """;

    [Fact]
    public void CommentsAndTrailingCommasAreReadAndPlainJsonIsWritten()
    {
        var saved = LaneSetupWriter.WriteActors(HandWritten, [new ActorSpec("boss", "Boss", new Spot(0f, 10f), "you")]);
        var chart = ChartLoader.Load(saved).Chart!;

        Assert.Equal("Song", chart.Name);
        Assert.Equal(12f, chart.Lane.Length);
        Assert.Equal(10f, chart.Actors[0].At.Z);
        Assert.DoesNotContain("//", saved, StringComparison.Ordinal);
    }

    [Fact]
    public void ApostrophesAndAccentsAreWrittenAsTheyAre()
    {
        var saved = LaneSetupWriter.WriteActors(HandWritten, []);

        Assert.Contains("Don't panic - it's été", saved, StringComparison.Ordinal);
    }

    [Fact]
    public void KeysAreMatchedAsTheLoaderMatchesThem()
    {
        var saved = LaneSetupWriter.WriteActors(/*lang=json,strict*/ """{ "Actors": [ { "id": "old" } ] }""", [new ActorSpec("new", "New", new Spot(0f, 5f), "you")]);

        Assert.Equal("new", Assert.Single(ChartLoader.Load(saved).Chart!.Actors).Id);
        Assert.DoesNotContain("old", saved, StringComparison.Ordinal);
    }

    [Fact]
    public void NotAnObjectIsRefused() => Assert.Throws<FormatException>(() => LaneSetupWriter.WriteActors("[1]", []));

    [Fact]
    public void BrokenJsonIsRefusedAsAFormatProblem() => Assert.Throws<FormatException>(() => LaneSetupWriter.WriteActors("{ \"name\": ", []));
}

public class TrackTests
{
    [Fact]
    public void TracksLoadAndPlayEachClipUntilTheNext()
    {
        var chart = Load(/*lang=json,strict*/ """
            {
              "format": 2,
              "actors": [ { "id": "boss" } ],
              "tracks": [ { "actor": "boss", "clips": [
                { "t": 0, "clip": "boss-clip-1" },
                { "t": 8, "clip": "boss-clip-2", "from": 8, "to": 12 },
                { "t": 12, "clip": "boss-clip-1", "from": 12 }
              ] } ]
            }
            """);

        var track = Assert.Single(chart.Tracks);
        Assert.Equal(3, track.Clips.Count);

        var plays = new ChartRunner(chart).Schedule.OfType<PlayClip>().ToList();
        Assert.Equal(new PlayClip(0, "boss", "boss-clip-1", 0, 8), plays[0]);
        Assert.Equal(new PlayClip(8, "boss", "boss-clip-2", 8, 12), plays[1]);
        Assert.Equal(new PlayClip(12, "boss", "boss-clip-1", 12, null), plays[2]);
    }

    [Theory]
    [InlineData(/*lang=json,strict*/ """{ "tracks": [ { "clips": [ { "t": 0, "clip": "a" } ] } ] }""", "format")]
    [InlineData(/*lang=json,strict*/ """{ "format": 2, "tracks": [ { "actor": "nobody", "clips": [] } ] }""", "is not one of the chart's actors")]
    [InlineData(/*lang=json,strict*/ """{ "format": 2, "tracks": [ { "clips": [ { "t": 0 } ] } ] }""", "needs the clip's name")]
    [InlineData(/*lang=json,strict*/ """{ "format": 2, "tracks": [ { "clips": [ { "t": 2, "clip": "a" }, { "t": 1, "clip": "b" } ] } ] }""", "must be after the clip before it")]
    [InlineData(/*lang=json,strict*/ """{ "format": 2, "tracks": [ { "clips": [ { "t": 0, "clip": "a", "from": 3, "to": 2 } ] } ] }""", "must be after from")]
    [InlineData(/*lang=json,strict*/ """{ "format": 2, "tracks": [ { "clips": [] }, { "clips": [] } ] }""", "one per actor")]
    public void BadTracksAreReported(string json, string expected)
    {
        var result = ChartLoader.Load(json);

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, e => e.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void KeepingAClipWritesTheTrackRaisesTheFormatAndCoversTheEnd()
    {
        const string text = /*lang=json*/ """
            {
              // my chart
              "format": 1,
              "name": "Mine",
              "actors": [ { "id": "boss" } ],
              "end": 5
            }
            """;
        var chart = Load(text);
        var clip = Recorded("boss-clip-1", 0f, 9.5f);

        var kept = TrackWriter.KeepWhole(text, chart, "boss", clip, 0);
        var after = Load(kept);

        Assert.Equal(new TrackClip(0, "boss-clip-1", 0), Assert.Single(Assert.Single(after.Tracks).Clips));
        Assert.Equal(10.5, after.End);

        var again = TrackWriter.KeepWhole(kept, after, "boss", Recorded("boss-clip-2", 0f, 3f), 0);
        Assert.Equal("boss-clip-2", Assert.Single(Assert.Single(Load(again).Tracks).Clips).Clip);
        Assert.Equal(10.5, Load(again).End);
    }

    [Fact]
    public void AClipRecordedLateStartsAtZeroAndTheTrackPlacesIt()
    {
        const string text = /*lang=json,strict*/ """{ "actors": [ { "id": "boss" } ], "end": 5 }""";
        var after = Load(TrackWriter.KeepWhole(text, Load(text), "boss", Recorded("boss-clip-1", 0f, 4f), 40));

        Assert.Equal(new TrackClip(40, "boss-clip-1", 0), Assert.Single(Assert.Single(after.Tracks).Clips));
        Assert.Equal(45, after.End);
    }

    [Fact]
    public void ClipNamesCountUp()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"Basalt-{Guid.NewGuid():N}");
        try
        {
            Assert.Equal("boss-clip-1", new ClipLibrary(folder).NextName("boss"));
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "boss-clip-1.json"), "{}");
            File.WriteAllText(Path.Combine(folder, "boss-clip-7.json"), "{}");
            File.WriteAllText(Path.Combine(folder, "other-clip-9.json"), "{}");
            Assert.Equal("boss-clip-8", new ClipLibrary(folder).NextName("boss"));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void AClipKeepsItsPathAndMovesOnDisk()
    {
        var clip = Recorded("boss-clip-1", 2f, 4f);
        Clip withMoves = new(clip.Name, clip.Frames) { Moves = new[] { new ClipMove(2.5f, "Straight"), new ClipMove(3f, "Kick") } };

        (var read, var error) = ClipFile.Read(ClipFile.Write(withMoves), "x");

        Assert.Null(error);
        Assert.Equal(withMoves.Moves, read!.Moves);
        Assert.Equal(2f, read.Start);
        Assert.Equal(new Vector2(0.5f, 11f), read.Frames[0].Offset);
        Assert.Equal(new Vector2(0.75f, 11f), read.Sample(3f).Offset!.Value, new Vector2Comparer());
    }

    [Fact]
    public void AnOldClipStillReads()
    {
        var old = SyntheticClips.Sway("old", 1f);

        (var read, _) = ClipFile.Read(ClipFile.Write(old), "x");

        Assert.Empty(read!.Moves);
        Assert.Null(read.Frames[0].Offset);
        Assert.DoesNotContain("\"P\"", ClipFile.Write(old), StringComparison.Ordinal);
    }

    /// <summary>A clip from <paramref name="start"/> to <paramref name="end"/> walking from x 0.5 to 1.0 at z 11.</summary>
    private static Clip Recorded(string name, float start, float end)
    {
        var pose = new ClipPose(Vector3.Zero, Quaternion.Identity);
        var frames = new List<ClipFrame>();
        for (var t = start; t <= end + 0.0001f; t += 0.5f)
        {
            var x = 0.5f + (0.5f * (t - start) / Math.Max(0.001f, end - start));
            frames.Add(new ClipFrame(t, pose, pose, pose, new Vector2(x, 11f)));
        }

        return new Clip(name, frames);
    }

    private static Chart Load(string json)
    {
        var result = ChartLoader.Load(json);
        return result.Chart ?? throw new InvalidOperationException(string.Join("; ", result.Errors));
    }

    private sealed class Vector2Comparer : IEqualityComparer<Vector2>
    {
        public bool Equals(Vector2 a, Vector2 b) => Vector2.Distance(a, b) < 0.001f;

        public int GetHashCode(Vector2 v) => 0;
    }
}

public class LaneSetupTests
{
    [Fact]
    public void LaneSetupSavesArenaLaneAndActorsAndKeepsTheRest()
    {
        const string text = /*lang=json*/ """
            {
              // keep
              "name": "Setup",
              "lane": { "length": 12, "width": 3 },
              "actors": [ { "id": "boss", "at": { "x": 0, "z": 12 } } ],
              "events": [ { "t": 1, "move": "Straight" } ]
            }
            """;
        var chart = ChartLoader.Load(text).Chart!;
        (var lane, var actors) = LaneSetupWriter.Resize(chart.Lane, chart.Actors, 14f, 3.5f);

        var saved = LaneSetupWriter.Write(text, new ArenaSpec(ArenaKind.Gym, 1.25f, -3f, 90f), lane, actors);
        var after = ChartLoader.Load(saved).Chart!;

        Assert.Equal(new ArenaSpec(ArenaKind.Gym, 1.25f, -3f, 90f), after.Arena);
        Assert.Equal(new Lane(14f, 3.5f), after.Lane);
        Assert.Equal(14f, after.Actors[0].At.Z);
        Assert.Single(after.Events);
    }

    [Fact]
    public void ResizingIsClampedAndLeavesMidLaneActors()
    {
        var actors = new[] { new ActorSpec("a", "A", new Spot(0, 12), "you"), new ActorSpec("b", "B", new Spot(1, 6), "you") };

        (var lane, var moved) = LaneSetupWriter.Resize(new Lane(12, 3), actors, 100f, 0.1f);

        Assert.Equal(new Lane(LaneSetupWriter.MaxLength, LaneSetupWriter.MinWidth), lane);
        Assert.Equal(LaneSetupWriter.MaxLength, moved[0].At.Z);
        Assert.Equal(6f, moved[1].At.Z);
    }
}

public class TempoTests
{
    [Fact]
    public void BeatsCrossTempoChanges()
    {
        var tempo = new TempoMap(new[] { new TempoChange(0, 120), new TempoChange(64, 140) });

        Assert.Equal(32, tempo.Seconds(64), 6);
        Assert.Equal(32 + (6 * 60.0 / 140), tempo.Seconds(70), 6);
        Assert.Equal(70, tempo.Beat(tempo.Seconds(70)), 6);
        Assert.Equal(120, tempo.BpmAt(10));
        Assert.Equal(140, tempo.BpmAt(40));
    }

    [Fact]
    public void ChartsTimeBeatsThroughTheirTempoChanges()
    {
        var chart = ChartLoader.Load(/*lang=json,strict*/ """
            { "bpm": [ { "beat": 0, "bpm": 120 }, { "beat": 8, "bpm": 60 } ], "events": [ { "beat": 10, "move": "Straight" } ] }
            """).Chart!;

        Assert.Equal(4 + 2, Assert.Single(chart.Events).Time, 6);
        Assert.Equal(120, chart.Tempo!.BpmAt(3.9));
        Assert.Equal(60, chart.Tempo.BpmAt(4.1));
    }

    [Fact]
    public void ASingleBpmStillWorks() =>
        Assert.Equal(2, Assert.Single(ChartLoader.Load(/*lang=json,strict*/ """{ "bpm": 120, "events": [ { "beat": 4, "move": "Straight" } ] }""").Chart!.Events).Time, 6);

    [Theory]
    [InlineData(/*lang=json,strict*/ """{ "bpm": [ { "beat": 4, "bpm": 120 } ] }""", "must be 0")]
    [InlineData(/*lang=json,strict*/ """{ "bpm": [ { "beat": 0, "bpm": 120 }, { "beat": 0, "bpm": 140 } ] }""", "must be after")]
    [InlineData(/*lang=json,strict*/ """{ "bpm": [ { "beat": 0, "bpm": -1 } ] }""", "must be over 0")]
    [InlineData(/*lang=json,strict*/ """{ "bpm": [ { "beat": 0 } ] }""", "needs a beat and a bpm")]
    [InlineData(/*lang=json,strict*/ """{ "bpm": "fast" }""", "a list of tempo changes")]
    [InlineData(/*lang=json,strict*/ """{ "bpm": 0 }""", "must be over 0")]
    public void BadTemposAreReported(string json, string expected) =>
        Assert.Contains(ChartLoader.Load(json).Errors, e => e.Contains(expected, StringComparison.Ordinal));
}

public class LaneScaleTests
{
    [Fact]
    public void ShorteningTheLaneTakesTheChartWithIt()
    {
        var text = ChartTemplate.Json("Scaled", "me");
        var before = ChartLoader.Load(text).Chart!;
        (var lane, var actors) = LaneSetupWriter.Resize(before.Lane, before.Actors, 9f, before.Lane.Width);

        var saved = LaneSetupWriter.ScaleDepth(LaneSetupWriter.Write(text, new ArenaSpec(ArenaKind.Pit, 1.7f, -7.2f, 354f), lane, actors), 9f / 12f, 9f);
        var after = ChartLoader.Load(saved);

        Assert.True(after.Ok, string.Join("; ", after.Errors));
        Assert.All(after.Chart!.Events.OfType<ProjectileEvent>(), p => Assert.InRange(p.From.Z, 0.1f, 9f));
        Assert.Equal(7.5f, after.Chart.Events.OfType<ProjectileEvent>().First().From.Z, 2);
        Assert.Equal(8.25f, after.Chart.Events.OfType<PlaceEvent>().Single().At.Z, 2);
    }
}

public class FacingTests
{
    [Theory]
    [InlineData(350f, 10f, 0.5f, 0f)]
    [InlineData(10f, 350f, 0.5f, 0f)]
    [InlineData(90f, 180f, 0.5f, 135f)]
    [InlineData(0f, 270f, 0.5f, 315f)]
    public void FacingsInterpolateTheShortWayRound(float from, float to, float t, float expected) =>
        Assert.Equal(expected, AngleHelpers.Lerp(from, to, t) % 360f, 2);

    [Fact]
    public void AClipKeepsItsFacingsOnDisk()
    {
        var pose = new ClipPose(Vector3.Zero, Quaternion.Identity);
        var clip = new Clip("t", new[]
        {
            new ClipFrame(0f, pose, pose, pose, Vector2.Zero, 350f),
            new ClipFrame(1f, pose, pose, pose, Vector2.Zero, 30f),
        });

        (var read, _) = ClipFile.Read(ClipFile.Write(clip), "t");

        Assert.Equal(350f, read!.Frames[0].Yaw);
        Assert.Equal(10f, read.Sample(0.5f).Yaw!.Value, 2);
    }

    [Fact]
    public void LaneYawsRoundTrip()
    {
        var lane = Anchor.CreateFacingDirection(Vector3.Zero, new Vector3(1f, 0f, 0f));

        Assert.Equal(90f, lane.Yaw, 2);
        Assert.Equal(180f, lane.ToLocalYaw(270f), 2);
        Assert.Equal(270f, lane.ToWorldYaw(180f), 2);
    }
}

public class ZoneAndRulesTests
{
    [Fact]
    public void WithoutAZoneThePlayerKeepsTwoMetresFromTheFarEnd()
    {
        var chart = ChartLoader.Load(/*lang=json,strict*/ """{ "lane": { "length": 12, "width": 3 } }""").Chart!;

        Assert.Equal(new Zone(10f, Zone.DefaultHeight), chart.Zone);
        Assert.Equal(SpawnRule.All, chart.Rules.Spawn);
        Assert.Equal(new Zone(1.5f, Zone.DefaultHeight), ChartLoader.Load(/*lang=json,strict*/ """{ "lane": { "length": 3, "width": 3 } }""").Chart!.Zone);
    }

    [Fact]
    public void AChartSetsItsZoneAndRules()
    {
        var chart = ChartLoader.Load(/*lang=json,strict*/ """{ "zone": { "length": 6, "height": 2.5 }, "rules": { "spawn": "Discs" } }""").Chart!;

        Assert.Equal(new Zone(6f, 2.5f), chart.Zone);
        Assert.Equal(SpawnRule.Discs, chart.Rules.Spawn);
    }

    [Theory]
    [InlineData(/*lang=json,strict*/ """{ "zone": { "length": 20 } }""", "zone.length")]
    [InlineData(/*lang=json,strict*/ """{ "zone": { "height": 0.5 } }""", "zone.height")]
    [InlineData(/*lang=json,strict*/ """{ "rules": { "spawn": "some" } }""", "rules.spawn")]
    public void BadZonesAndRulesAreReported(string json, string expected) =>
        Assert.Contains(ChartLoader.Load(json).Errors, e => e.Contains(expected, StringComparison.Ordinal));

    [Fact]
    public void TheZoneGoesWithTheLane()
    {
        const string text = /*lang=json,strict*/ """{ "lane": { "length": 12, "width": 3 }, "zone": { "length": 8, "height": 3 } }""";
        var scaled = LaneSetupWriter.ScaleDepth(text, 0.75f, 9f);

        Assert.Equal(6f, ChartLoader.Load(scaled).Chart!.Zone.Length);
    }
}

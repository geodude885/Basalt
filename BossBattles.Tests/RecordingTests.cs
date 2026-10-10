using System.Numerics;
using BossBattles.Services.Charts;
using BossBattles.Services.Clips;
using Xunit;

namespace BossBattles.Tests;

public sealed class ChartDocumentTests
{
    private const string Chart = /*lang=json*/ """
        {
          // keep me
          "name": "Song", /* and me */
          "lane": { "length": 12, "width": 3 }, // trailing
          "events": [ { "t": 1, "move": "Straight" } ]
        }
        """;

    [Fact]
    public void ReplacingAValueKeepsEverythingElse()
    {
        var text = ChartDocument.SetRaw(Chart, "lane", /*lang=json,strict*/ """{ "length": 9, "width": 2 }""");

        Assert.Contains(/*lang=json,strict*/ """{ "length": 9, "width": 2 }""", text, StringComparison.Ordinal);
        Assert.Contains("// keep me", text, StringComparison.Ordinal);
        Assert.Contains("/* and me */", text, StringComparison.Ordinal);
        Assert.Contains("// trailing", text, StringComparison.Ordinal);
        Assert.Equal(9f, ChartLoader.Load(text).Chart!.Lane.Length);
    }

    [Fact]
    public void ANewPropertyIsAddedAtTheEnd()
    {
        var text = ChartDocument.SetRaw(Chart, "end", "20");

        Assert.Equal(20, ChartLoader.Load(text).Chart!.End);
        Assert.Contains("// keep me", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AddingAfterATrailingCommaOrToAnEmptyObjectStaysValid()
    {
        Assert.Equal(5, ChartLoader.Load(ChartDocument.SetRaw(/*lang=json*/ """{ "name": "A", }""", "end", "5")).Chart!.End);
        Assert.Equal(5, ChartLoader.Load(ChartDocument.SetRaw("{}", "end", "5")).Chart!.End);
        Assert.Equal(5, ChartLoader.Load(ChartDocument.SetRaw(/*lang=json*/ "{ \"name\": \"A\" // last\n}", "end", "5")).Chart!.End);
    }

    [Fact]
    public void KeysAreMatchedAsTheLoaderMatchesThem()
    {
        var text = ChartDocument.SetRaw(/*lang=json,strict*/ """{ "Name": "Old" }""", "name", "\"New\"");

        Assert.Equal("New", ChartLoader.Load(text).Chart!.Name);
        Assert.DoesNotContain("Old", text, StringComparison.Ordinal);
    }

    [Fact]
    public void StringsAndCommentsWithBracesDoNotConfuseIt()
    {
        const string tricky = /*lang=json*/ """
            {
              "name": "a } { ] [ \" quote",
              // "end": 99, }
              "description": "/* not a comment */"
            }
            """;

        Chart chart = ChartLoader.Load(ChartDocument.SetRaw(tricky, "end", "7")).Chart!;
        Assert.Equal(7, chart.End);
        Assert.Equal("a } { ] [ \" quote", chart.Name);
        Assert.Equal("/* not a comment */", chart.Info.Description);
    }

    [Fact]
    public void NotAnObjectIsRefused() => Assert.Throws<FormatException>(() => ChartDocument.SetRaw("[1]", "end", "1"));
}

public sealed class TrackTests
{
    [Fact]
    public void TracksLoadAndPlayEachTakeUntilTheNext()
    {
        Chart chart = Load(/*lang=json,strict*/ """
            {
              "format": 2,
              "actors": [ { "id": "boss" } ],
              "tracks": [ { "actor": "boss", "takes": [
                { "t": 0, "clip": "boss-take-1" },
                { "t": 8, "clip": "boss-take-2", "from": 8, "to": 12 },
                { "t": 12, "clip": "boss-take-1", "from": 12 }
              ] } ]
            }
            """);

        Track track = Assert.Single(chart.Tracks);
        Assert.Equal(3, track.Takes.Count);

        var plays = new ChartRunner(chart).Schedule.OfType<PlayTake>().ToList();
        Assert.Equal(new PlayTake(0, "boss", "boss-take-1", 0, 8), plays[0]);
        Assert.Equal(new PlayTake(8, "boss", "boss-take-2", 8, 12), plays[1]);
        Assert.Equal(new PlayTake(12, "boss", "boss-take-1", 12, null), plays[2]);
    }

    [Theory]
    [InlineData(/*lang=json,strict*/ """{ "tracks": [ { "takes": [ { "t": 0, "clip": "a" } ] } ] }""", "format")]
    [InlineData(/*lang=json,strict*/ """{ "format": 2, "tracks": [ { "actor": "nobody", "takes": [] } ] }""", "is not one of the chart's actors")]
    [InlineData(/*lang=json,strict*/ """{ "format": 2, "tracks": [ { "takes": [ { "t": 0 } ] } ] }""", "needs the take's name")]
    [InlineData(/*lang=json,strict*/ """{ "format": 2, "tracks": [ { "takes": [ { "t": 2, "clip": "a" }, { "t": 1, "clip": "b" } ] } ] }""", "must be after the take before it")]
    [InlineData(/*lang=json,strict*/ """{ "format": 2, "tracks": [ { "takes": [ { "t": 0, "clip": "a", "from": 3, "to": 2 } ] } ] }""", "must be after from")]
    [InlineData(/*lang=json,strict*/ """{ "format": 2, "tracks": [ { "takes": [] }, { "takes": [] } ] }""", "one per actor")]
    public void BadTracksAreReported(string json, string expected)
    {
        ChartLoadResult result = ChartLoader.Load(json);

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, e => e.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void KeepingATakeWritesTheTrackRaisesTheFormatAndCoversTheEnd()
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
        Chart chart = Load(text);
        Clip take = Take("boss-take-1", 0f, 9.5f);

        var kept = Takes.KeepWhole(text, chart, "boss", take);
        Chart after = Load(kept);

        Assert.Contains("// my chart", kept, StringComparison.Ordinal);
        Assert.Equal(new TakeSegment(0, "boss-take-1", 0), Assert.Single(Assert.Single(after.Tracks).Takes));
        Assert.Equal(10.5, after.End);

        var again = Takes.KeepWhole(kept, after, "boss", Take("boss-take-2", 0f, 3f));
        Assert.Equal("boss-take-2", Assert.Single(Assert.Single(Load(again).Tracks).Takes).Clip);
        Assert.Equal(10.5, Load(again).End);
    }

    [Fact]
    public void TakeNamesCountUp()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"BossBattles-{Guid.NewGuid():N}");
        try
        {
            Assert.Equal("boss-take-1", Takes.NextName(folder, "boss"));
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "boss-take-1.json"), "{}");
            File.WriteAllText(Path.Combine(folder, "boss-take-7.json"), "{}");
            File.WriteAllText(Path.Combine(folder, "other-take-9.json"), "{}");
            Assert.Equal("boss-take-8", Takes.NextName(folder, "boss"));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void ATakeKeepsItsPathAndMovesOnDisk()
    {
        Clip take = Take("boss-take-1", 2f, 4f);
        Clip withMoves = new(take.Name, take.Frames) { Moves = new[] { new ClipMove(2.5f, "Straight"), new ClipMove(3f, "Kick") } };

        (Clip? read, var error) = ClipFile.Read(ClipFile.Write(withMoves), "x");

        Assert.Null(error);
        Assert.Equal(withMoves.Moves, read!.Moves);
        Assert.Equal(2f, read.Start);
        Assert.Equal(new Vector2(0.5f, 11f), read.Frames[0].Lane);
        Assert.Equal(new Vector2(0.75f, 11f), read.Sample(3f).Lane!.Value, new Vector2Comparer());
    }

    [Fact]
    public void AnOldClipStillReads()
    {
        Clip old = SyntheticClips.Sway("old", 1f);

        (Clip? read, _) = ClipFile.Read(ClipFile.Write(old), "x");

        Assert.Empty(read!.Moves);
        Assert.Null(read.Frames[0].Lane);
        Assert.DoesNotContain("\"P\"", ClipFile.Write(old), StringComparison.Ordinal);
    }

    /// <summary>A take from <paramref name="start"/> to <paramref name="end"/> walking from x 0.5 to 1.0 at z 11.</summary>
    private static Clip Take(string name, float start, float end)
    {
        var pose = new ClipPose(Vector3.Zero, Quaternion.Identity);
        var frames = new List<ClipFrame>();
        for (var t = start; t <= end + 0.0001f; t += 0.5f)
        {
            var x = 0.5f + (0.5f * (t - start) / Math.Max(0.001f, end - start));
            frames.Add(new ClipFrame(t, pose, pose, pose, pose, Vector2.Zero, Vector2.Zero, new Vector2(x, 11f)));
        }

        return new Clip(name, frames);
    }

    private static Chart Load(string json)
    {
        ChartLoadResult result = ChartLoader.Load(json);
        return result.Chart ?? throw new InvalidOperationException(string.Join("; ", result.Errors));
    }

    private sealed class Vector2Comparer : IEqualityComparer<Vector2>
    {
        public bool Equals(Vector2 a, Vector2 b) => Vector2.Distance(a, b) < 0.001f;

        public int GetHashCode(Vector2 v) => 0;
    }
}

public sealed class SectionTests
{
    private static readonly TakeSegment[] Whole = { new(0, "take-1", 0) };

    [Fact]
    public void PunchingIntoOneTakeCutsItAndResumesIt()
    {
        IReadOnlyList<TakeSegment> result = Takes.PunchIn(Whole, "take-2", 4, 8);

        Assert.Equal(new[]
        {
            new TakeSegment(0, "take-1", 0, 4),
            new TakeSegment(4, "take-2", 4, 8),
            new TakeSegment(8, "take-1", 8),
        }, result);
    }

    [Fact]
    public void PunchingAgainOverAnEarlierPunchReplacesWhatItCovers()
    {
        IReadOnlyList<TakeSegment> once = Takes.PunchIn(Whole, "take-2", 4, 8);
        IReadOnlyList<TakeSegment> twice = Takes.PunchIn(once, "take-3", 6, 10);

        Assert.Equal(new[]
        {
            new TakeSegment(0, "take-1", 0, 4),
            new TakeSegment(4, "take-2", 4, 6),
            new TakeSegment(6, "take-3", 6, 10),
            new TakeSegment(10, "take-1", 10),
        }, twice);
    }

    [Fact]
    public void PunchingFromTheStartOrOverEverythingLeavesNothingStale()
    {
        Assert.Equal(new[] { new TakeSegment(0, "take-2", 0, 3), new TakeSegment(3, "take-1", 3) }, Takes.PunchIn(Whole, "take-2", 0, 3));
        Assert.Equal(new[] { new TakeSegment(0, "take-2", 0, 30) }, Takes.PunchIn(new[] { new TakeSegment(0, "take-1", 0, 20) }, "take-2", 0, 30));
    }

    [Fact]
    public void ASplicedTrackLoadsAndPlaysInOrder()
    {
        const string text = /*lang=json,strict*/ """{ "format": 2, "actors": [ { "id": "boss" } ], "tracks": [ { "actor": "boss", "takes": [ { "t": 0, "clip": "take-1", "from": 0 } ] } ] }""";
        Chart chart = ChartLoader.Load(text).Chart!;
        Clip take = new("take-2", new[] { Frame(4f), Frame(8f) });

        Chart after = ChartLoader.Load(Takes.KeepSection(text, chart, "boss", take, 4, 8)).Chart!;
        var plays = new ChartRunner(after).Schedule.OfType<PlayTake>().ToList();

        Assert.Equal(new[] { "take-1", "take-2", "take-1" }, plays.Select(p => p.Clip));
        Assert.Equal(new double?[] { 4, 8, null }, plays.Select(p => p.Until));
    }

    [Fact]
    public void StartingPartWayBringsForwardWhatIsInForce()
    {
        Chart chart = ChartLoader.Load(/*lang=json,strict*/ """
            {
              "format": 2,
              "actors": [ { "id": "boss" } ],
              "phases": [ { "name": "A", "start": 0 }, { "name": "B", "start": 3 } ],
              "events": [
                { "t": 1, "projectile": { "structure": "Disc", "from": { "x": 0, "y": 1, "z": 9 }, "speed": 5 } },
                { "t": 5.2, "projectile": { "structure": "Disc", "from": { "x": 0, "y": 1, "z": 9 }, "speed": 5, "telegraph": 0.5 } },
                { "t": 6, "move": "Straight" }
              ],
              "tracks": [ { "takes": [ { "t": 0, "clip": "take-1" } ] } ]
            }
            """).Chart!;

        var runner = new ChartRunner(chart, startAt: 5);
        var first = runner.Advance(5).ToList();

        Assert.Equal("B", Assert.Single(first.OfType<EnterPhase>()).Phase.Name);
        Assert.Equal(5, Assert.Single(first.OfType<PlayTake>()).From);
        Assert.Single(first.OfType<SpawnProjectile>());
        Assert.Empty(first.OfType<LaunchProjectile>());
        Assert.Single(first.OfType<PlaceActor>());
        Assert.Contains(runner.Advance(6), c => c is RunMove);
    }

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
        Chart chart = ChartLoader.Load(text).Chart!;
        (Lane lane, IReadOnlyList<ActorSpec> actors) = ChartSetup.Resize(chart.Lane, chart.Actors, 14f, 3.5f);

        var saved = ChartSetup.Write(text, new ArenaSpec(ArenaKind.Gym, 1.25f, -3f, 90f), lane, actors);
        Chart after = ChartLoader.Load(saved).Chart!;

        Assert.Contains("// keep", saved, StringComparison.Ordinal);
        Assert.Equal(new ArenaSpec(ArenaKind.Gym, 1.25f, -3f, 90f), after.Arena);
        Assert.Equal(new Lane(14f, 3.5f), after.Lane);
        Assert.Equal(14f, after.Actors[0].At.Z);
        Assert.Single(after.Events);
    }

    [Fact]
    public void ResizingIsClampedAndLeavesMidLaneActors()
    {
        ActorSpec[] actors = new[] { new ActorSpec("a", "A", new Spot(0, 12), "you"), new ActorSpec("b", "B", new Spot(1, 6), "you") };

        (Lane lane, IReadOnlyList<ActorSpec> moved) = ChartSetup.Resize(new Lane(12, 3), actors, 100f, 0.1f);

        Assert.Equal(new Lane(ChartSetup.MaxLength, ChartSetup.MinWidth), lane);
        Assert.Equal(ChartSetup.MaxLength, moved[0].At.Z);
        Assert.Equal(6f, moved[1].At.Z);
    }

    private static ClipFrame Frame(float t)
    {
        var pose = new ClipPose(Vector3.Zero, Quaternion.Identity);
        return new ClipFrame(t, pose, pose, pose, pose, Vector2.Zero, Vector2.Zero, Vector2.Zero);
    }
}

public sealed class TempoTests
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
        Chart chart = ChartLoader.Load(/*lang=json,strict*/ """
            { "bpm": [ { "beat": 0, "bpm": 120 }, { "beat": 8, "bpm": 60 } ], "events": [ { "beat": 10, "move": "Straight" } ] }
            """).Chart!;

        Assert.Equal(4 + 2, Assert.Single(chart.Events).Time, 6);
        Assert.Equal(new[] { new TempoChange(0, 120), new TempoChange(8, 60) }, chart.Tempo!.Changes);
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

public sealed class LaneScaleTests
{
    [Fact]
    public void ShorteningTheLaneTakesTheChartWithIt()
    {
        var text = ChartTemplate.Json("Scaled", "me");
        Chart before = ChartLoader.Load(text).Chart!;
        (Lane lane, IReadOnlyList<ActorSpec> actors) = ChartSetup.Resize(before.Lane, before.Actors, 9f, before.Lane.Width);

        var saved = ChartSetup.ScaleDepth(ChartSetup.Write(text, new ArenaSpec(ArenaKind.Pit, 1.7f, -7.2f, 354f), lane, actors), 9f / 12f, 9f);
        ChartLoadResult after = ChartLoader.Load(saved);

        Assert.True(after.Ok, string.Join("; ", after.Errors));
        Assert.All(after.Chart!.Events.OfType<ProjectileEvent>(), p => Assert.InRange(p.From.Z, 0.1f, 9f));
        Assert.Equal(7.5f, after.Chart.Events.OfType<ProjectileEvent>().First().From.Z, 2);
        Assert.Equal(8.25f, after.Chart.Events.OfType<PlaceEvent>().Single().At.Z, 2);
        Assert.Contains("// The boss does one of its own moves", saved.Replace("\r\n", "\n"), StringComparison.Ordinal);
    }
}

public sealed class FacingTests
{
    [Theory]
    [InlineData(350f, 10f, 0.5f, 0f)]
    [InlineData(10f, 350f, 0.5f, 0f)]
    [InlineData(90f, 180f, 0.5f, 135f)]
    [InlineData(0f, 270f, 0.5f, 315f)]
    public void FacingsInterpolateTheShortWayRound(float from, float to, float t, float expected) =>
        Assert.Equal(expected, Clip.LerpDegrees(from, to, t) % 360f, 2);

    [Fact]
    public void ATakeKeepsItsFacingsOnDisk()
    {
        var pose = new ClipPose(Vector3.Zero, Quaternion.Identity);
        var take = new Clip("t", new[]
        {
            new ClipFrame(0f, pose, pose, pose, pose, Vector2.Zero, Vector2.Zero, Vector2.Zero, 350f),
            new ClipFrame(1f, pose, pose, pose, pose, Vector2.Zero, Vector2.Zero, Vector2.Zero, 30f),
        });

        (Clip? read, _) = ClipFile.Read(ClipFile.Write(take), "t");

        Assert.Equal(350f, read!.Frames[0].Yaw);
        Assert.Equal(10f, read.Sample(0.5f).Yaw!.Value, 2);
    }

    [Fact]
    public void LaneYawsRoundTrip()
    {
        var lane = LaneFrame.From(Vector3.Zero, new Vector3(1f, 0f, 0f));

        Assert.Equal(90f, lane.Bearing, 2);
        Assert.Equal(180f, lane.ToLaneYaw(270f), 2);
        Assert.Equal(270f, lane.ToWorldYaw(180f), 2);
    }
}

public sealed class ZoneAndRulesTests
{
    [Fact]
    public void WithoutAZoneYouKeepTwoMetresFromTheFarEnd()
    {
        Chart chart = ChartLoader.Load(/*lang=json,strict*/ """{ "lane": { "length": 12, "width": 3 } }""").Chart!;

        Assert.Equal(new Zone(10f, Zone.DefaultHeight), chart.Zone);
        Assert.Equal(SpawnRule.All, chart.Rules.Spawn);
        Assert.Equal(new Zone(1.5f, Zone.DefaultHeight), ChartLoader.Load(/*lang=json,strict*/ """{ "lane": { "length": 3, "width": 3 } }""").Chart!.Zone);
    }

    [Fact]
    public void AChartSetsItsZoneAndRules()
    {
        Chart chart = ChartLoader.Load(/*lang=json,strict*/ """{ "zone": { "length": 6, "height": 2.5 }, "rules": { "spawn": "Discs" } }""").Chart!;

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
        var scaled = ChartSetup.ScaleDepth(text, 0.75f, 9f);

        Assert.Equal(6f, ChartLoader.Load(scaled).Chart!.Zone.Length);
    }
}

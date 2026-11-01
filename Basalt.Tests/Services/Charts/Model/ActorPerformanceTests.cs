using System.Numerics;
using Basalt.DevTools.Testing;
using Basalt.Services.Charts;
using Basalt.Services.Charts.Files;
using Basalt.Services.Charts.Model;
using Basalt.Services.Clips;
using Xunit;

namespace Basalt.Tests.Services.Charts.Model;

public class ActorPerformanceTests
{
    [Fact]
    public void TracksLoadAndPlayEachClipUntilTheNext()
    {
        var chart = Load(/*lang=json,strict*/ """
            {
              "format": 2,
              "actors": [ { "id": "boss" } ],
              "performances": [ { "actor": "boss", "clips": [
                { "t": 0, "clip": "boss-clip-1" },
                { "t": 8, "clip": "boss-clip-2", "start": 8, "end": 12 },
                { "t": 12, "clip": "boss-clip-1", "start": 12 }
              ] } ]
            }
            """);

        var performance = Assert.Single(chart.Performances);
        Assert.Equal(3, performance.Clips.Count);

        var plays = new ChartRunner(chart).Schedule.OfType<PlayClip>().ToList();
        Assert.Equal(new PlayClip(0, "boss", "boss-clip-1", 0, 8), plays[0]);
        Assert.Equal(new PlayClip(8, "boss", "boss-clip-2", 8, 12), plays[1]);
        Assert.Equal(new PlayClip(12, "boss", "boss-clip-1", 12, null), plays[2]);
    }

    [Theory]
    [InlineData(/*lang=json,strict*/ """{ "format": 2, "performances": [ { "actor": "nobody", "clips": [] } ] }""", "is not one of the chart's actors")]
    [InlineData(/*lang=json,strict*/ """{ "format": 2, "performances": [ { "clips": [ { "t": 0 } ] } ] }""", "needs the clip's name")]
    [InlineData(/*lang=json,strict*/ """{ "format": 2, "performances": [ { "clips": [ { "t": 2, "clip": "a" }, { "t": 1, "clip": "b" } ] } ] }""", "must be after the clip before it")]
    [InlineData(/*lang=json,strict*/ """{ "format": 2, "performances": [ { "clips": [ { "t": 0, "clip": "a", "start": 3, "end": 2 } ] } ] }""", "must be after clip start")]
    [InlineData(/*lang=json,strict*/ """{ "format": 2, "performances": [ { "clips": [] }, { "clips": [] } ] }""", "more than one performance")]
    public void BadTracksAreReported(string json, string expected)
    {
        var result = ChartLoader.Load(json);

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, e => e.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void KeepingAClipWritesThePerformanceAndCoversTheEnd()
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

        var kept = ChartWriter.WritePerformances(text, chart, "boss", clip, 0);
        var after = Load(kept);

        Assert.Equal(new PerformanceClip(0, "boss-clip-1", 0), Assert.Single(Assert.Single(after.Performances).Clips));
        Assert.Equal(10.5, after.End);

        var again = ChartWriter.WritePerformances(kept, after, "boss", Recorded("boss-clip-2", 0f, 3f), 0);
        Assert.Equal("boss-clip-2", Assert.Single(Assert.Single(Load(again).Performances).Clips).Clip);
        Assert.Equal(10.5, Load(again).End);
    }

    [Fact]
    public void AClipRecordedLateStartsAtZeroAndTheTrackPlacesIt()
    {
        const string text = /*lang=json,strict*/ """{ "actors": [ { "id": "boss" } ], "end": 5 }""";
        var after = Load(ChartWriter.WritePerformances(text, Load(text), "boss", Recorded("boss-clip-1", 0f, 4f), 40));

        Assert.Equal(new PerformanceClip(40, "boss-clip-1", 0), Assert.Single(Assert.Single(after.Performances).Clips));
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
        Assert.Equal(new Vector2(0.75f, 11f), read.GetClipFrameAt(3f).Offset!.Value, new Vector2Comparer());
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

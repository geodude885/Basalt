using System.Numerics;
using Basalt.DevTools.Testing;
using Basalt.Services.Charts;
using Xunit;

namespace Basalt.Tests;

public class ChartLoaderTests
{
    private const string Sample = /*lang=json*/ """
        {
          // comments and trailing commas are fine - charts are hand-written
          "name": "Training Dummy",
          "lane": { "length": 12, "width": 3 },
          "actors": [
            { "id": "dummy", "name": "Training Dummy", "at": { "x": 0, "z": 12 } },
            { "id": "add", "at": { "x": -1, "z": 10, "facing": 20 }, "look": "default" },
          ],
          "phases": [
            { "name": "Warm-up", "start": 0 },
            { "name": "Open", "start": 20, "scoreMultiplier": 2, "actor": "add", "at": { "x": 1, "z": 10 } },
          ],
          "events": [
            { "t": 2, "projectile": { "structure": "Disc", "from": { "x": -1, "y": 1.4, "z": 10 },
                                      "to": { "x": 0.8, "y": 1.2 }, "speed": 12, "telegraph": 0.6, "by": "dummy" } },
            { "t": 3, "projectile": { "structure": "Ball", "from": { "x": 1, "y": 1, "z": 11 }, "to": "player", "speed": 10 } },
            { "t": 5, "actor": "add", "move": "Straight" },
            { "t": 8, "at": { "x": -1, "z": 11 } },
          ],
          "end": 40,
        }
        """;

    [Fact]
    public void LoadsTheSampleChart()
    {
        var result = ChartLoader.Load(Sample);

        Assert.True(result.Ok, string.Join("; ", result.Errors));
        var chart = result.Chart!;

        Assert.Equal("Training Dummy", chart.Name);
        Assert.Equal(new Lane(12, 3), chart.Lane);
        Assert.Equal(40, chart.End);

        Assert.Equal(2, chart.Actors.Count);
        Assert.Equal(new ActorSpec("dummy", "Training Dummy", new Spot(0, 12), ChartLoader.DefaultLook), chart.Actors[0]);
        Assert.Equal(new ActorSpec("add", "add", new Spot(-1, 10, 20), "default"), chart.Actors[1]);
        Assert.Same(chart.Actors[0], chart.DefaultActor);

        Assert.Equal("add", chart.Phases[1].ActorId);
        Assert.Equal(new Spot(1, 10), chart.Phases[1].At);

        var disc = Assert.IsType<ProjectileEvent>(chart.Events[0]);
        Assert.Equal(new Vector3(-1f, 1.4f, 10f), disc.From);
        Assert.Equal(Aim.Point(0.8f, 1.2f), disc.To);
        Assert.Equal("dummy", disc.By);
        Assert.Equal(1.4, disc.SpawnTime, 6);

        var ball = Assert.IsType<ProjectileEvent>(chart.Events[1]);
        Assert.True(ball.To.AtPlayer);
        Assert.Null(ball.By);
        Assert.Equal(ChartLoader.DefaultTelegraph, ball.Telegraph);

        Assert.Equal("add", Assert.IsType<MoveEvent>(chart.Events[2]).ActorId);
        Assert.Equal("dummy", Assert.IsType<PlaceEvent>(chart.Events[3]).ActorId);
    }

    [Fact]
    public void BeatsBecomeSecondsAtTheChartsBpm()
    {
        var result = ChartLoader.Load(/*lang=json,strict*/ """
            { "bpm": 120, "phases": [ { "start": 0 }, { "startBeat": 16 } ], "events": [ { "beat": 8, "move": "Kick" } ] }
            """);

        Assert.True(result.Ok, string.Join("; ", result.Errors));
        Assert.Equal(4.0, result.Chart!.Events[0].Time, 6);
        Assert.Equal(8.0, result.Chart.Phases[1].Start, 6);
    }

    [Fact]
    public void DefaultsFillTheGaps()
    {
        var chart = ChartLoader.Load(/*lang=json,strict*/ """{ "events": [ { "t": 10, "move": "Kick" } ] }""").Chart!;

        Assert.Equal("Untitled", chart.Name);
        Assert.Equal(new Lane(ChartLoader.DefaultLaneLength, ChartLoader.DefaultLaneWidth), chart.Lane);
        Assert.Equal(new ActorSpec(ChartLoader.DefaultActorId, "Actor", new Spot(0, ChartLoader.DefaultLaneLength), ChartLoader.DefaultLook), Assert.Single(chart.Actors));
        Assert.Single(chart.Phases);
        Assert.Equal(10 + ChartLoader.DefaultEndPadding, chart.End, 6);
    }

    [Fact]
    public void ReportsEveryProblemWithItsPath()
    {
        var result = ChartLoader.Load(/*lang=json,strict*/ """
            {
              "actors": [ { "id": "a" }, { "id": "a" } ],
              "events": [
                { "t": 1, "projectile": { "from": { "x": 0, "y": 1, "z": 30 }, "speed": 0 } },
                { "t": 2 },
                { "beat": 4, "move": "Kick" },
                { "t": 0.2, "projectile": { "structure": "Disc", "from": { "x": 0, "y": 1, "z": 10 }, "speed": 10, "telegraph": 1 } },
                { "t": 3, "move": "Kick", "at": { "x": 0, "z": 5 } },
                { "t": 4, "actor": "ghost", "move": "Kick" },
                { "t": 5, "projectile": { "structure": "Disc", "from": { "x": 0, "y": 1, "z": 5 }, "speed": 10, "to": "nowhere" } }
              ]
            }
            """);

        Assert.False(result.Ok);
        var all = string.Join("\n", result.Errors);

        Assert.Contains("actors[1].id \"a\" is used twice", all, StringComparison.Ordinal);
        Assert.Contains("events[0].projectile.structure is required", all, StringComparison.Ordinal);
        Assert.Contains("events[0].projectile.from.z (30) must be in front of you and within the lane", all, StringComparison.Ordinal);
        Assert.Contains("events[0].projectile.speed must be over 0", all, StringComparison.Ordinal);
        Assert.Contains("events[1] needs exactly one of projectile, move or at (has 0)", all, StringComparison.Ordinal);
        Assert.Contains("events[2].beat needs the chart's bpm", all, StringComparison.Ordinal);
        Assert.Contains("events[3].projectile would appear before the battle starts", all, StringComparison.Ordinal);
        Assert.Contains("events[4] needs exactly one of projectile, move or at (has 2)", all, StringComparison.Ordinal);
        Assert.Contains("events[5].actor \"ghost\" is not one of the chart's actors", all, StringComparison.Ordinal);
        Assert.Contains("events[6].projectile.to must be \"player\" or", all, StringComparison.Ordinal);
    }

    [Fact]
    public void PhasesMustStartAtZeroAndGoForwards()
    {
        var result = ChartLoader.Load(/*lang=json,strict*/ """{ "phases": [ { "start": 5 }, { "start": 3 } ] }""");

        var all = string.Join("\n", result.Errors);
        Assert.Contains("phases[0] must start at 0", all, StringComparison.Ordinal);
        Assert.Contains("phases[1].start (3) must be after phases[0] (5)", all, StringComparison.Ordinal);
    }

    [Fact]
    public void BrokenJsonIsAnErrorNotAThrow()
    {
        var result = ChartLoader.Load("{ \"events\": [ ");

        Assert.False(result.Ok);
        Assert.StartsWith("not valid chart JSON", result.Errors[0], StringComparison.Ordinal);
    }

    [Fact]
    public void EveryStarterChartLoads()
    {
        foreach ((var file, var json) in StarterCharts.All)
        {
            var result = ChartLoader.Load(json);
            Assert.True(result.Ok, $"{file}: {string.Join("; ", result.Errors)}");
        }
    }

    [Fact]
    public void TheSelfTestChartLoads()
    {
        var result = ChartLoader.Load(SelfTestChart.Json);
        Assert.True(result.Ok, string.Join("; ", result.Errors));
    }

    [Fact]
    public void AnActorKeepsItsRecordersMeasurementThroughASave()
    {
        const string Text = /*lang=json,strict*/ """{ "actors": [ { "id": "boss", "measurement": { "height": 1.72, "armSpan": 1.68 } } ] }""";
        var chart = ChartLoader.Load(Text).Chart!;
        Assert.Equal(new BodySize(1.72f, 1.68f), chart.Actors[0].Measurement);

        var saved = ChartLoader.Load(LaneSetupWriter.WriteActors(Text, chart.Actors)).Chart!;
        Assert.Equal(chart.Actors[0].Measurement, saved.Actors[0].Measurement);

        Assert.Null(ChartLoader.Load(/*lang=json,strict*/ """{ "actors": [ { "id": "boss" } ] }""").Chart!.Actors[0].Measurement);
        Assert.Contains(ChartLoader.Load(/*lang=json,strict*/ """{ "actors": [ { "measurement": { "height": 9 } } ] }""").Errors,
            e => e.StartsWith("actors[0].measurement", StringComparison.Ordinal));
    }
}

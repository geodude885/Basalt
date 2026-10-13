using BossBattles.Services.Charts;
using Xunit;

namespace BossBattles.Tests;

public sealed class VisualsTests
{
    // The player's own value of every parameter, for the timeline to fade from and back to.
    private static float Yours(string pack, string key) => 1f;

    [Fact]
    public void ReadsCuesInSecondsAndBeats()
    {
        Chart chart = Load(/*lang=json,strict*/ """
            {
              "bpm": 120,
              "visuals": [
                { "beat": 8, "pack": "Grayscale", "set": { "Strength": 1 }, "fade": 2 },
                { "t": 1, "pack": "UltraShade", "set": { "Saturation": 0.5, "_Vignette": 0.6 } },
                { "t": 10, "pack": "UltraShade", "off": true, "fade": 1 }
              ]
            }
            """);

        Assert.Equal(new[] { 1.0, 4.0, 10.0 }, chart.Visuals.Select(v => v.Time));
        VisualCue first = chart.Visuals[0];
        Assert.Equal("UltraShade", first.Pack);
        Assert.Equal(0.5f, first.Set["saturation"]);
        Assert.True(chart.Visuals[2].Off);
        Assert.Equal(2, chart.Visuals[1].Fade);
    }

    [Theory]
    [InlineData(/*lang=json,strict*/ """{ "visuals": [ { "t": 1, "set": { "Strength": 1 } } ] }""", "needs a pack")]
    [InlineData(/*lang=json,strict*/ """{ "visuals": [ { "t": 1, "pack": "Grayscale" } ] }""", "needs \"set\"")]
    [InlineData(/*lang=json,strict*/ """{ "visuals": [ { "t": 1, "pack": "Grayscale", "off": true, "set": { "Strength": 1 } } ] }""", "both \"off\" and \"set\"")]
    [InlineData(/*lang=json,strict*/ """{ "visuals": [ { "t": 1, "pack": "Grayscale", "on": true, "fade": -1 } ] }""", "fade cannot be negative")]
    [InlineData(/*lang=json,strict*/ """{ "visuals": [ { "beat": 1, "pack": "Grayscale", "on": true } ] }""", "needs the chart's bpm")]
    public void RejectsBrokenCues(string json, string expected)
    {
        ChartLoadResult result = ChartLoader.Load(json);

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, e => e.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void ACueFadesFromYoursToItsValues()
    {
        VisualCue[] cues = new[] { Cue(2, "Pack", 1, ("Saturation", 0f)) };

        Assert.Empty(LookTimeline.Evaluate(cues, 1.9, Yours));
        Assert.Equal(0.5f, LookTimeline.Evaluate(cues, 2.5, Yours)["Pack"].Values["Saturation"], 3);
        PackLook done = LookTimeline.Evaluate(cues, 9, Yours)["Pack"];
        Assert.True(done.On);
        Assert.Equal(0f, done.Values["Saturation"], 3);
    }

    [Fact]
    public void ALaterCueFadesOnFromWhereTheLastWasCutOff()
    {
        // The first fade is a quarter done (0.75) when the second cuts in and heads for 2.
        VisualCue[] cues = new[] { Cue(0, "Pack", 4, ("Contrast", 0f)), Cue(1, "Pack", 1, ("Contrast", 2f)) };

        Assert.Equal(0.75f + ((2f - 0.75f) * 0.5f), LookTimeline.Evaluate(cues, 1.5, Yours)["Pack"].Values["Contrast"], 3);
    }

    [Fact]
    public void ValuesNotInALaterCueStayAsTheyWere()
    {
        VisualCue[] cues = new[] { Cue(0, "Pack", 0, ("A", 0f), ("B", 0f)), Cue(1, "Pack", 0, ("B", 3f)) };

        PackLook look = LookTimeline.Evaluate(cues, 2, Yours)["Pack"];
        Assert.Equal(0f, look.Values["A"]);
        Assert.Equal(3f, look.Values["B"]);
    }

    [Fact]
    public void OffFadesBackToYoursThenLetsGo()
    {
        VisualCue[] cues = new[]
        {
            Cue(0, "Pack", 0, ("Strength", 0f)),
            new VisualCue(4, "Pack", new Dictionary<string, float>(), Off: true, Fade: 2),
        };

        PackLook fading = LookTimeline.Evaluate(cues, 5, Yours)["Pack"];
        Assert.True(fading.On);
        Assert.Equal(0.5f, fading.Values["Strength"], 3);

        PackLook gone = LookTimeline.Evaluate(cues, 6, Yours)["Pack"];
        Assert.False(gone.On);
        Assert.Empty(gone.Values);
    }

    [Fact]
    public void PacksAreIndependentAndNamesIgnoreCase()
    {
        VisualCue[] cues = new[] { Cue(0, "Grayscale", 0, ("Strength", 0.4f)), Cue(1, "grayscale", 0, ("strength", 0.8f)), Cue(0, "Other", 0, ("X", 2f)) };

        Dictionary<string, PackLook> looks = LookTimeline.Evaluate(cues, 2, Yours);
        Assert.Equal(2, looks.Count);
        Assert.Equal(0.8f, looks["GRAYSCALE"].Values["Strength"]);
        Assert.Equal(2f, looks["Other"].Values["X"]);
    }

    private static VisualCue Cue(double t, string pack, double fade, params (string Key, float Value)[] set) =>
        new(t, pack, set.ToDictionary(s => s.Key, s => s.Value, StringComparer.OrdinalIgnoreCase), false, fade);

    private static Chart Load(string json)
    {
        ChartLoadResult result = ChartLoader.Load(json);
        Assert.True(result.Ok, string.Join("; ", result.Errors));
        return result.Chart!;
    }
}

public sealed class LaneColourTests
{
    [Theory]
    [InlineData("#ff8000", 1f, 128f / 255f, 0f, 1f)]
    [InlineData("66ccff33", 0.4f, 0.8f, 1f, 0.2f)]
    public void ParsesHexColours(string text, float r, float g, float b, float a)
    {
        Assert.True(Rgba.TryParse(text, out Rgba c));
        Assert.Equal(r, c.R, 3);
        Assert.Equal(g, c.G, 3);
        Assert.Equal(b, c.B, 3);
        Assert.Equal(a, c.A, 3);
    }

    [Fact]
    public void ReadsColoursEitherSpelling()
    {
        Chart uk = ChartLoader.Load(/*lang=json,strict*/ """{ "colours": { "edges": "#33ccff", "walls": "#66ccff33" } }""").Chart!;
        Chart us = ChartLoader.Load(/*lang=json,strict*/ """{ "colors": { "ends": "#ff3399" } }""").Chart!;

        Assert.Equal(0.2f, uk.Colours.Walls!.Value.A, 3);
        Assert.Null(uk.Colours.Ends);
        Assert.NotNull(us.Colours.Ends);
        Assert.Null(ChartLoader.Load("{}").Chart!.Colours.Walls);
    }

    [Fact]
    public void RejectsABadColour()
    {
        ChartLoadResult result = ChartLoader.Load(/*lang=json,strict*/ """{ "colours": { "edges": "orange" } }""");

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, e => e.Contains("colours.edges", StringComparison.Ordinal));
    }
}

public sealed class PhaseColourTests
{
    [Fact]
    public void APhaseLaysItsColoursOverTheCharts()
    {
        Chart chart = ChartLoader.Load(/*lang=json,strict*/ """
            {
              "colours": { "edges": "#00ff00", "walls": "#66ccff33" },
              "phases": [ { "name": "A", "start": 0 }, { "name": "B", "start": 5, "colours": { "ends": "#ff0000", "walls": "none" } } ]
            }
            """).Chart!;

        LaneColours first = chart.ColoursIn(chart.Phases[0]);
        LaneColours second = chart.ColoursIn(chart.Phases[1]);

        Assert.True(first.ShowsWalls);
        Assert.Equal(chart.Colours.Edges, second.Edges);
        Assert.Equal(1f, second.Ends!.Value.R);
        Assert.False(second.ShowsWalls);
        Assert.True(chart.EverShowsWalls);
    }

    [Fact]
    public void WallsShownOnlyInAPhaseStillGetPanes()
    {
        Chart chart = ChartLoader.Load(/*lang=json,strict*/ """{ "phases": [ { "start": 0 }, { "start": 2, "colors": { "walls": "#ffffff40" } } ] }""").Chart!;

        Assert.False(chart.ColoursIn(chart.Phases[0]).ShowsWalls);
        Assert.True(chart.EverShowsWalls);
    }

    [Fact]
    public void ABadPhaseColourNamesThePhase()
    {
        ChartLoadResult result = ChartLoader.Load(/*lang=json,strict*/ """{ "phases": [ { "start": 0, "colours": { "edges": "blue" } } ] }""");

        Assert.Contains(result.Errors, e => e.Contains("phases[0].colours.edges", StringComparison.Ordinal));
    }
}

public sealed class ActorLookTests
{
    [Fact]
    public void ShiftstonesAreYoursUnlessNamed()
    {
        Chart chart = ChartLoader.Load(/*lang=json,strict*/ """
            {
              "actors": [
                { "id": "a" },
                { "id": "b", "shiftstones": "you" },
                { "id": "c", "shiftstones": [ "Vigor", "FlowStone" ] },
                { "id": "d", "shiftstones": [] }
              ]
            }
            """).Chart!;

        Assert.Null(chart.Actors[0].Shiftstones);
        Assert.Null(chart.Actors[1].Shiftstones);
        Assert.Equal(new[] { "Vigor", "FlowStone" }, chart.Actors[2].Shiftstones);
        Assert.Empty(chart.Actors[3].Shiftstones!);
    }

    [Theory]
    [InlineData(/*lang=json,strict*/ """{ "actors": [ { "shiftstones": [ "A", "B", "C" ] } ] }""", "two at most")]
    [InlineData(/*lang=json,strict*/ """{ "actors": [ { "shiftstones": "Vigor" } ] }""", "must be \"you\" or a list")]
    public void RejectsBadShiftstones(string json, string expected) => Assert.Contains(ChartLoader.Load(json).Errors, e => e.Contains(expected, StringComparison.Ordinal));

    [Fact]
    public void AChartMadeInGameKeepsItsMakersLookAndStones()
    {
        ChartLoadResult result = ChartLoader.Load(ChartTemplate.Json("Mine", "maker", "look \"string\"", new[] { "VigorStone", "FlowStone" }));

        Assert.True(result.Ok, string.Join("; ", result.Errors));
        ActorSpec boss = result.Chart!.Actors[0];
        Assert.Equal("look \"string\"", boss.Look);
        Assert.Equal(new[] { "VigorStone", "FlowStone" }, boss.Shiftstones);
    }

    [Fact]
    public void LaneSetupKeepsAnActorsStones()
    {
        Chart chart = ChartLoader.Load(/*lang=json,strict*/ """{ "actors": [ { "id": "boss", "shiftstones": [ "Vigor" ] } ] }""").Chart!;

        var saved = ChartSetup.Write(/*lang=json,strict*/ """{ "actors": [] }""", chart.Arena, chart.Lane, chart.Actors);

        Assert.Equal(new[] { "Vigor" }, ChartLoader.Load(saved).Chart!.Actors[0].Shiftstones);
    }
}

public sealed class ProjectileReachTests
{
    [Fact]
    public void ShotsMayStartOutsideTheLaneButNotTooFar()
    {
        static ChartLoadResult Shot(float x, float z)
        {
            return ChartLoader.Load($$"""
                { "lane": { "length": 8, "width": 3 },
                  "events": [ { "t": 2, "projectile": { "structure": "Ball", "from": { "x": {{x}}, "y": 1, "z": {{z}} }, "speed": 9 } } ] }
                """);
        }

        Assert.True(Shot(0, 12).Ok);
        Assert.True(Shot(-5, 4).Ok);
        Assert.False(Shot(0, 15).Ok);
        Assert.False(Shot(8, 4).Ok);
        Assert.False(Shot(0, 0).Ok);
    }

    [Fact]
    public void ResizingTheLaneKeepsShotsPastItsEnd()
    {
        var chart = /*lang=json,strict*/ """{ "events": [ { "t": 1, "projectile": { "structure": "Ball", "from": { "x": 0, "y": 1, "z": 10 }, "speed": 9 } } ], "phases": [ { "start": 0, "at": { "x": 0, "z": 8 } } ] }""";

        var scaled = ChartSetup.ScaleDepth(chart, 0.5f, 4f);

        Assert.Contains("\"z\": 5", scaled, StringComparison.Ordinal);
        Assert.Contains("\"z\": 4", scaled, StringComparison.Ordinal);
    }
}

public sealed class MimeClipTests
{
    [Fact]
    public void AMimedClipIsReadAndPlayedMimed()
    {
        Chart chart = ChartLoader.Load(/*lang=json,strict*/ """
            { "format": 2, "tracks": [ { "clips": [ { "t": 0, "clip": "a" }, { "t": 5, "clip": "b", "mime": true } ] } ] }
            """).Chart!;

        Assert.False(chart.Tracks[0].Clips[0].Mime);
        Assert.True(chart.Tracks[0].Clips[1].Mime);

        var plays = new ChartRunner(chart).Advance(10).OfType<PlayClip>().ToList();
        Assert.False(plays[0].Mime);
        Assert.True(plays[1].Mime);
    }

    [Fact]
    public void KeepingAMimedClipWritesItMimed()
    {
        Chart chart = ChartLoader.Load(/*lang=json,strict*/ """{ "actors": [ { "id": "boss" } ] }""").Chart!;
        var pose = new Services.Clips.ClipPose(System.Numerics.Vector3.Zero, System.Numerics.Quaternion.Identity);
        Services.Clips.ClipFrame Frame(float t)
        {
            return new(t, pose, pose, pose);
        }

        var clip = new Services.Clips.Clip("boss-clip-1", new[] { Frame(0), Frame(4) });

        var whole = TrackEdits.KeepWhole(/*lang=json,strict*/ """{ "actors": [ { "id": "boss" } ] }""", chart, "boss", clip, 0, mime: true);
        Assert.True(ChartLoader.Load(whole).Chart!.Tracks[0].Clips[0].Mime);

        IReadOnlyList<TrackClip> punched = TrackEdits.PunchIn(new[] { new TrackClip(0, "old", 0) }, "new", 2, 3, mime: true);
        Assert.False(punched[0].Mime);
        Assert.True(punched[1].Mime);
        Assert.False(punched[2].Mime);
    }
}

public sealed class GroundedAndCleanupTests
{
    [Fact]
    public void PillarsCubesAndWallsAreGroundedUnlessToldOtherwise()
    {
        Chart chart = ChartLoader.Load(/*lang=json,strict*/ """
            { "events": [
              { "t": 1, "projectile": { "structure": "Wall", "from": { "x": 0, "y": 0, "z": 5 }, "speed": 8 } },
              { "t": 2, "projectile": { "structure": "RockCube", "from": { "x": 0, "y": 0, "z": 5 }, "speed": 8, "grounded": false } },
              { "t": 3, "projectile": { "structure": "Ball", "from": { "x": 0, "y": 1, "z": 5 }, "speed": 8 } },
              { "t": 4, "projectile": { "structure": "Ball", "from": { "x": 0, "y": 1, "z": 5 }, "speed": 8, "grounded": true } }
            ] }
            """).Chart!;

        Assert.Equal(new[] { true, false, false, true }, chart.Events.OfType<ProjectileEvent>().Select(p => p.IsGrounded));
    }

    [Fact]
    public void CleanupDefaultsToThreeSecondsAndCanBeOff()
    {
        Assert.Equal(3, ChartLoader.Load("{}").Chart!.Rules.CleanupSeconds);
        Assert.Equal(0, ChartLoader.Load(/*lang=json,strict*/ """{ "rules": { "cleanup": 0 } }""").Chart!.Rules.CleanupSeconds);
        Assert.Equal(SpawnRule.Discs, ChartLoader.Load(/*lang=json,strict*/ """{ "rules": { "spawn": "discs", "cleanup": 5 } }""").Chart!.Rules.Spawn);
        Assert.False(ChartLoader.Load(/*lang=json,strict*/ """{ "rules": { "cleanup": -1 } }""").Ok);
    }
}

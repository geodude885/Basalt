using Basalt.Services.Charts;
using Basalt.Services.Charts.Model;
using Xunit;

namespace Basalt.Tests;

public class VisualsTests
{
    // The player's own value of every parameter, for the timeline to fade from and back to.
    private static float Yours(string pack, string key) => 1f;

    [Fact]
    public void ReadsCuesInSecondsAndBeats()
    {
        var chart = Load(/*lang=json,strict*/ """
            {
              "bpm": 120,
              "shaders": [
                { "beat": 8, "pack": "Grayscale", "values": { "Strength": 1 }, "fade": 2 },
                { "t": 1, "pack": "UltraShade", "values": { "Saturation": 0.5, "_Vignette": 0.6 } },
                { "t": 10, "pack": "UltraShade", "off": true, "fade": 1 }
              ]
            }
            """);

        Assert.Equal(new[] { 1.0, 4.0, 10.0 }, chart.Shaders.Select(v => v.Time));
        var first = chart.Shaders[0];
        Assert.Equal("UltraShade", first.Pack);
        Assert.Equal(0.5f, first.Values["saturation"]);
        Assert.True(chart.Shaders[2].Off);
        Assert.Equal(2, chart.Shaders[1].Fade);
    }

    [Theory]
    [InlineData(/*lang=json,strict*/ """{ "shaders": [ { "t": 1, "values": { "Strength": 1 } } ] }""", "needs a pack")]
    [InlineData(/*lang=json,strict*/ """{ "shaders": [ { "t": 1, "pack": "Grayscale" } ] }""", "needs \"values\"")]
    [InlineData(/*lang=json,strict*/ """{ "shaders": [ { "t": 1, "pack": "Grayscale", "off": true, "values": { "Strength": 1 } } ] }""", "both \"off\" and \"values\"")]
    [InlineData(/*lang=json,strict*/ """{ "shaders": [ { "t": 1, "pack": "Grayscale", "on": true, "fade": -1 } ] }""", "fade cannot be negative")]
    [InlineData(/*lang=json,strict*/ """{ "shaders": [ { "beat": 1, "pack": "Grayscale", "on": true } ] }""", "needs the chart's bpm")]
    public void RejectsBrokenCues(string json, string expected)
    {
        var result = ChartLoader.Load(json);

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, e => e.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void ACueFadesFromThePlayersOwnToItsValues()
    {
        var cues = new[] { Cue(2, "Pack", 1, ("Saturation", 0f)) };

        Assert.Empty(LookTimeline.Evaluate(cues, 1.9, Yours));
        Assert.Equal(0.5f, LookTimeline.Evaluate(cues, 2.5, Yours)["Pack"].Values["Saturation"], 3);
        var done = LookTimeline.Evaluate(cues, 9, Yours)["Pack"];
        Assert.True(done.On);
        Assert.Equal(0f, done.Values["Saturation"], 3);
    }

    [Fact]
    public void ALaterCueFadesOnFromWhereTheLastWasCutOff()
    {
        // The first fade is a quarter done (0.75) when the second cuts in and heads for 2.
        var cues = new[] { Cue(0, "Pack", 4, ("Contrast", 0f)), Cue(1, "Pack", 1, ("Contrast", 2f)) };

        Assert.Equal(0.75f + ((2f - 0.75f) * 0.5f), LookTimeline.Evaluate(cues, 1.5, Yours)["Pack"].Values["Contrast"], 3);
    }

    [Fact]
    public void ValuesNotInALaterCueStayAsTheyWere()
    {
        var cues = new[] { Cue(0, "Pack", 0, ("A", 0f), ("B", 0f)), Cue(1, "Pack", 0, ("B", 3f)) };

        var look = LookTimeline.Evaluate(cues, 2, Yours)["Pack"];
        Assert.Equal(0f, look.Values["A"]);
        Assert.Equal(3f, look.Values["B"]);
    }

    [Fact]
    public void OffFadesBackToThePlayersOwnThenLetsGo()
    {
        var cues = new[]
        {
            Cue(0, "Pack", 0, ("Strength", 0f)),
            new ShaderCue(4, "Pack", new Dictionary<string, float>(), Off: true, Fade: 2),
        };

        var fading = LookTimeline.Evaluate(cues, 5, Yours)["Pack"];
        Assert.True(fading.On);
        Assert.Equal(0.5f, fading.Values["Strength"], 3);

        var gone = LookTimeline.Evaluate(cues, 6, Yours)["Pack"];
        Assert.False(gone.On);
        Assert.Empty(gone.Values);
    }

    [Fact]
    public void PacksAreIndependentAndNamesIgnoreCase()
    {
        var cues = new[] { Cue(0, "Grayscale", 0, ("Strength", 0.4f)), Cue(1, "grayscale", 0, ("strength", 0.8f)), Cue(0, "Other", 0, ("X", 2f)) };

        var looks = LookTimeline.Evaluate(cues, 2, Yours);
        Assert.Equal(2, looks.Count);
        Assert.Equal(0.8f, looks["GRAYSCALE"].Values["Strength"]);
        Assert.Equal(2f, looks["Other"].Values["X"]);
    }

    private static ShaderCue Cue(double t, string pack, double fade, params (string Key, float Value)[] set) =>
        new(t, pack, set.ToDictionary(s => s.Key, s => s.Value, StringComparer.OrdinalIgnoreCase), false, fade);

    private static Chart Load(string json)
    {
        var result = ChartLoader.Load(json);
        Assert.True(result.Ok, string.Join("; ", result.Errors));
        return result.Chart!;
    }
}

public class LaneColourTests
{
    [Theory]
    [InlineData("#ff8000", 1f, 128f / 255f, 0f, 1f)]
    [InlineData("66ccff33", 0.4f, 0.8f, 1f, 0.2f)]
    public void ParsesHexColours(string text, float r, float g, float b, float a)
    {
        Assert.True(Rgba.TryParse(text, out var c));
        Assert.Equal(r, c.R, 3);
        Assert.Equal(g, c.G, 3);
        Assert.Equal(b, c.B, 3);
        Assert.Equal(a, c.A, 3);
    }

    [Fact]
    public void ReadsColours()
    {
        var chart = ChartLoader.Load(/*lang=json,strict*/ """{ "colours": { "sideLines": "#33ccff", "walls": "#66ccff33" } }""").Chart!;

        Assert.Equal(0.2f, chart.Colours.Walls!.Value.A, 3);
        Assert.NotNull(chart.Colours.SideLines);
        Assert.Null(chart.Colours.EndLines);
        Assert.Null(ChartLoader.Load("{}").Chart!.Colours.Walls);
    }

    [Fact]
    public void RejectsABadColour()
    {
        var result = ChartLoader.Load(/*lang=json,strict*/ """{ "colours": { "sideLines": "orange" } }""");

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, e => e.Contains("colours.sideLines", StringComparison.Ordinal));
    }
}

public class PhaseColourTests
{
    [Fact]
    public void APhaseLaysItsColoursOverTheCharts()
    {
        var chart = ChartLoader.Load(/*lang=json,strict*/ """
            {
              "colours": { "sideLines": "#00ff00", "walls": "#66ccff33" },
              "phases": [ { "name": "A", "t": 0 }, { "name": "B", "t": 5, "colours": { "endLines": "#ff0000", "walls": "none" } } ]
            }
            """).Chart!;

        var first = chart.ColoursIn(chart.Phases[0]);
        var second = chart.ColoursIn(chart.Phases[1]);
        var everShowsWalls = chart.Colours.ShowsWalls || chart.Phases.Any(p => p.Colours?.ShowsWalls == true);

        Assert.True(first.ShowsWalls);
        Assert.True(everShowsWalls);
        Assert.Equal(chart.Colours.SideLines, second.SideLines);
        Assert.Equal(1f, second.EndLines!.Value.R);
        Assert.False(second.ShowsWalls);
    }

    [Fact]
    public void WallsShownOnlyInAPhaseStillGetPanes()
    {
        var chart = ChartLoader.Load(/*lang=json,strict*/ """{ "phases": [ { "t": 0 }, { "t": 2, "colours": { "walls": "#ffffff40" } } ] }""").Chart!;
        var everShowsWalls = chart.Colours.ShowsWalls || chart.Phases.Any(p => p.Colours?.ShowsWalls == true);

        Assert.False(chart.ColoursIn(chart.Phases[0]).ShowsWalls);
        Assert.True(everShowsWalls);
    }

    [Fact]
    public void ABadPhaseColourNamesThePhase()
    {
        var result = ChartLoader.Load(/*lang=json,strict*/ """{ "phases": [ { "t": 0, "colours": { "sideLines": "blue" } } ] }""");

        Assert.Contains(result.Errors, e => e.Contains("phases[0].colours.sideLines", StringComparison.Ordinal));
    }
}

public class ActorLookTests
{
    [Fact]
    public void ShiftstonesAreThePlayersUnlessNamed()
    {
        var chart = ChartLoader.Load(/*lang=json,strict*/ """
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
        var result = ChartLoader.Load(ChartTemplate.Json("Mine", "maker", "look \"string\"", new[] { "VigorStone", "FlowStone" }));

        Assert.True(result.Ok, string.Join("; ", result.Errors));
        var boss = result.Chart!.Actors[0];
        Assert.Equal("look \"string\"", boss.Look);
        Assert.Equal(new[] { "VigorStone", "FlowStone" }, boss.Shiftstones);
    }

    [Fact]
    public void LaneSetupKeepsAnActorsStones()
    {
        var chart = ChartLoader.Load(/*lang=json,strict*/ """{ "actors": [ { "id": "boss", "shiftstones": [ "Vigor" ] } ] }""").Chart!;

        var saved = LaneSetupWriter.Write(/*lang=json,strict*/ """{ "actors": [] }""", chart.Arena, chart.Lane, chart.Actors);

        Assert.Equal(new[] { "Vigor" }, ChartLoader.Load(saved).Chart!.Actors[0].Shiftstones);
    }
}

public class ProjectileReachTests
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
        var chart = /*lang=json,strict*/ """{ "events": [ { "t": 1, "projectile": { "structure": "Ball", "from": { "x": 0, "y": 1, "z": 10 }, "speed": 9 } }, { "t": 2, "at": { "x": 0, "z": 8 } } ] }""";

        var scaled = LaneSetupWriter.ScaleDepth(chart, 0.5f, 4f);

        Assert.Contains("\"z\": 5", scaled, StringComparison.Ordinal);
        Assert.Contains("\"z\": 4", scaled, StringComparison.Ordinal);
    }
}

public class MimeClipTests
{
    [Fact]
    public void AMimedClipIsReadAndPlayedMimed()
    {
        var chart = ChartLoader.Load(/*lang=json,strict*/ """
            { "format": 2, "performances": [ { "clips": [ { "t": 0, "clip": "a" }, { "t": 5, "clip": "b", "mime": true } ] } ] }
            """).Chart!;

        Assert.False(chart.Performances[0].Clips[0].Mime);
        Assert.True(chart.Performances[0].Clips[1].Mime);

        var plays = new ChartRunner(chart).Advance(10).OfType<PlayClip>().ToList();
        Assert.False(plays[0].Mime);
        Assert.True(plays[1].Mime);
    }

    [Fact]
    public void KeepingAMimedClipWritesItMimed()
    {
        var chart = ChartLoader.Load(/*lang=json,strict*/ """{ "actors": [ { "id": "boss" } ] }""").Chart!;
        var pose = new Services.Clips.ClipPose(System.Numerics.Vector3.Zero, System.Numerics.Quaternion.Identity);
        Services.Clips.ClipFrame Frame(float t)
        {
            return new(t, pose, pose, pose);
        }

        var clip = new Services.Clips.Clip("boss-clip-1", new[] { Frame(0), Frame(4) });

        var whole = PerformanceWriter.KeepWhole(/*lang=json,strict*/ """{ "actors": [ { "id": "boss" } ] }""", chart, "boss", clip, 0, mime: true);
        Assert.True(ChartLoader.Load(whole).Chart!.Performances[0].Clips[0].Mime);
    }
}

public class GroundedAndCleanupTests
{
    [Fact]
    public void PillarsCubesAndWallsAreGroundedUnlessToldOtherwise()
    {
        var chart = ChartLoader.Load(/*lang=json,strict*/ """
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
    public void DiscsPillarsCubesAndWallsComeFromTheFloorAndAnythingGroundedRestsOnIt()
    {
        var chart = ChartLoader.Load(/*lang=json,strict*/ """
            { "events": [
              { "t": 1, "projectile": { "structure": "Disc", "from": { "x": 0, "y": 1.4, "z": 5 }, "speed": 8 } },
              { "t": 2, "projectile": { "structure": "RockCube", "from": { "x": 0, "y": 1, "z": 5 }, "speed": 8, "grounded": false } },
              { "t": 3, "projectile": { "structure": "Ball", "from": { "x": 0, "y": 1, "z": 5 }, "speed": 8 } },
              { "t": 4, "projectile": { "structure": "Ball", "from": { "x": 0, "y": 1, "z": 5 }, "speed": 8, "grounded": true } }
            ] }
            """).Chart!;
        var projectiles = chart.Events.OfType<ProjectileEvent>().ToList();

        Assert.Equal(new[] { true, true, false, false }, projectiles.Select(p => p.SpawnsFromFloor));
        Assert.Equal(new[] { true, true, false, true }, projectiles.Select(p => p.RestsOnFloor));
    }

    [Fact]
    public void CleanupDefaultsToThreeSecondsAndCanBeOff()
    {
        Assert.Equal(3, ChartLoader.Load("{}").Chart!.Rules.BreakStillAfterSeconds);
        Assert.Equal(0, ChartLoader.Load(/*lang=json,strict*/ """{ "rules": { "breakStillAfter": 0 } }""").Chart!.Rules.BreakStillAfterSeconds);
        Assert.Equal(SpawnRule.Discs, ChartLoader.Load(/*lang=json,strict*/ """{ "rules": { "playerSpawns": "discs", "breakStillAfter": 5 } }""").Chart!.Rules.PlayerSpawns);
        Assert.False(ChartLoader.Load(/*lang=json,strict*/ """{ "rules": { "breakStillAfter": -1 } }""").Ok);
    }

    [Fact]
    public void BreakingBehindIsOffUnlessAsked()
    {
        Assert.False(ChartLoader.Load("{}").Chart!.Rules.BreakBehind);
        Assert.False(ChartLoader.Load(/*lang=json,strict*/ """{ "rules": { "breakStillAfter": 5 } }""").Chart!.Rules.BreakBehind);

        var rules = ChartLoader.Load(/*lang=json,strict*/ """{ "rules": { "breakBehind": true } }""").Chart!.Rules;
        Assert.True(rules.BreakBehind);
        Assert.Equal(Rules.DefaultBreakStillAfterSeconds, rules.BreakStillAfterSeconds);
    }
}

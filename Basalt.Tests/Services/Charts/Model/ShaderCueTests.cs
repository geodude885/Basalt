using Basalt.Services.Charts;
using Basalt.Services.Charts.Files;
using Basalt.Services.Charts.Model;
using Xunit;

namespace Basalt.Tests.Services.Charts.Model;

public class ShaderCueTests
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
    [InlineData(/*lang=json,strict*/ """{ "shaders": [ { "t": 1, "values": { "Strength": 1 } } ] }""", "needs a RumbleReshaded pack name")]
    [InlineData(/*lang=json,strict*/ """{ "shaders": [ { "t": 1, "pack": "Grayscale", "off": true, "values": { "Strength": 1 } } ] }""", "can't set values and turn shaders off")]
    [InlineData(/*lang=json,strict*/ """{ "shaders": [ { "t": 1, "pack": "Grayscale", "fade": -1 } ] }""", "fade cannot be negative")]
    [InlineData(/*lang=json,strict*/ """{ "shaders": [ { "beat": 1, "pack": "Grayscale" } ] }""", "needs the chart's bpm")]
    public void RejectsBrokenCues(string json, string expected)
    {
        var result = ChartLoader.Load(json);

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, e => e.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void ACueWithoutValuesJustSwitchesThePackOn()
    {
        var result = ChartLoader.Load(/*lang=json,strict*/ """{ "shaders": [ { "t": 1, "pack": "Grayscale" } ] }""");

        Assert.Empty(Assert.Single(result.Chart!.Shaders).Values);
    }

    [Fact]
    public void ACueFadesFromThePlayersOwnToItsValues()
    {
        var cues = new[] { Cue(2, "Pack", 1, ("Saturation", 0f)) };

        Assert.Empty(CurrentVisuals.Evaluate(cues, 1.9, Yours));
        Assert.Equal(0.5f, CurrentVisuals.Evaluate(cues, 2.5, Yours)["Pack"].Values["Saturation"], 3);
        var done = CurrentVisuals.Evaluate(cues, 9, Yours)["Pack"];
        Assert.True(done.On);
        Assert.Equal(0f, done.Values["Saturation"], 3);
    }

    [Fact]
    public void ALaterCueFadesOnFromWhereTheLastWasCutOff()
    {
        // The first fade is a quarter done (0.75) when the second cuts in and heads for 2.
        var cues = new[] { Cue(0, "Pack", 4, ("Contrast", 0f)), Cue(1, "Pack", 1, ("Contrast", 2f)) };

        Assert.Equal(0.75f + ((2f - 0.75f) * 0.5f), CurrentVisuals.Evaluate(cues, 1.5, Yours)["Pack"].Values["Contrast"], 3);
    }

    [Fact]
    public void ValuesNotInALaterCueStayAsTheyWere()
    {
        var cues = new[] { Cue(0, "Pack", 0, ("A", 0f), ("B", 0f)), Cue(1, "Pack", 0, ("B", 3f)) };

        var look = CurrentVisuals.Evaluate(cues, 2, Yours)["Pack"];
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

        var fading = CurrentVisuals.Evaluate(cues, 5, Yours)["Pack"];
        Assert.True(fading.On);
        Assert.Equal(0.5f, fading.Values["Strength"], 3);

        var gone = CurrentVisuals.Evaluate(cues, 6, Yours)["Pack"];
        Assert.False(gone.On);
        Assert.Empty(gone.Values);
    }

    [Fact]
    public void PacksAreIndependentAndNamesIgnoreCase()
    {
        var cues = new[] { Cue(0, "Grayscale", 0, ("Strength", 0.4f)), Cue(1, "grayscale", 0, ("strength", 0.8f)), Cue(0, "Other", 0, ("X", 2f)) };

        var looks = CurrentVisuals.Evaluate(cues, 2, Yours);
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

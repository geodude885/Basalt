using Basalt.Services.Charts.Files;
using Basalt.Services.Charts.Model;
using Xunit;

namespace Basalt.Tests.Services.Charts.Model;

public class TempoTests
{
    [Fact]
    public void BeatsCrossTempoChanges()
    {
        var tempo = new TempoMap(new[] { new TempoSection(0, 120), new TempoSection(64, 140) });

        Assert.Equal(32, tempo.BeatToSeconds(64), 6);
        Assert.Equal(32 + (6 * 60.0 / 140), tempo.BeatToSeconds(70), 6);
        Assert.Equal(70, tempo.SecondsToBeat(tempo.BeatToSeconds(70)), 6);
        Assert.Equal(120, tempo.BpmAt(10));
        Assert.Equal(140, tempo.BpmAt(40));
    }

    [Fact]
    public void ChartsTimeBeatsThroughTheirTempoChanges()
    {
        var chart = ChartLoader.Load(/*lang=json,strict*/ """
            { "bpm": 120, "bpmChanges": [ { "beat": 8, "bpm": 60 } ], "events": [ { "beat": 10, "move": "Straight" } ] }
            """).Chart!;

        Assert.Equal(4 + 2, Assert.Single(chart.Events).Time, 6);
        Assert.Equal(120, chart.Tempo!.BpmAt(3.9));
        Assert.Equal(60, chart.Tempo.BpmAt(4.1));
    }

    [Fact]
    public void ASingleBpmStillWorks() =>
        Assert.Equal(2, Assert.Single(ChartLoader.Load(/*lang=json,strict*/ """{ "bpm": 120, "events": [ { "beat": 4, "move": "Straight" } ] }""").Chart!.Events).Time, 6);

    [Theory]
    [InlineData(/*lang=json,strict*/ """{ "bpmChanges": [ { "beat": 8, "bpm": 60 } ] }""", "needs a bpm")]
    [InlineData(/*lang=json,strict*/ """{ "bpm": 120, "bpmChanges": [ { "beat": 0, "bpm": 140 } ] }""", "must be after")]
    [InlineData(/*lang=json,strict*/ """{ "bpm": 120, "bpmChanges": [ { "beat": 4, "bpm": -1 } ] }""", "must be over 0")]
    [InlineData(/*lang=json,strict*/ """{ "bpm": 120, "bpmChanges": [ { "beat": 4 } ] }""", "needs a beat and a bpm")]
    [InlineData(/*lang=json,strict*/ """{ "bpm": "fast" }""", "not valid chart JSON")]
    [InlineData(/*lang=json,strict*/ """{ "bpm": 0 }""", "must be over 0")]
    public void BadTemposAreReported(string json, string expected) =>
        Assert.Contains(ChartLoader.Load(json).Errors, e => e.Contains(expected, StringComparison.Ordinal));
}

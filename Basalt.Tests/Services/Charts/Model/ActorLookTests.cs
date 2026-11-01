using Basalt.Services.Charts.Files;
using Xunit;

namespace Basalt.Tests.Services.Charts.Model;

public class ActorLookTests
{
    [Fact]
    public void AnActorWearsNoShiftstonesUnlessNamed()
    {
        var chart = ChartLoader.Load(/*lang=json,strict*/ """
            {
              "actors": [
                { "id": "a" },
                { "id": "b", "shiftstones": [ "Vigor", "FlowStone" ] },
                { "id": "c", "shiftstones": [] }
              ]
            }
            """).Chart!;

        Assert.Empty(chart.Actors[0].Shiftstones);
        Assert.Equal(new[] { "Vigor", "FlowStone" }, chart.Actors[1].Shiftstones);
        Assert.Empty(chart.Actors[2].Shiftstones);
    }

    [Theory]
    [InlineData(/*lang=json,strict*/ """{ "actors": [ { "shiftstones": [ "A", "B", "C" ] } ] }""", "more than 2 shiftstones")]
    [InlineData(/*lang=json,strict*/ """{ "actors": [ { "shiftstones": [ " " ] } ] }""", "must be a list of stone names")]
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

        var saved = ChartWriter.WriteLaneSetup(/*lang=json,strict*/ """{ "actors": [] }""", chart.Arena, chart.Lane, chart.Actors);

        Assert.Equal(new[] { "Vigor" }, ChartLoader.Load(saved).Chart!.Actors[0].Shiftstones);
    }
}

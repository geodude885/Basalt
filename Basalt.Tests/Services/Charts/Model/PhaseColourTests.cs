using Basalt.Services.Charts.Files;
using Xunit;

namespace Basalt.Tests.Services.Charts.Model;

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

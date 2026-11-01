using Basalt.Services.Charts;
using Basalt.Services.Charts.Files;
using Xunit;

namespace Basalt.Tests.Services.Charts.Model;

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

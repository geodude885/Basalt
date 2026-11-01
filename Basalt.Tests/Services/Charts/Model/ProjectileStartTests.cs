using Basalt.Services.Charts.Files;
using Xunit;

namespace Basalt.Tests.Services.Charts.Model;

public class ProjectileStartTests
{
    [Fact]
    public void ShotsMayStartOutsideTheLaneButNotBehindYou()
    {
        static ChartLoadResult Shot(float x, float z)
        {
            return ChartLoader.Load($$"""
                { "lane": { "length": 8, "width": 3 },
                  "events": [ { "t": 2, "projectile": { "structure": "Ball", "from": { "x": {{x}}, "y": 1, "z": {{z}} }, "speed": 9 } } ] }
                """);
        }

        Assert.True(Shot(0, 30).Ok);
        Assert.True(Shot(-20, 4).Ok);
        Assert.False(Shot(0, 0).Ok);
    }

    [Fact]
    public void ResizingTheLaneKeepsShotsPastItsEnd()
    {
        var chart = /*lang=json,strict*/ """{ "events": [ { "t": 1, "projectile": { "structure": "Ball", "from": { "x": 0, "y": 1, "z": 10 }, "speed": 9 } }, { "t": 2, "at": { "x": 0, "z": 8 } } ] }""";

        var scaled = ChartWriter.ScaleChartOnLaneLengthChange(chart, 0.5f, 4f);

        Assert.Contains("\"z\": 5", scaled, StringComparison.Ordinal);
        Assert.Contains("\"z\": 4", scaled, StringComparison.Ordinal);
    }
}
